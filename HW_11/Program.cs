using System.Diagnostics;
using System.Text;


// Обертка над кластером: создаeт N коммуникаторов, запускает их, соединяет в полную сетку
public sealed class Cluster : IDisposable
{
    public MpiCommunicator[] Comms { get; }
    public int Count
    {
        get { return Comms.Length; }
    }

    public Cluster(int nodeCount)
    {
        Comms = new MpiCommunicator[nodeCount];

        for (int i = 0; i < nodeCount; i++)
            Comms[i] = new MpiCommunicator(i, nodeCount);
    }

    // Запустить кластер. Подключить каждую ноду друг к другу.
    public async Task StartAll()
    {
        // Сначала все узлы начинают слушать свои порты
        foreach (var c in Comms)
        {
            c.Start();
        }
        // Потом соединяются друг с другом
        await Task.WhenAll(Comms.Select(c => c.ConnectToPeers()));
        // Небольшая пауза, чтобы читатели успели подняться
        await Task.Delay(300);
    }

    public void Dispose()
    {
        foreach (var c in Comms)
        {
            c.Stop();
        }
        // дать ОС освободить порты
        Thread.Sleep(500);
    }
}

public static class Program
{
    public static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        const int nodeCount = 4;

        Console.WriteLine($"Демонстрация корректности операций (кластер из {nodeCount} узлов)");
        Console.WriteLine("--------------------------------------------------------------\n");

        var cluster = new Cluster(nodeCount);
        await cluster.StartAll();

        await DemoPointToPoint(cluster);
        await DemoBroadcast(cluster);
        await DemoScatter(cluster);
        await DemoGather(cluster);
        await DemoReduce(cluster);
        await DemoAllReduce(cluster);
        await DemoAllGather(cluster);
        double barrierMs = await DemoBarrier(cluster);

        Console.WriteLine("Измерение производительности (бенчмарки)");
        Console.WriteLine("------------------------------------------------------------\n");

        Logger.Enabled = false;
        var bench = new MpiBenchmark(cluster.Comms);
        var iterations = 50;
        var messageSize = 1024;
        var dataSize = 1000;

        var p2p = await bench.BenchmarkPointToPoint(iterations, messageSize);
        var bc = await bench.BenchmarkBroadcast(iterations, messageSize);
        var ga = await bench.BenchmarkGather(iterations, dataSize);
        var rd = await bench.BenchmarkReduce(iterations);
        Logger.Enabled = true;

        Console.WriteLine($"Точка-точка ({messageSize / 1024} КБ, ping-pong): среднее {p2p.Avg:F3} мс (мин {p2p.Min:F3}, макс {p2p.Max:F3})");
        Console.WriteLine($"Broadcast ({messageSize / 1024} КБ): {bc.TimeMs:F3} мс, сообщений за операцию: {bc.Messages}");
        Console.WriteLine($"Gather ({dataSize} значений): {ga.TimeMs:F3} мс, объём данных: {ga.DataVolumeBytes / 1024} КБ");
        Console.WriteLine($"Reduce (сумма): {rd.TimeMs:F3} мс, результат: {rd.Result}");

        Console.WriteLine($"Масштабируемость (Broadcast {messageSize / 1024} КБ при росте числа узлов)");
        Console.WriteLine("-------------------------------------------------------------\n");
        var scalability = new Dictionary<int, double>();
        Logger.Enabled = false;
        cluster.Dispose();

        foreach (int n in new[] { 2, 4, 8, 16 })
        {
            using var smallCluster = new Cluster(n);
            await smallCluster.StartAll();
            var bench2 = new MpiBenchmark(smallCluster.Comms);
            var bc2 = await bench2.BenchmarkBroadcast(iterations, messageSize);
            scalability[n] = bc2.TimeMs;
        }
        Logger.Enabled = true;

        foreach (var kv in scalability)
            Console.WriteLine($"{kv.Key} узла/узлов: {kv.Value:F3} мс");

