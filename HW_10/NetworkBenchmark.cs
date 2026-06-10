using System.Diagnostics;

public sealed class NetworkBenchmark
{
    private readonly string _serverHost;
    private readonly int _serverPort;

    public NetworkBenchmark(string serverHost, int serverPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverHost);
        _serverHost = serverHost;
        _serverPort = serverPort;
    }

    // Cоздает payload заданного размера, заполненный
    private static byte[] MakePayload(int sizeBytes)
    {
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        var payload = new byte[sizeBytes];

        for (var i = 0; i < sizeBytes; i++)
            payload[i] = (byte)(i % 256);

        return payload;
    }

    // Измеряет время отправки заданного количества сообщений одному клиенту
    public async Task<(TimeSpan Duration, int Sent, int Received, double ThroughputMBps)>
        BenchmarkSingleClientAsync(int messageCount, int messageSize)
    {
        if (messageCount <= 0) throw new ArgumentOutOfRangeException(nameof(messageCount));
        if (messageSize < 0) throw new ArgumentOutOfRangeException(nameof(messageSize));

        // Получаем тестовый payload один раз и переиспользуем.
        var payload = MakePayload(messageSize);

        // Счетчик полученных ACK
        var ackCount = 0;
        // Когда все ACK получены, заврешаем работу.
        var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var client = new TcpClientWrapper(_serverHost, _serverPort);

        client.MessageReceived += (_, e) =>
        {
            if (e.Message.MessageType == "ACK")
            {
                var current = Interlocked.Increment(ref ackCount);
                if (current >= messageCount)
                    allReceived.TrySetResult(true);
            }
        };

        await client.ConnectAsync(TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();

        // Шлем несколько сообщений
        for (var i = 0; i < messageCount; i++)
        {
            var message = new NetworkMessage
            {
                MessageType = "DATA",
                SenderId = client.Id,
                Payload = payload,
            };
            await client.SendMessageAsync(message);
        }

        // Ждем, пока придут все ACK
        var ackTimeout = Task.Delay(TimeSpan.FromSeconds(30));
        await Task.WhenAny(allReceived.Task, ackTimeout);

        stopwatch.Stop();

        var sent = messageCount;
        var received = Interlocked.CompareExchange(ref ackCount, 0, 0);

        // Считаем пропускную способность по успешно отправленным байтам.
        var totalBytes = (long)sent * messageSize;
        var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.0001);
        var throughputMBps = totalBytes / 1024.0 / 1024.0 / seconds;

        await client.DisconnectAsync();

        return (stopwatch.Elapsed, sent, received, throughputMBps);
    }

    // Измеряет время отправки сообщений нескольким клиентам одновременно
    public async Task<(TimeSpan Duration, int Sent, int Received)>
        BenchmarkMultipleClientsAsync(int clientCount, int messageCount, int messageSize)
    {
        if (clientCount <= 0) throw new ArgumentOutOfRangeException(nameof(clientCount));
        if (messageCount <= 0) throw new ArgumentOutOfRangeException(nameof(messageCount));
        if (messageSize < 0) throw new ArgumentOutOfRangeException(nameof(messageSize));

        var payload = MakePayload(messageSize);

        // Создаём всех клиентов и подключаем их параллельно
        var clients = new TcpClientWrapper[clientCount];
        var ackCounters = new int[clientCount];
        var tcsArray = new TaskCompletionSource<bool>[clientCount];

        try
        {
            for (var i = 0; i < clientCount; i++)
            {
                var index = i;
                tcsArray[index] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var client = new TcpClientWrapper(_serverHost, _serverPort);
                client.MessageReceived += (_, e) =>
                {
                    if (e.Message.MessageType == "ACK")
                    {
                        var current = Interlocked.Increment(ref ackCounters[index]);
                        if (current >= messageCount)
                            tcsArray[index].TrySetResult(true);
                    }
                };
                clients[index] = client;
            }

            // Подключаемся параллельно
            await Task.WhenAll(clients.Select(c =>
                c.ConnectAsync(TimeSpan.FromSeconds(10))));

            var stopwatch = Stopwatch.StartNew();

            // Все клиенты параллельно шлют сообщения
            var sendTasks = clients.Select(async client =>
            {
                for (var i = 0; i < messageCount; i++)
                {
                    await client.SendMessageAsync(new NetworkMessage
                    {
                        MessageType = "DATA",
                        SenderId = client.Id,
                        Payload = payload,
                    });
                }
            });

            await Task.WhenAll(sendTasks);

            // Ждем, пока каждый клиент получит все свои ACK (или таймаут)
            var ackWait = Task.WhenAll(tcsArray.Select(t => t.Task));
            var timeout = Task.Delay(TimeSpan.FromSeconds(60));
            await Task.WhenAny(ackWait, timeout);

            stopwatch.Stop();

            var totalSent = clientCount * messageCount;
            var totalReceived = ackCounters.Sum();

            return (stopwatch.Elapsed, totalSent, totalReceived);
        }
        finally
        {
            foreach (var c in clients)
            {
                if (c is not null)
                {
                    try { await c.DisposeAsync(); } catch { }
                }
            }
        }
    }

    // Измеряет пропускную способность сети за заданное время
    public async Task<(long SentBytes, long ReceivedBytes, double ThroughputMBps)>
        BenchmarkThroughputAsync(int durationSeconds, int messageSize)
    {
        if (durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (messageSize < 0) throw new ArgumentOutOfRangeException(nameof(messageSize));

        var payload = MakePayload(messageSize);

        long sentBytes = 0;
        long receivedBytes = 0;

        await using var client = new TcpClientWrapper(_serverHost, _serverPort);

        client.MessageReceived += (_, e) =>
        {
            if (e.Message.MessageType == "ACK")
            {
                // Длина payload в ACK сервер не возвращает, поэтому считаем
                //  столько байт, сколько отправили в исходном DATA.
                Interlocked.Add(ref receivedBytes, messageSize);
            }
        };

        await client.ConnectAsync(TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!cts.IsCancellationRequested)
            {
                await client.SendMessageAsync(new NetworkMessage
                {
                    MessageType = "DATA",
                    SenderId = client.Id,
                    Payload = payload,
                }, cts.Token);

                Interlocked.Add(ref sentBytes, messageSize);
            }
        }
        catch (OperationCanceledException)
        {
            // Время вышло. Это ожидаемо
        }

        // Даем небольшое окно, чтобы оставшиеся ACK успели вернуться
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        stopwatch.Stop();

        await client.DisconnectAsync();

        var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.0001);
        var throughputMBps = sentBytes / 1024.0 / 1024.0 / seconds;

        return (sentBytes, Interlocked.Read(ref receivedBytes), throughputMBps);
    }

    // Измеряет задержку (latency) отправки и получения сообщения
    public async Task<(double AverageMs, double MinMs, double MaxMs)>
        CompareLatencyAsync(int iterations)
    {
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));

        // Для каждой итерации заводим TCS, на который сработает ACK.
        var pendingAcks = new System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskCompletionSource<bool>>();

        await using var client = new TcpClientWrapper(_serverHost, _serverPort);

        client.MessageReceived += (_, e) =>
        {
            if (e.Message.MessageType != "ACK") return;

            // ACK от сервера должен нести в Payload тот же MessageId, что и DATA.
            if (Guid.TryParse(e.Message.SenderId, out var requestId) &&
                pendingAcks.TryRemove(requestId, out var tcs))
            {
                tcs.TrySetResult(true);
            }
        };

        await client.ConnectAsync(TimeSpan.FromSeconds(5));

        var latencies = new double[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var requestId = Guid.NewGuid();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingAcks[requestId] = tcs;

            var sw = Stopwatch.StartNew();
            await client.SendMessageAsync(new NetworkMessage
            {
                MessageId = requestId,
                MessageType = "DATA",
                SenderId = client.Id,
                Payload = new byte[] { 1 }, // маленький payload, так как мерим именно latency
            });

            // Ждём ACK или таймаут (5 секунд на одну итерацию)
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            sw.Stop();

            if (done == tcs.Task)
                latencies[i] = sw.Elapsed.TotalMilliseconds;
            else
                latencies[i] = double.NaN; // потеря/таймаут
        }

        await client.DisconnectAsync();

        // Считаем метрики только по успешным итерациям.
        var successful = latencies.Where(x => !double.IsNaN(x)).ToArray();
        if (successful.Length == 0)
            return (double.NaN, double.NaN, double.NaN);

        return (successful.Average(), successful.Min(), successful.Max());
    }
}