using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;


public sealed class ServerClientEventArgs : EventArgs
{
    public string ClientId { get; }
    public ServerClientEventArgs(string clientId)
    {
        ClientId = clientId;
    }
}

public sealed class ServerMessageEventArgs : EventArgs
{
    public string ClientId { get; }
    public NetworkMessage Message { get; }
    public ServerMessageEventArgs(string clientId, NetworkMessage message)
    {
        ClientId = clientId;
        Message = message;
    }
}

public sealed class TcpServer : IAsyncDisposable
{
    private readonly int _port;
    private readonly int _maxConnections;
    private readonly SemaphoreSlim _connectionLimiter;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    // Счётчик обработанных сообщений
    private long _messagesProcessed;

    // Cписок подключенных клиентов
    public ConcurrentDictionary<string, TcpClientWrapper> ConnectedClients { get; } = new ConcurrentDictionary<string, TcpClientWrapper>();

    // Событие вызывается при подключении клиента
    public event EventHandler<ServerClientEventArgs>? ClientConnected;
    // Событие вызывается при отключении клиента
    public event EventHandler<ServerClientEventArgs>? ClientDisconnected;
    // Событие вызывается при  получении сообщения
    public event EventHandler<ServerMessageEventArgs>? MessageReceived;

    // Свойства для отчётности
    public long MessagesProcessed
    {
        get { return Interlocked.Read(ref _messagesProcessed); }
    }
    public int ConnectedClientsCount
    {
        get { return ConnectedClients.Count; }
    }
    public int Port
    {
        get { return _port; }
    }

