using System.Text;


public sealed class NetworkMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();

    // Тип сообщения. Может быть любая строка вида: "DATA", "ACK", "ERROR"
    public string MessageType { get; set; } = "DATA";

    // Данные сообщения
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    // Время создания сообщения
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Идентификатор отправителя. Сервер запишет "SERVER", а клиенты свои Id
    public string SenderId { get; set; } = string.Empty;
}

// Класс для сериализации/десериализации сообщений
public static class MessageProtocol
{
    // Максимальный размер сообщения 10 МБ
    public const int MaxMessageSize = 10 * 1024 * 1024;

    // Сериализация сообщения в байтовый массив
    public static byte[] Serialize(NetworkMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // буфер в памяти, в который мы пишем байты
        var memory = new MemoryStream();

        try
        {
            // Примитивы (int, long, string) сразу пишутся в бинарном виде
            var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
            try
            {
                writer.Write(message.MessageId.ToByteArray());
                writer.Write(message.MessageType ?? string.Empty);
                writer.Write(message.Timestamp.ToBinary());
                writer.Write(message.SenderId ?? string.Empty);

                // Сначала пишем длину сообщения, а потом сами байты
                var payload = message.Payload ?? Array.Empty<byte>();
                writer.Write(payload.Length);

                if (payload.Length > 0)
                    writer.Write(payload);
            }
            finally
            {
                writer.Dispose();
            }

            return memory.ToArray();
        }
        finally
        {
            memory.Dispose();
        }
    }

    // Десериализация байтового массива в сообщение
    public static NetworkMessage Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var memory = new MemoryStream(data);

        try
        {
            var reader = new BinaryReader(memory, Encoding.UTF8, leaveOpen: true);

            try
            {
                var message = new NetworkMessage
                {
                    MessageId = new Guid(reader.ReadBytes(16)),
                    MessageType = reader.ReadString(),
                    Timestamp = DateTime.FromBinary(reader.ReadInt64()),
                    SenderId = reader.ReadString(),
                };

                var payloadLength = reader.ReadInt32();

                if (payloadLength < 0 || payloadLength > MaxMessageSize)
                {
                    throw new InvalidDataException($"Некорректная длина payload: {payloadLength}");
                }

                message.Payload = payloadLength > 0
                    ? reader.ReadBytes(payloadLength)
                    : Array.Empty<byte>();

                return message;
            }
            finally
            {
                reader.Dispose();
            }
        }
        finally
        {
            memory.Dispose();
        }
    }

    // Проверка корректности сообщения
    public static void Validate(NetworkMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.MessageId == Guid.Empty)
            throw new InvalidDataException("MessageId не должен быть пустым.");

        if (string.IsNullOrWhiteSpace(message.MessageType))
            throw new InvalidDataException("MessageType не задан.");

        if (message.Payload is null)
            throw new InvalidDataException("Payload не должен быть null.");

        if (message.Payload.Length > MaxMessageSize)
            throw new InvalidDataException(
                $"Размер Payload превышает допустимый максимум: {message.Payload.Length} > {MaxMessageSize}");
    }

    // Код ниже под вопросом??? Нужен ли
    // ---------------------------------------------------------------------
    // Запись сообщения в сетевой поток.
    // Сначала кладём 4 байта длины тела, потом само тело.
    // ---------------------------------------------------------------------
    public static async Task WriteMessageAsync(
        Stream stream,
        NetworkMessage message,
        CancellationToken cancellationToken = default)
    {
        Validate(message);

        var body = Serialize(message);
        if (body.Length > MaxMessageSize)
            throw new InvalidDataException(
                $"Тело сообщения превышает максимум: {body.Length} > {MaxMessageSize}");

        // 4 байта длины (int32, little-endian — стандартный порядок в .NET).
        var lengthPrefix = BitConverter.GetBytes(body.Length);

        // Записываем сначала длину, потом тело. На стороне приёма
        // мы будем делать то же самое в обратную сторону.
        await stream.WriteAsync(lengthPrefix, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    // ---------------------------------------------------------------------
    // Чтение сообщения из сетевого потока.
    // Возвращает null, если соединение было корректно закрыто
    // другой стороной до начала следующего сообщения.
    // ---------------------------------------------------------------------
    public static async Task<NetworkMessage?> ReadMessageAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        // Шаг 1: читаем РОВНО 4 байта длины.
        var lengthBytes = await ReadExactlyAsync(stream, 4, cancellationToken);
        if (lengthBytes is null)
            return null; // Поток закрылся "чисто" — это нормальное завершение.

        var bodyLength = BitConverter.ToInt32(lengthBytes, 0);

        // Защита от мусора и слишком больших значений.
        if (bodyLength <= 0 || bodyLength > MaxMessageSize)
            throw new InvalidDataException(
                $"Некорректная длина сообщения в префиксе: {bodyLength}");

        // Шаг 2: читаем РОВНО bodyLength байт тела сообщения.
        var body = await ReadExactlyAsync(stream, bodyLength, cancellationToken);
        if (body is null)
            throw new EndOfStreamException(
                "Поток закрылся посреди тела сообщения.");

        return Deserialize(body);
    }

    // ---------------------------------------------------------------------
    // Вспомогательный метод: прочитать РОВНО count байт из потока.
    //
    // ВАЖНО: TCP может вернуть в одном ReadAsync МЕНЬШЕ байт, чем мы
    // запросили (это и есть "частичное чтение"). Поэтому всегда нужно
    // читать в цикле, пока не наберём нужное количество.
    //
    // Возвращает null, если поток закрылся СРАЗУ (до первого байта) —
    // это нормальный разрыв соединения между сообщениями.
    // ---------------------------------------------------------------------
    private static async Task<byte[]?> ReadExactlyAsync(
        Stream stream,
        int count,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var totalRead = 0;

        while (totalRead < count)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, count - totalRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                // 0 байт = другая сторона закрыла соединение.
                if (totalRead == 0)
                    return null; // ничего ещё не успели прочесть — нормально

                // Иначе мы успели прочитать кусок, но не всё — это ошибка протокола.
                throw new EndOfStreamException(
                    $"Поток закрылся: прочитано {totalRead} из {count} байт.");
            }

            totalRead += bytesRead;
        }

        return buffer;
    }
}