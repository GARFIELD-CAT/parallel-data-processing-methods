using System.Collections.Concurrent;
using System.Diagnostics;

namespace LibrarySystem;


public class CollectionBenchmark
{

    public (long ElapsedMs, int SuccessOps, int FailedOps) BenchmarkConcurrentDictionary(int operationCount)
    {
        var dict = new ConcurrentDictionary<int, string>();
        int success = 0;
        int failed = 0;
        var rnd = new Random(42); // фиксированный seed для воспроизводимости внутри бенчмарка
        var sw = Stopwatch.StartNew();

        // Создаём 50 потоков (как в основном тесте)
        int threadCount = 50;
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < operationCount / threadCount; i++)
                {
                    int key = rnd.Next(operationCount);
                    // Случайная операция: добавление, получение, удаление
                    int op = rnd.Next(3);
                    if (op == 0)
                    {
                        if (dict.TryAdd(key, $"Value {key}"))
                            Interlocked.Increment(ref success);
                        else
                            Interlocked.Increment(ref failed);
                    }
                    else if (op == 1)
                    {
                        if (dict.TryRemove(key, out _))
                            Interlocked.Increment(ref success);
                        else
                            Interlocked.Increment(ref failed);
                    }
                    else
                    {
                        if (dict.TryGetValue(key, out _))
                            Interlocked.Increment(ref success);
                        else
                            Interlocked.Increment(ref failed);
                    }
                }
            });
        }
        Task.WaitAll(tasks);
        sw.Stop();
        return (sw.ElapsedMilliseconds, success, failed);
    }

    /// <summary>
    /// Тестирование BlockingCollection: добавляем задачи, обрабатываем воркерами.
    /// Возвращает время и количество обработанных задач.
    /// </summary>
    public (long ElapsedMs, int ProcessedTasks) BenchmarkBlockingCollection(int taskCount, int workerCount)
    {
        var bc = new BlockingCollection<Action>(taskCount);
        int processed = 0;
        var sw = Stopwatch.StartNew();

        // Добавляем задачи
        for (int i = 0; i < taskCount; i++)
        {
            int taskId = i;
            bc.Add(() => { Interlocked.Increment(ref processed); });
        }
        bc.CompleteAdding();

        // Запускаем воркеров
        var workers = new Task[workerCount];
        for (int w = 0; w < workerCount; w++)
        {
            workers[w] = Task.Run(() =>
            {
                foreach (var action in bc.GetConsumingEnumerable())
                {
                    action();
                }
            });
        }
        Task.WaitAll(workers);
        sw.Stop();
        return (sw.ElapsedMilliseconds, processed);
    }

    public (long ElapsedMs, int SuccessOps) BenchmarkSynchronizedDictionary(int operationCount)
    {
        var dict = new Dictionary<int, string>();
        int success = 0;
        var rnd = new Random(42);
        var lockObj = new object();
        var sw = Stopwatch.StartNew();

        int threadCount = 50;
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < operationCount / threadCount; i++)
                {
                    int key = rnd.Next(operationCount);
                    int op = rnd.Next(3);
                    lock (lockObj)
                    {
                        if (op == 0)
                        {
                            if (!dict.ContainsKey(key))
                            {
                                dict[key] = $"Value {key}";
                                success++;
                            }
                        }
                        else if (op == 1)
                        {
                            if (dict.Remove(key))
                                success++;
                        }
                        else
                        {
                            if (dict.TryGetValue(key, out _))
                                success++;
                        }
                    }
                }
            });
        }
        Task.WaitAll(tasks);
        sw.Stop();
        return (sw.ElapsedMilliseconds, success);
    }

    public void CompareAllCollections()
    {
        const int ops = 1000;   // небольшое количество, чтобы быстро отработало в учебном примере
        const int tasks = 1000;
        const int workers = 10;

        var cdResult = BenchmarkConcurrentDictionary(ops);
        var bcResult = BenchmarkBlockingCollection(tasks, workers);
        var syncResult = BenchmarkSynchronizedDictionary(ops);

        double cdPerSec = cdResult.SuccessOps / (cdResult.ElapsedMs / 1000.0);
        double bcPerSec = bcResult.ProcessedTasks / (bcResult.ElapsedMs / 1000.0);
        double syncPerSec = syncResult.SuccessOps / (syncResult.ElapsedMs / 1000.0);
        double speedup = cdPerSec / syncPerSec;

        Console.WriteLine("\n=== Сравнение потокобезопасных коллекций ===");
        Console.WriteLine("ConcurrentDictionary:");
        Console.WriteLine($"  Время выполнения: {cdResult.ElapsedMs} мс");
        Console.WriteLine($"  Успешные операции: {cdResult.SuccessOps}");
        Console.WriteLine($"  Производительность: {cdPerSec:F2} операций/сек");

        Console.WriteLine("\nBlockingCollection:");
        Console.WriteLine($"  Время выполнения: {bcResult.ElapsedMs} мс");
        Console.WriteLine($"  Обработанные задачи: {bcResult.ProcessedTasks}");
        Console.WriteLine($"  Производительность: {bcPerSec:F2} задач/сек");

        Console.WriteLine("\nSynchronized Dictionary (с lock):");
        Console.WriteLine($"  Время выполнения: {syncResult.ElapsedMs} мс");
        Console.WriteLine($"  Успешные операции: {syncResult.SuccessOps}");
        Console.WriteLine($"  Производительность: {syncPerSec:F2} операций/сек");

        Console.WriteLine($"\nУскорение ConcurrentDictionary vs Synchronized: {speedup:F2}x");
    }
}