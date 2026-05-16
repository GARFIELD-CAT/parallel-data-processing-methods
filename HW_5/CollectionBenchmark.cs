using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class CollectionBenchmark
{
    public (long ElapsedMs, int SuccessOps, int FailedOps) BenchmarkConcurrentDictionary(int operationCount, int threadCount)
    {
        var catalog = new ConcurrentDictionary<string, int>();
        int success = 0;
        int failed = 0;

        Parallel.For(0, operationCount, i =>
            catalog.TryAdd($"Key_{i}", i));

        var sw = Stopwatch.StartNew();
        var tasks = new Task[threadCount];

        var options = new ParallelOptions { MaxDegreeOfParallelism = threadCount };
        int opsPerThread = operationCount / threadCount;

        Parallel.For(0, threadCount, options, t =>
        {
            var rnd = new Random(42 * t);

            for (int i = 0; i < opsPerThread; i++)
            {
                int keyIdx = rnd.Next(operationCount);
                string key = $"Key_{keyIdx}";
                int op = rnd.Next(3);

                if (op == 0) // Add
                {
                    if (catalog.TryAdd($"NewKey_{keyIdx}_{i}", keyIdx))
                        Interlocked.Increment(ref success);
                    else
                        Interlocked.Increment(ref failed);
                }
                else if (op == 1) // TryGet
                {
                    if (catalog.TryGetValue(key, out _))
                        Interlocked.Increment(ref success);
                    else
                        Interlocked.Increment(ref failed);
                }
                else // Remove
                {
                    if (catalog.TryRemove(key, out _))
                        Interlocked.Increment(ref success);
                    else
                        Interlocked.Increment(ref failed);
                }
            }
        });

        sw.Stop();
        return (sw.ElapsedMilliseconds, success, failed);
    }

    public (long ElapsedMs, int ProcessedTasks) BenchmarkBlockingCollection(int taskCount, int workerCount)
    {
        var queueManager = new TaskQueueManager(taskCount);
        int processed = 0;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < taskCount; i++)
        {
            int taskId = i;
            queueManager.AddTask($"Task_{taskId}", () =>
            {
                var result = Math.Sqrt(i) * Math.Log10(i + 1.0);
                Interlocked.Increment(ref processed);
            });
        }
        queueManager.CompleteAdding();
        queueManager.ProcessTasks(workerCount);

        sw.Stop();
        return (sw.ElapsedMilliseconds, processed);
    }

    public (long ElapsedMs, int SuccessOps) BenchmarkSynchronizedDictionary(int operationCount, int threadCount)
    {
        var dict = new Dictionary<string, int>();
        var lockObj = new object();
        int success = 0;
        int failed = 0;

        for (int i = 0; i < operationCount; i++)
            dict[$"Key_{i}"] = i;

        var sw = Stopwatch.StartNew();
        var options = new ParallelOptions { MaxDegreeOfParallelism = threadCount };
        int opsPerThread = operationCount / threadCount;

        Parallel.For(0, threadCount, options, t =>
        {
            var rnd = new Random(42 * t);

            for (int i = 0; i < opsPerThread; i++)
            {
                int keyIdx = rnd.Next(operationCount);
                string key = $"Key_{keyIdx}";
                int op = rnd.Next(3);

                lock (lockObj)
                {
                    if (op == 0) // Add
                    {
                        string newKey = $"NewKey_{keyIdx}_{i}";
                        if (!dict.ContainsKey(newKey))
                        {
                            dict[newKey] = keyIdx;
                            Interlocked.Increment(ref success);
                        }
                        else
                            Interlocked.Increment(ref failed);
                    }
                    else if (op == 1) // Search
                    {
                        if (dict.ContainsKey(key))
                            Interlocked.Increment(ref success);
                        else
                            Interlocked.Increment(ref failed);
                    }
                    else // Remove
                    {
                        if (dict.Remove(key))
                            Interlocked.Increment(ref success);
                        else
                            Interlocked.Increment(ref failed);
                    }
                }
            }
        });

        sw.Stop();
        return (sw.ElapsedMilliseconds, success);
    }

    public void CompareAllCollections()
    {
        int ops = 100000;
        int threads = 50;
        int tasks = 10000;
        int workers = 10;

        var cdResult = BenchmarkConcurrentDictionary(ops, threads);
        var bcResult = BenchmarkBlockingCollection(tasks, workers);
        var syncResult = BenchmarkSynchronizedDictionary(ops, threads);

        double cdPerSec = cdResult.ElapsedMs > 0 ? cdResult.SuccessOps / (cdResult.ElapsedMs / 1000.0) : 0;
        double bcPerSec = bcResult.ElapsedMs > 0 ? bcResult.ProcessedTasks / (bcResult.ElapsedMs / 1000.0) : 0;
        double syncPerSec = syncResult.ElapsedMs > 0 ? syncResult.SuccessOps / (syncResult.ElapsedMs / 1000.0) : 0;
        double speedup = syncPerSec > 0 ? cdPerSec / syncPerSec : 0;

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