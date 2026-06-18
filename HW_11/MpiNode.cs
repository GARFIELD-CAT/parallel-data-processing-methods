using System.Net;
using System.Net.Sockets;

public static class Logger
{
    private static readonly object ConsoleLock = new object();
    // во время бенчмарков выключаем, чтобы не мешало
    public static bool Enabled = true;

    public static void Log(int nodeId, string text)
    {
        if (!Enabled) return;
        lock (ConsoleLock)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Узел {nodeId}] {text}");
        }
    }
}

// Обертка над TcpClient
public class TcpClientWrapper : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

    // Ранг узла на другом конце соединения (для исходящих известен сразу)
    public int RemoteNodeId { get; set; }

    public TcpClientWrapper(int remoteNodeId, TcpClient client)
    {
        RemoteNodeId = remoteNodeId;
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
    }

    // Отправить готовые байты: [4 байта длина]+[данные]
    public async Task SendBytes(byte[] data)
    {
        await _writeLock.WaitAsync();

        try
        {
            byte[] lengthPrefix = BitConverter.GetBytes(data.Length);
            await _stream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // Прочитать одино сообщение. Вернет null, если соединение закрыто
    public async Task<byte[]> ReadFrame()
    {
        byte[] lengthBuf = new byte[4];
        await ReadExactly(lengthBuf);
        int length = BitConverter.ToInt32(lengthBuf, 0);
        if (length <= 0) return Array.Empty<byte>();

        byte[] body = new byte[length];
        await ReadExactly(body);
        return body;
    }

    // Прочитать определенное число байт
    private async Task ReadExactly(byte[] buffer)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await _stream.ReadAsync(buffer, offset, buffer.Length - offset);
            if (read == 0) throw new EndOfStreamException("Соединение закрыто до окончания чтения сообщения");
            offset += read;
        }
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }
    }
}

// Почтовый ящик. Все входящие сообщения складываются сюда. Метод Receive ждет
// сообщение, подходящее под фильтр (источник + тип + тег), либо бросает
// TimeoutException по истечении времени
public class Mailbox
{
    private readonly List<MpiMessage> _messages = new List<MpiMessage>();
    private readonly object _lock = new object();
    private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

    /// <summary>Положить письмо в ящик и разбудить всех ожидающих.</summary>
    public void Add(MpiMessage message)
    {
        lock (_lock)
        {
            _messages.Add(message);
        }
        _signal.Release();
    }

    // Дождаться сообщения по фильтру. По истечении timeout бросает TimeoutException
    public async Task<MpiMessage> Receive(Func<MpiMessage, bool> match, TimeSpan timeout, CancellationToken cancellationToken = default)
    {

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        while (true)
        {
            // Сначала проверяем, нет ли уже подходящего письма в ящике.
            lock (_lock)
            {
                for (int i = 0; i < _messages.Count; i++)
                {
                    if (match(_messages[i]))
                    {
                        MpiMessage found = _messages[i];
                        _messages.RemoveAt(i);
                        return found;
                    }
                }
            }

            // Ждем сигнала о новом письме
            try
            {
                await _signal.WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("Ожидание сообщения отменено вызывающей стороной.", cancellationToken);

                throw new TimeoutException("Таймаут ожидания сообщения в почтовом ящике.");
            }
        }
    }
}

// MpiNode — узел распределенной системы.
// Слушает свой порт, подключается к другим узлам, отправляет и принимает байты.
public class MpiNode
{
    public int NodeId { get; }
    public int Port { get; }
    // Cписок подключенных узлов. Ключ — ранг соседа.
    public Dictionary<int, TcpClientWrapper> ConnectedNodes { get; } = new Dictionary<int, TcpClientWrapper>();
    // Событие "пришло сообщение"
    public event Action<MpiMessage> MessageReceived;

    private TcpListener _listener;
    private CancellationTokenSource _cts;
    private readonly Mailbox _mailbox = new Mailbox();
    private readonly object _connectionsLock = new object();

    // Счетчик отправленных сообщений
    private long _sentCount;

    public MpiNode(int nodeId, int port)
    {
        NodeId = nodeId;
        Port = port;
    }

