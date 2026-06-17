using System.Text;
using System.Text.Json;


public static class MessageTypeNames
{
    public const string Handshake = "HELLO";     // приветствие при подключении
    public const string Send = "SEND";      // обычная отправка точка-точка
    public const string Broadcast = "BROADCAST"; // рассылка
    public const string Gather = "GATHER";    // сбор данных
    public const string Scatter = "SCATTER";   // раздача данных
    public const string Reduce = "REDUCE";    // редукция
    public const string Barrier = "BARRIER";   // барьерная синхронизация
}

// Теги сообщений. Это метка, по которой получатель отличает сообщения разных операций друг от друга
public static class MessageTags
{
    public const int Default = 0;   // обычные пользовательские сообщения
    public const int Broadcast = 100;
    public const int Gather = 200;
    public const int Scatter = 300;
    public const int Reduce = 400;
    public const int BarrierArrive = 500; // "я дошёл до барьера"
    public const int BarrierRelease = 501; // "всем можно идти дальше"
}

// Класс для представления сообщения
public class MpiMessage
{
    // Уникальный идентификатор сообщения
    public Guid MessageId { get; set; } = Guid.NewGuid();
    // Тип сообщения ("SEND", "RECEIVE", "BROADCAST", "GATHER", и т.д.)
    public string MessageType { get; set; } = MessageTypeNames.Send;
    // Ранг отправителя
    public int SourceRank { get; set; }
    // Ранг получателя
    public int DestinationRank { get; set; }
    // Тег сообщения для фильтрации
    public int Tag { get; set; } = MessageTags.Default;
    // данные сообщения
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    // Имя .NET-типа исходного объекта (например, "System.Int32" или "System.Int32[]").
    // Нужно, чтобы при приеме мы знали, во что десериализовать Payload обратно.
    public string PayloadType { get; set; } = "null";

    /// <summary>Временная метка создания сообщения.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// Cообщение точка-точка (один узел -> другому узлу)
public class PointToPointMessage : MpiMessage
{
    public PointToPointMessage()
    {
        MessageType = MessageTypeNames.Send;
    }
}

// Коллективное сообщение (участвуют все узлы: Broadcast, Gather, ...)
public class CollectiveMessage : MpiMessage
{
    public CollectiveMessage(string collectiveType)
    {
        MessageType = collectiveType;
    }
}

// Сообщение для синхронизации барьером
public class BarrierMessage : MpiMessage
{
    public BarrierMessage()
    {
        MessageType = MessageTypeNames.Barrier;
    }
}

// PayloadCodec превращает произвольный объект (object) в пару (байты + имя типа)
public static class PayloadCodec
{
    // Общие настройки JSON
    private static readonly JsonSerializerOptions Options = new()
    {
        // Компактный JSON (меньше байт)
        WriteIndented = false
    };

    // Объект -> (байты JSON, имя типа)
    public static (byte[] Bytes, string TypeName) Encode(object value)
    {
        if (value == null)
            return (Array.Empty<byte>(), "null");

        Type type = value.GetType();
        // Сериализуем именно как конкретный тип иначе JSON потеряет информацию и не восстановится корректно
        string json = JsonSerializer.Serialize(value, type, Options);

        return (Encoding.UTF8.GetBytes(json), type.AssemblyQualifiedName);
    }

    // (байты JSON, имя типа) -> объект
    public static object Decode(byte[] bytes, string typeName)
    {
        if (typeName == "null" || bytes == null || bytes.Length == 0)
            return null;

        Type type = Type.GetType(typeName);

        if (type == null)
            throw new InvalidOperationException($"Не удалось найти .NET-тип: {typeName}");

        string json = Encoding.UTF8.GetString(bytes);

        return JsonSerializer.Deserialize(json, type, Options);
    }

    public static JsonSerializerOptions JsonOptions => Options;
}

// MessageCodec превращает целый MpiMessage в байты и обратно. что реально передается по TCP-соединению.
public static class MessageCodec
{
    public static byte[] Serialize(MpiMessage message)
    {
        // Сериализуем как базовый тип MpiMessage.
        string json = JsonSerializer.Serialize(message, typeof(MpiMessage), PayloadCodec.JsonOptions);

        return Encoding.UTF8.GetBytes(json);
    }

    public static MpiMessage Deserialize(byte[] bytes)
    {
        string json = Encoding.UTF8.GetString(bytes);

        return JsonSerializer.Deserialize<MpiMessage>(json, PayloadCodec.JsonOptions);
    }
}

// Накопитель для операции Gather, который "путешествует" по кольцу узлов.
// Каждый узел кладет сюда свой ранг и свое значение (в виде JSON-строки),
// а корневой узел в конце восстанавливает массив значений по порядку рангов.
public class GatherAccumulator
{
    // Имя типа значений (все узлы отправляют значения одного типа)
    public string ElementType { get; set; } = "null";

    // Ранги узлов, чьи данные уже собраны
    public List<int> Ranks { get; set; } = new List<int>();

    // Значения этих узлов, каждое — в виде JSON-строки
    public List<string> ValuesJson { get; set; } = new List<string>();

    // Добавить вклад одного узла
    public void Add(int rank, object value)
    {
        if (ElementType == "null" && value != null)
            ElementType = value.GetType().AssemblyQualifiedName;

        Ranks.Add(rank);
        Type t = value?.GetType() ?? typeof(object);
        ValuesJson.Add(JsonSerializer.Serialize(value, t, PayloadCodec.JsonOptions));
    }

    // Собрать итоговый массив object[], упорядоченный по рангам 0..count-1.
    // Вызывается на корневом узле в конце Gather.
    public object[] ToOrderedArray(int totalNodes)
    {
        var result = new object[totalNodes];
        Type t = ElementType == "null" ? typeof(object) : Type.GetType(ElementType);

        for (int i = 0; i < Ranks.Count; i++)
        {
            int rank = Ranks[i];
            object value = t == null
                ? null
                : JsonSerializer.Deserialize(ValuesJson[i], t, PayloadCodec.JsonOptions);
            result[rank] = value;
        }
        return result;
    }
}
