using System.Text;

const string ServerHost = "127.0.0.1";
const int ServerPort = 8888;

Console.OutputEncoding = Encoding.UTF8;

using var globalCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    globalCts.Cancel();
};

// Поднимаем сервер
await using var server = new TcpServer(ServerPort, maxConnections: 50);

// Подписываемся на событие "пришло сообщение". На DATA отвечаем ACK.
server.MessageReceived += async (_, e) =>
{
    if (e.Message.MessageType != "DATA")
        return;

    var ack = new NetworkMessage
    {
        MessageType = "ACK",
        // Кладем исходный MessageId в SenderId — это договоренность
        // нашего протокола (NetworkBenchmark.CompareLatencyAsync).
        SenderId = e.Message.MessageId.ToString(),
        Payload = Array.Empty<byte>(),
    };

    try
    {
        await server.SendMessageToClientAsync(e.ClientId, ack);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Сервер] Не удалось отправить ACK клиенту {e.ClientId}: {ex.Message}");
    }
};

server.Start();

// Небольшая пауза, чтобы listener гарантированно начал принимать подключения
await Task.Delay(200);

// Создаем несколько клиентов для подключения к серверу
Console.WriteLine();
Console.WriteLine("=== Подключение демонстрационных клиентов ===");

const int demoClientsCount = 5;
var demoClients = new List<TcpClientWrapper>(demoClientsCount);
var successfulConnections = 0;

for (var i = 0; i < demoClientsCount; i++)
{
    try
    {
        var client = new TcpClientWrapper(ServerHost, ServerPort);
        await client.ConnectAsync(TimeSpan.FromSeconds(5));
        demoClients.Add(client);
        successfulConnections++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Демо] Клиент #{i + 1} не смог подключиться: {ex.Message}");
    }
}

Console.WriteLine($"Создано клиентов: {demoClientsCount}, успешно подключено: {successfulConnections}");

// Отправим по одному тестовому сообщению от каждого клиента, чтобы
// убедиться, что и DATA доходит, и ACK возвращается
var totalAckReceived = 0;
foreach (var c in demoClients)
{
    c.MessageReceived += (_, e) =>
    {
        if (e.Message.MessageType == "ACK")
            Interlocked.Increment(ref totalAckReceived);
    };
}

foreach (var c in demoClients)
{
    await c.SendMessageAsync(new NetworkMessage
    {
        MessageType = "DATA",
        SenderId = c.Id,
        Payload = Encoding.UTF8.GetBytes("Привет, сервер!"),
    });
}

// Даем серверу мгновение на ответ
await Task.Delay(500);
Console.WriteLine($"ACK получено от сервера: {totalAckReceived} из {demoClients.Count}");

// Отключаем клиенты
foreach (var c in demoClients)
{
    try { await c.DisposeAsync(); } catch { }
}

// Прогоняем бенчмарки
var benchmark = new NetworkBenchmark(ServerHost, ServerPort);

Console.WriteLine();
Console.WriteLine("=== Тест 1: один клиент (1000 сообщений по 1 КБ) ===");
var singleResult = await benchmark.BenchmarkSingleClientAsync(messageCount: 1000, messageSize: 1024);
Console.WriteLine($"Время:               {singleResult.Duration.TotalMilliseconds:F1} мс");
Console.WriteLine($"Отправлено:          {singleResult.Sent}");
Console.WriteLine($"Получено ACK:        {singleResult.Received}");
Console.WriteLine($"Пропускная способн.: {singleResult.ThroughputMBps:F2} МБ/сек");

Console.WriteLine();
Console.WriteLine("=== Тест 2: 5 клиентов параллельно (по 200 сообщений по 1 КБ) ===");
var multiResult = await benchmark.BenchmarkMultipleClientsAsync(clientCount: 5, messageCount: 200, messageSize: 1024);
Console.WriteLine($"Время:               {multiResult.Duration.TotalMilliseconds:F1} мс");
Console.WriteLine($"Отправлено суммарно: {multiResult.Sent}");
Console.WriteLine($"Получено ACK:        {multiResult.Received}");

Console.WriteLine();
Console.WriteLine("=== Тест 3: пропускная способность (3 сек, сообщения по 4 КБ) ===");
var throughputResult = await benchmark.BenchmarkThroughputAsync(durationSeconds: 3, messageSize: 4096);
Console.WriteLine($"Отправлено байт:     {throughputResult.SentBytes:N0}");
Console.WriteLine($"Получено байт (ACK): {throughputResult.ReceivedBytes:N0}");
Console.WriteLine($"Пропускная способн.: {throughputResult.ThroughputMBps:F2} МБ/сек");

Console.WriteLine();
Console.WriteLine("=== Тест 4: задержка round-trip (100 итераций) ===");
var latencyResult = await benchmark.CompareLatencyAsync(iterations: 100);
Console.WriteLine($"Средняя задержка:    {latencyResult.AverageMs:F2} мс");
Console.WriteLine($"Минимальная:         {latencyResult.MinMs:F2} мс");
Console.WriteLine($"Максимальная:        {latencyResult.MaxMs:F2} мс");

// ---------------------------------------------------------------------
// 4) Сводная статистика по шаблону из задания.
// ---------------------------------------------------------------------
var reliability = singleResult.Sent > 0
    ? singleResult.Received * 100.0 / singleResult.Sent
    : 0.0;

Console.WriteLine();
Console.WriteLine("=== Результаты тестирования сетевого обмена ===");
Console.WriteLine("Сервер:");
Console.WriteLine($"  Адрес: {ServerHost}:{ServerPort}");
Console.WriteLine($"  Подключено клиентов: {server.ConnectedClientsCount}");
Console.WriteLine($"  Обработано сообщений: {server.MessagesProcessed}");
Console.WriteLine();
Console.WriteLine("Клиенты:");
Console.WriteLine($"  Создано клиентов: {demoClientsCount}");
Console.WriteLine($"  Успешных подключений: {successfulConnections}");
Console.WriteLine();
Console.WriteLine("Производительность:");
Console.WriteLine($"  Пропускная способность: {throughputResult.ThroughputMBps:F2} МБ/сек");
Console.WriteLine($"  Средняя задержка:       {latencyResult.AverageMs:F2} мс");
Console.WriteLine($"  Минимальная задержка:   {latencyResult.MinMs:F2} мс");
Console.WriteLine($"  Максимальная задержка:  {latencyResult.MaxMs:F2} мс");
Console.WriteLine($"  Надежность доставки:    {reliability:F2}%");
Console.WriteLine();
Console.WriteLine("Тесты:");
Console.WriteLine($"  Тест 1 (один клиент):         {singleResult.Sent} отправлено / {singleResult.Received} ACK за {singleResult.Duration.TotalMilliseconds:F0} мс");
Console.WriteLine($"  Тест 2 (множество клиентов):  {multiResult.Sent} отправлено / {multiResult.Received} ACK за {multiResult.Duration.TotalMilliseconds:F0} мс");
Console.WriteLine($"  Тест 3 (пропускная способность): {throughputResult.ThroughputMBps:F2} МБ/сек");

// ---------------------------------------------------------------------
// 5) Аккуратно останавливаем сервер.
// ---------------------------------------------------------------------
await server.StopAsync();
Console.WriteLine();
Console.WriteLine("Готово.");