    // Cоздание сервера на указанном порту
    public TcpServer(int port, int maxConnections = 100)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Порт должен быть в диапазоне 1..65535.");
        if (maxConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConnections));

        _port = port;
        _maxConnections = maxConnections;

        // Ограничиваем одновременное число подключений
        _connectionLimiter = new SemaphoreSlim(maxConnections, maxConnections);
    }

    // Запуск сервера и прослушивание подключений
    public void Start()
    {
        if (_listener is not null)
            throw new InvalidOperationException("Сервер уже запущен.");

        _cts = new CancellationTokenSource();

        // Слушаем только локальный интерфейс
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        Console.WriteLine($"[Сервер] Запущен на 127.0.0.1:{_port}");

        // Цикл приёма новых подключений работает в фоновой задаче
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    // Остановка сервера
    public async Task StopAsync()
    {
        if (_cts is null) return;

        // Сигнал фоновому циклу
        try { _cts.Cancel(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[Сервер] Не удалось отправить сигнал отмены: {ex.GetType().Name}: {ex.Message}");
        }

        // Прекращаем слушать сокет
        try { _listener?.Stop(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[Сервер] Ошибка при остановке слушателя порта: {ex.GetType().Name}: {ex.Message}");
        }

        // Ждем окончания цикла приема.
        if (_acceptTask is not null)
        {
            try { await _acceptTask; }
            catch (OperationCanceledException)
            {
                // Ожидаемое завершение после Cancel()
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Сервер] Цикл приёма подключений завершился с ошибкой: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Аккуратно закрываем все активные соединения
        foreach (var clientId in ConnectedClients.Keys.ToArray())
        {
            if (ConnectedClients.TryRemove(clientId, out var wrapper))
            {
                try { await wrapper.DisposeAsync(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Сервер] Не удалось корректно закрыть соединение {clientId}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Console.WriteLine("[Сервер] Остановлен.");
    }

    // Фоновый цикл, который принимает новые подключения
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            System.Net.Sockets.TcpClient tcpClient;

            try
            {
                // Ждём входящего подключения
                tcpClient = await _listener!.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException) { break; } // Ожидаемое завершение после Cancel()
            catch (ObjectDisposedException) { break; } // Stop() закрыл listener
            catch (Exception ex)
            {
                Console.WriteLine($"[Сервер] Ошибка при приёме подключения: {ex.Message}");
                continue;
            }

            // Ограничиваем одновременное число клиентов
            if (!await _connectionLimiter.WaitAsync(0, cancellationToken))
            {
                Console.WriteLine("[Сервер] Достигнут лимит подключений, отклоняем клиента.");
                try { tcpClient.Close(); } catch { }
                continue;
            }

            // Генерируем Id для нового клиента
            var clientId = "srv-cli-" + Guid.NewGuid().ToString("N")[..8];

            // Оборачиваем системный TcpClient в наш wrapper
            var wrapper = new TcpClientWrapper(clientId, tcpClient);

            // Подписываемся на события wrapper, чтобы пробрасывать их наружу
            wrapper.MessageReceived += OnWrapperMessageReceived;
            wrapper.Disconnected += OnWrapperDisconnected;

            // Регистрируем в подключенных клиентах
            ConnectedClients[clientId] = wrapper;

            // Запускаем фоновый приём сообщений у wrapper
            wrapper.StartReceiving();

            Console.WriteLine($"[Сервер] Клиент {clientId} подключился. Всего: {ConnectedClients.Count}");
            ClientConnected?.Invoke(this, new ServerClientEventArgs(clientId));
        }
    }

    // Обработчик "wrapper получил сообение"
    private void OnWrapperMessageReceived(object? sender, NetworkMessageEventArgs e)
    {
        if (sender is not TcpClientWrapper wrapper) return;

        Interlocked.Increment(ref _messagesProcessed);
        MessageReceived?.Invoke(this, new ServerMessageEventArgs(wrapper.Id, e.Message));
    }

    // Обработчик "wrapper сообщил, что соединение разорвано"
    private void OnWrapperDisconnected(object? sender, EventArgs e)
    {
        if (sender is not TcpClientWrapper wrapper) return;

        if (ConnectedClients.TryRemove(wrapper.Id, out _))
        {
            // Освобождаем слот для нового клиента
            try { _connectionLimiter.Release(); } catch { }

            // Освобождаем ресурсы wrapper
            _ = Task.Run(async () =>
            {
                try { await wrapper.DisposeAsync(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Сервер] Не удалось корректно закрыть соединение {wrapper.Id}: {ex.GetType().Name}: {ex.Message}");
                }
            });

            Console.WriteLine($"[Сервер] Клиент {wrapper.Id} отключился. Всего: {ConnectedClients.Count}");
            ClientDisconnected?.Invoke(this, new ServerClientEventArgs(wrapper.Id));
        }
    }

    // Рассылка сообщения всем подключенным клиентам
    public async Task BroadcastMessageAsync(
        NetworkMessage message,
        // Если токен не передали, то отмены не будет
        CancellationToken cancellationToken = default)
    {
        // Делаем снимок словаря, чтобы не было гонок при изменении во время рассылки
        var snapshot = ConnectedClients.Values.ToArray();
        var tasks = new List<Task>(snapshot.Length);

        foreach (var client in snapshot)
            tasks.Add(SafeSendAsync(client, message, cancellationToken));

        await Task.WhenAll(tasks);
    }

    // Отправка сообщения конкретному клиенту
    public async Task SendMessageToClientAsync(
        string clientId,
        NetworkMessage message,
        // Если токен не передали, то отмены не будет
        CancellationToken cancellationToken = default)
    {
        if (!ConnectedClients.TryGetValue(clientId, out var wrapper))
            throw new InvalidOperationException($"Клиент {clientId} не подключён.");

        await wrapper.SendMessageAsync(message, cancellationToken);
    }

    //Если конкретному клиенту не удалось отправить сообщение, то логируем, а не валим всю рассылку
    private static async Task SafeSendAsync(
        TcpClientWrapper wrapper,
        NetworkMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await wrapper.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Сервер] Не удалось отправить клиенту {wrapper.Id}: {ex.Message}");
        }
    }

    // Освобождаем ресурсы сервера
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
        _connectionLimiter.Dispose();
    }
}