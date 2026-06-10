using System.Net.Sockets;

public sealed class NetworkMessageEventArgs : EventArgs
{
    public NetworkMessage Message { get; }
    public NetworkMessageEventArgs(NetworkMessage message)
    {
        Message = message;
    }
}

public sealed class TcpClientWrapper : IAsyncDisposable
{
    public string Id { get; }

    // Хост и порт сервера
    private readonly string? _serverHost;
    private readonly int _serverPort;

    // Системный TCP-клиент
    private System.Net.Sockets.TcpClient? _tcp;

    // Сетевой поток для чтения/записи
    private NetworkStream? _stream;

    // Только одна операция записи в сокет одновременно
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

    private CancellationTokenSource? _receiveCts;

    // Фоновая задача, которая читает сообщения из сокета
    private Task? _receiveTask;

    // Флаг "уже отключены", чтобы не дёргать события несколько раз.
    private int _disconnectedFlag;

    // Событие вызывается при подключении
    public event EventHandler? Connected;
    // Событие вызывается при отключении
    public event EventHandler? Disconnected;
    // Событие вызывается при получении сообщения от сервера
    public event EventHandler<NetworkMessageEventArgs>? MessageReceived;

    // КОНСТРУКТОР 1 — для клиентского сценария
    // Просто запоминает адрес сервера; реального подключения ещё нет
    public TcpClientWrapper(string serverHost, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverHost);

        _serverHost = serverHost;
        _serverPort = port;
        Id = "client-" + Guid.NewGuid().ToString("N")[..8];
    }

    // КОНСТРУКТОР 2 — для серверной стороны
    // Принимает уже подключённый TcpClient (его выдал TcpListener.AcceptTcpClient)
    public TcpClientWrapper(string id, System.Net.Sockets.TcpClient acceptedClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(acceptedClient);

        Id = id;
        _tcp = acceptedClient;
        _stream = acceptedClient.GetStream();

        // host/port здесь не используются
        _serverHost = null;
        _serverPort = 0;
    }

    // True, если сокет открыт и готов работать
    public bool IsConnected
    {
        get { return _tcp?.Connected ?? false; }
    }

    // Только для клиентского сценария
    // Открывает соединение с сервером и запускает фоновое чтение
    public async Task ConnectAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (_serverHost is null)
            throw new InvalidOperationException(
                "Этот объект создан в режиме \"серверная сторона\" — " +
                "соединение уже установлено, ConnectAsync вызывать не нужно.");

        if (_tcp != null && _tcp.Connected)
            throw new InvalidOperationException("Уже подключены.");

        _tcp = new System.Net.Sockets.TcpClient();

        // Объединяем cancellationToken пользователя с таймаутом.
        using var timeoutCts = new CancellationTokenSource();

        if (timeout != null)
            timeoutCts.CancelAfter(timeout.Value);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            await _tcp.ConnectAsync(_serverHost, _serverPort, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Не удалось подключиться к {_serverHost}:{_serverPort} за {timeout}.");
        }

        _stream = _tcp.GetStream();

        // Подписчики узнают, что соединение установлено
        Connected?.Invoke(this, EventArgs.Empty);

        // Запускаем фоновый цикл чтения входящих сообщений
        StartReceiving();
    }

    // StartReceiving — запускает фоновую задачу, которая в цикле читает сообщения из потока
    // Вызывается:
    //   1. автоматически в ConnectAsync (на клиенте)
    //   2. вручную сервером сразу после создания wrapper
    public void StartReceiving()
    {
        if (_stream is null)
            throw new InvalidOperationException("Поток не открыт.");

        if (_receiveTask is not null)
            return;

        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
    }

    // Фоновый цикл чтения сообщений
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await MessageProtocol.ReadMessageAsync(_stream!, cancellationToken);

                // Если null, то другая сторона закрыла соединение между сообщениями
                if (message is null)
                    break;

                // Если сообщение пришло, то оповещаем подписчиков
                MessageReceived?.Invoke(this, new NetworkMessageEventArgs(message));
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемое завершение после Cancel()
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Id}] Ошибка приёма: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Помечаем себя как отключённого ровно один раз
            if (Interlocked.Exchange(ref _disconnectedFlag, 1) == 0)
                Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    // Отправка сообщения серверу
    public async Task SendMessageAsync(
        NetworkMessage message,
        CancellationToken cancellationToken = default)
    {
        if (_stream is null)
            throw new InvalidOperationException("Соединение не открыто.");

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await MessageProtocol.WriteMessageAsync(_stream, message, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // Отключение от сервера
    public async Task DisconnectAsync()
    {
        // Сигналим фоновому циклу, что пора заканчивать
        try
        {
            _receiveCts?.Cancel();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Id}] Не удалось отправить сигнал остановки циклу приёма: {ex.GetType().Name}: {ex.Message}");
        }

        // Закрываем сетевой опток
        try
        {
            _stream?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Id}] Ошибка при закрытии сетевого потока: {ex.GetType().Name}: {ex.Message}");
        }

        // Закрываем сокет
        try
        {
            _tcp?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Id}] Ошибка при закрытии сокета: {ex.GetType().Name}: {ex.Message}");
        }

        // Ждём, пока фоновая задача завершится
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Ожидаемое завершение после Cancel()
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Id}] Фоновый цикл приёма завершился с ошибкой: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // Освобождаем ресурсы клиента
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();

        _receiveCts?.Dispose();
        _stream?.Dispose();
        _tcp?.Dispose();
        _writeLock.Dispose();
    }
}