    // Запуск узла и прослушивание подключений
    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, Port);
        // Разрешаем повторно занять порт сразу после освобождения — нужно для
        // тестов масштабируемости, где мы пересоздаем кластеры на тех же портах
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();
        _cts = new CancellationTokenSource();

        // Поток приема входящих подключений работает в фоне
        _ = AcceptLoop();
        Logger.Log(NodeId, $"Запущен и слушает порт {Port}.");
    }

    // Остановка узла
    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }

        lock (_connectionsLock)
        {
            foreach (var wrapper in ConnectedNodes.Values)
                wrapper.Dispose();

            ConnectedNodes.Clear();
        }
        Logger.Log(NodeId, "Остановлен.");
    }

    // Подключение к удаленному узлу
    public async Task ConnectToNode(int remoteNodeId, string remoteAddress, int remotePort)
    {
        for (int attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                var client = new TcpClient();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                // Подключаемся с таймаутом, чтобы не зависнуть навсегда.
                await client.ConnectAsync(remoteAddress, remotePort, connectCts.Token);
                var wrapper = new TcpClientWrapper(remoteNodeId, client);

                var hello = new MpiMessage
                {
                    MessageType = MessageTypeNames.Handshake,
                    SourceRank = NodeId,
                    DestinationRank = remoteNodeId
                };
                // Сразу шлем приветствие, чтобы сосед знал, кто подключился
                await wrapper.SendBytes(MessageCodec.Serialize(hello));

                lock (_connectionsLock)
                {
                    ConnectedNodes[remoteNodeId] = wrapper;
                }

                // Logger.Log(NodeId, $"Подключился к узлу {remoteNodeId} ({remoteAddress}:{remotePort}).");

                return;
            }
            catch (Exception ex)
            {
                Logger.Log(NodeId, $"Попытка {attempt} подключиться к {remoteNodeId} не удалась: {ex.Message}");
                await Task.Delay(100);
            }
        }
        throw new InvalidOperationException($"Не удалось подключиться к узлу {remoteNodeId}");
    }

    // Цикл приема новых подключений. Под каждое поднимаем поток-читатель
    private async Task AcceptLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                // Logger.Log(NodeId, $"Входящее подключение от {client.Client.RemoteEndPoint}");
                // Ранг -1 - это заглушка. Еще не знаем кто к нам подключился.
                _ = ReaderLoop(new TcpClientWrapper(-1, client));
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Logger.Log(NodeId, $"Ошибка приема соединения: {ex.Message}");
            }
        }
    }

    // Цикл чтения сообщений из одного входящего соединения
    private async Task ReaderLoop(TcpClientWrapper wrapper)
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                byte[] frame = await wrapper.ReadFrame();
                if (frame == null || frame.Length == 0) continue;

                MpiMessage message = MessageCodec.Deserialize(frame);

                if (message.MessageType == MessageTypeNames.Handshake)
                {
                    wrapper.RemoteNodeId = message.SourceRank;
                    // Logger.Log(NodeId, $"Принял приветствие от узла {message.SourceRank}");
                }
                else
                {
                    await ProcessMessage(message);
                }
            }
        }
        catch (Exception ex)
        {
            if (!_cts.Token.IsCancellationRequested)
                Logger.Log(NodeId, $"Соединение с {wrapper.RemoteNodeId} прервано: {ex.Message}");
        }
        finally
        {
            lock (_connectionsLock)
                if (wrapper.RemoteNodeId >= 0 &&
                    ConnectedNodes.TryGetValue(wrapper.RemoteNodeId, out var w) &&
                    ReferenceEquals(w, wrapper))
                {
                    ConnectedNodes.Remove(wrapper.RemoteNodeId);
                }

            wrapper.Dispose();
        }
    }

    // Обработка входящего сообщения
    public async Task ProcessMessage(MpiMessage message)
    {
        if (message.MessageType == MessageTypeNames.Handshake)
        {
            // Logger.Log(NodeId, $"Принял приветствие от узла {message.SourceRank}.");
            return;
        }

        if (message.DestinationRank == NodeId)
        {
            _mailbox.Add(message);
            MessageReceived?.Invoke(message);
        }
        else
        {
            // В полной сетке такого не бывает (шлем напрямую)
            await RouteMessage(message);
        }
    }

    // Маршрутизация сообщения к целевому узлу
    public async Task RouteMessage(MpiMessage message)
    {
        TcpClientWrapper wrapper;
        lock (_connectionsLock)
        {
            if (!ConnectedNodes.TryGetValue(message.DestinationRank, out wrapper))
                throw new InvalidOperationException(
                    $"Нет соединения с узлом {message.DestinationRank}. Сообщение не доставлено.");
        }

        await wrapper.SendBytes(MessageCodec.Serialize(message));
        Interlocked.Increment(ref _sentCount);
    }

    // Высокоуровневая отправка: упаковать объект и отправить получателю
    public async Task SendObject(int destination, object payload, string messageType, int tag)
    {
        var (bytes, typeName) = PayloadCodec.Encode(payload);
        var message = new MpiMessage
        {
            MessageType = messageType,
            SourceRank = NodeId,
            DestinationRank = destination,
            Tag = tag,
            Payload = bytes,
            PayloadType = typeName
        };

        await RouteMessage(message);
    }

    // Высокоуровневый прием: дождаться сообщения от нужного источника с нужными типом и тегом, затем распаковать объект
    public async Task<object> ReceiveObject(int source, string messageType, int tag, TimeSpan timeout)
    {
        MpiMessage message = await _mailbox.Receive(
            m => m.SourceRank == source && m.MessageType == messageType && m.Tag == tag,
            timeout);

        return PayloadCodec.Decode(message.Payload, message.PayloadType);
    }

    // Счeтчики для бенчмарков
    public long SentCount
    {
        get { return Interlocked.Read(ref _sentCount); }
    }
    public void ResetSentCount()
    {
        Interlocked.Exchange(ref _sentCount, 0);
    }
}