        PrintSummary(nodeCount, p2p, bc.TimeMs, ga.TimeMs, rd.TimeMs, barrierMs, scalability, messageSize, dataSize);
    }

    private static async Task DemoPointToPoint(Cluster cluster)
    {
        Console.WriteLine(">>> Точка-точка: узел 0 отправляет сообщение узлу 1");
        var t0 = cluster.Comms[0].Send(1, "Привет от узла 0!");
        var t1 = cluster.Comms[1].Receive(0);
        await Task.WhenAll(t0, t1);
        Console.WriteLine($"    Узел 1 получил: \"{t1.Result}\"\n");
    }

    private static async Task DemoBroadcast(Cluster cluster)
    {
        Console.WriteLine(">>> Broadcast: узел 0 рассылает данные всем (по дереву)");
        var tasks = new Task<object>[cluster.Count];
        for (int i = 0; i < cluster.Count; i++)
        {
            MpiCommunicator c = cluster.Comms[i];
            tasks[i] = c.Broadcast(c.Rank == 0 ? "данные для всех" : null, 0);
        }
        object[] results = await Task.WhenAll(tasks);
        for (int i = 0; i < results.Length; i++)
            Console.WriteLine($"    Узел {i} получил: \"{results[i]}\"");
        Console.WriteLine();
    }

    private static async Task DemoScatter(Cluster cluster)
    {
        Console.WriteLine(">>> Scatter: узел 0 раздаёт каждому свой кусок");
        var tasks = new Task<object>[cluster.Count];
        for (int i = 0; i < cluster.Count; i++)
        {
            MpiCommunicator c = cluster.Comms[i];
            // Корень готовит по одному куску на каждый узел
            object[] data = c.Rank == 0
                ? Enumerable.Range(0, cluster.Count)
                            .Select(j => (object)$"кусок-{j}")
                            .ToArray()
                : null;
            tasks[i] = c.Scatter(data, 0);
        }
        object[] results = await Task.WhenAll(tasks);
        for (int i = 0; i < results.Length; i++)
            Console.WriteLine($"    Узел {i} получил: \"{results[i]}\"");
        Console.WriteLine();
    }

    private static async Task DemoGather(Cluster cluster)
    {
        Console.WriteLine(">>> Gather: узел 0 собирает значения со всех (по кольцу)");
        var tasks = new Task<object[]>[cluster.Count];

        for (int i = 0; i < cluster.Count; i++)
        {
            tasks[i] = cluster.Comms[i].Gather(cluster.Comms[i].Rank * 10, 0);
        }

        object[][] results = await Task.WhenAll(tasks);
        var collected = results[0];
        Console.WriteLine($"    Узел 0 собрал: [{string.Join(", ", collected)}]\n");
    }

    private static async Task DemoReduce(Cluster cluster)
    {
        Console.WriteLine(">>> Reduce: сумма рангов на узле 0 (по дереву)");
        var tasks = new Task<object>[cluster.Count];
        for (int i = 0; i < cluster.Count; i++)
        {
            tasks[i] = cluster.Comms[i].Reduce(cluster.Comms[i].Rank, (a, b) => (int)a + (int)b, 0);
        }

        object[] results = await Task.WhenAll(tasks);
        Console.WriteLine($"    Узел 0 получил сумму: {results[0]} (ожидалось 6)\n");
    }

    private static async Task DemoAllReduce(Cluster cluster)
    {
        Console.WriteLine(">>> AllReduce: сумма рангов на ВСЕХ узлах");
        var tasks = new Task<object>[cluster.Count];
        for (int i = 0; i < cluster.Count; i++)
        {
            tasks[i] = cluster.Comms[i].AllReduce(cluster.Comms[i].Rank, (a, b) => (int)a + (int)b);
        }

        object[] results = await Task.WhenAll(tasks);
        Console.WriteLine($"    Значения на узлах: [{string.Join(", ", results)}] (везде 6)\n");
    }

    private static async Task DemoAllGather(Cluster cluster)
    {
        Console.WriteLine(">>> AllGather: каждый узел получает данные всех");
        var tasks = new Task<object[]>[cluster.Count];
        for (int i = 0; i < cluster.Count; i++)
        {
            tasks[i] = cluster.Comms[i].AllGather(cluster.Comms[i].Rank * 100);
        }

        object[][] results = await Task.WhenAll(tasks);

        for (int i = 0; i < results.Length; i++)
        {
            Console.WriteLine($"    Узел {i} видит: [{string.Join(", ", results[i])}]");
        }

        Console.WriteLine();
    }

    private static async Task<double> DemoBarrier(Cluster cluster)
    {
        Console.WriteLine(">>> Barrier: синхронизация всех узлов");
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(cluster.Comms.Select(c => c.Barrier()));
        sw.Stop();
        Console.WriteLine($"    Все узлы прошли барьер за {sw.Elapsed.TotalMilliseconds:F3} мс\n");
        return sw.Elapsed.TotalMilliseconds;
    }

    private static void PrintSummary(int nodeCount,
        (double Avg, double Min, double Max) p2p,
        double broadcastMs, double gatherMs, double reduceMs, double barrierMs,
        Dictionary<int, double> scalability, int messageSize, int dataSize)
    {
        var ports = new List<int>();
        for (int i = 0; i < nodeCount; i++) ports.Add(MpiCommunicator.BasePort + i);

        Console.WriteLine();
        Console.WriteLine("=== Результаты тестирования MPI-подобного обмена сообщениями ===");
        Console.WriteLine("Конфигурация кластера:");
        Console.WriteLine($"  Количество узлов: {nodeCount}");
        Console.WriteLine($"  Порты: {string.Join(", ", ports)}");
        Console.WriteLine();
        Console.WriteLine("Точка-точка обмен:");
        Console.WriteLine($"  Средняя задержка: {p2p.Avg:F3} мс");
        Console.WriteLine($"  Минимальная задержка: {p2p.Min:F3} мс");
        Console.WriteLine($"  Максимальная задержка: {p2p.Max:F3} мс");
        Console.WriteLine();
        Console.WriteLine("Коллективные операции:");
        Console.WriteLine($"  Broadcast ({messageSize / 1024} КБ): {broadcastMs:F3} мс");
        Console.WriteLine($"  Gather ({dataSize} значений): {gatherMs:F3} мс");
        Console.WriteLine($"  Reduce (сумма): {reduceMs:F3} мс");
        Console.WriteLine($"  Barrier: {barrierMs:F3} мс");
        Console.WriteLine();
        Console.WriteLine("Масштабируемость:");
        foreach (var kv in scalability)
            Console.WriteLine($"  {kv.Key} узла/узлов: {kv.Value:F3} мс");
    }
}