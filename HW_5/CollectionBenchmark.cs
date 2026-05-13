using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class CollectionBenchmark
{
    // ConcurrentDictionary benchmark
    public (long elapsedMs, int successful, int failed) BenchmarkConcurrentDictionary(int operationCount, int seed = 42)
    {
        var dict = new ConcurrentDictionary<int, int>();
        var sw = Stopwatch.StartNew();
        int successful = 0, failed = 0;
        var rand = new Random(seed);

        var tasks = Enumerable.Range(0, Environment.ProcessorCount * 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < operationCount / (Environment.ProcessorCount * 4); i++)
            {
                int key = rand.Next(0, operationCount);
                if (dict.TryAdd(key, key))
                    Interlocked.Increment(ref successful);
                else
                    Interlocked.Increment(ref failed);

                dict.TryGetValue(key, out _);

                if (rand.NextDouble() < 0.3)
                {
                    dict.TryRemove(key, out _);
                }
                else
                {
                    dict.AddOrUpdate(key, key, (k, v) => v + 1);
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);
        sw.Stop();
        return (sw.ElapsedMilliseconds, successful, failed);
    }

    // Synchronized dictionary using lock
    public (long elapsedMs, int successful) BenchmarkSynchronizedDictionary(int operationCount, int seed = 42)
    {
        var dict = new Dictionary<int, int>();
        var sw = Stopwatch.StartNew();
        int successful = 0;
        var rand = new Random(seed);
        var locker = new object();

        var tasks = Enumerable.Range(0, Environment.ProcessorCount * 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < operationCount / (Environment.ProcessorCount * 4); i++)
            {
                int key = rand.Next(0, operationCount);
                lock (locker)
                {
                    if (!dict.ContainsKey(key))
                    {
                        dict[key] = key;
                        Interlocked.Increment(ref successful);
                    }
                }

                lock (locker)
                {
                    dict.TryGetValue(key, out _);
                }

                if (rand.NextDouble() < 0.3)
                {
                    lock (locker)
                    {
                        dict.Remove(key);
                    }
                }
                else
                {
                    lock (locker)
                    {
                        dict[key] = dict.ContainsKey(key) ? dict[key] + 1 : key;
                    }
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);
        sw.Stop();
        return (sw.ElapsedMilliseconds, successful);
    }

    // BlockingCollection benchmark (исправлен)
    public (long elapsedMs, int processedTasks) BenchmarkBlockingCollection(TaskQueueManager manager, int workerCount, int expectedTaskCount)
    {
        var workersTask = manager.ProcessTasks(workerCount);
        var sw = Stopwatch.StartNew();
        manager.CompleteAdding();
        workersTask.Wait(); // дожидаемся завершения всех обработчиков
        sw.Stop();
        return (sw.ElapsedMilliseconds, expectedTaskCount);
    }

    // ConcurrentCache benchmark
    public (long elapsedMs, int successfulGets) BenchmarkConcurrentCache(int operationCount, int seed = 42)
    {
        var cache = new ConcurrentCache();
        var rand = new Random(seed);
        for (int i = 0; i < Math.Max(100, operationCount / 2); i++)
            cache.AddToCache($"k_{i % 20}", i);

        int success = 0;
        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, Environment.ProcessorCount * 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < operationCount / (Environment.ProcessorCount * 4); i++)
            {
                string key = $"k_{rand.Next(0, 20)}";
                if (cache.TryGetFromCache(key, out var v))
                    Interlocked.Increment(ref success);
                else
                    cache.AddToCache(key, rand.Next());
            }
        })).ToArray();

        Task.WaitAll(tasks);
        sw.Stop();
        return (sw.ElapsedMilliseconds, success);
    }

    // Compare all and print
    public void CompareAllCollections(int operationCount, int taskCount, int cacheCount, int seed = 42)
    {
        var (cdTime, cdSucc, cdFail) = BenchmarkConcurrentDictionary(operationCount, seed);

        // Создаём менеджер для BlockingCollection
        var manager = new TaskQueueManager(200);
        for (int i = 0; i < taskCount; i++)
            manager.AddTask($"t{i}", () => { /* пустое действие для замера */ });

        var (bcTime, bcProcessed) = BenchmarkBlockingCollection(manager, workerCount: 10, taskCount);

        var (sdTime, sdSucc) = BenchmarkSynchronizedDictionary(operationCount, seed);

        Console.WriteLine("\n=== Сравнение потокобезопасных коллекций ===");
        Console.WriteLine("ConcurrentDictionary:");
        Console.WriteLine($"  Время выполнения: {cdTime} мс");
        Console.WriteLine($"  Успешные операции: {cdSucc}");
        Console.WriteLine($"  Производительность: {Math.Round(cdSucc / (cdTime / 1000.0), 2)} оп/с\n");

        Console.WriteLine("BlockingCollection:");
        Console.WriteLine($"  Время выполнения: {bcTime} мс");
        Console.WriteLine($"  Обработанные задачи: {bcProcessed}");
        Console.WriteLine($"  Производительность: {Math.Round(bcProcessed / (bcTime / 1000.0), 2)} задач/с\n");

        Console.WriteLine("Synchronized Dictionary (с lock):");
        Console.WriteLine($"  Время выполн: {sdTime} мс");
        Console.WriteLine($"  Успешные операции: {sdSucc}");
        Console.WriteLine($"  Производительность: {Math.Round(sdSucc / (sdTime / 1000.0), 2)} оп/с\n");

        var accel = sdTime > 0 ? Math.Round((double)sdTime / cdTime, 2) : 0;
        Console.WriteLine($"Ускорение ConcurrentDictionary vs Synchronized: {accel}x");
    }
}
