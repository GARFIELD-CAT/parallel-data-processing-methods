using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

public class CollectionBenchmark
{
    public (long ElapsedMs, int SuccessOps, int FailedOps) BenchmarkConcurrentDictionary(int booksCount, int operationCount, int threadCount)
    {
        var catalog = new ConcurrentLibraryCatalog();
        int success = 0;
        int failed = 0;

        Parallel.For(0, booksCount, i =>
            catalog.AddBook($"Title_{i}", $"Author_{i}")
        );

        var rnd = new Random(42);
        var sw = Stopwatch.StartNew();

        Parallel.ForEach(
            Enumerable.Range(0, operationCount),
            new ParallelOptions { MaxDegreeOfParallelism = threadCount }, i =>
        {
            int keyIdx = rnd.Next(operationCount);
            string title = $"Title_{keyIdx}";
            int op = rnd.Next(3);

            if (op == 0) // AddBook
            {
                if (catalog.AddBook($"New_Title_{keyIdx}", $"Author_{keyIdx}"))
                    Interlocked.Increment(ref success);
                else
                    Interlocked.Increment(ref failed);
            }
            else if (op == 1) // SearchBooks
            {
                List<Book> result = catalog.SearchBooks(title);

                if (result.Count > 0)
                    Interlocked.Increment(ref success);
                else
                    Interlocked.Increment(ref failed);
            }
            else // RemoveBook
            {
                if (catalog.RemoveBook(title))
                    Interlocked.Increment(ref success);
                else
                    Interlocked.Increment(ref failed);
            }
        });

        sw.Stop();
        return (sw.ElapsedMilliseconds, success, failed);
    }

    public (long ElapsedMs, int ProcessedTasks) BenchmarkBlockingCollection(int boundedCapacity, int taskCount, int workerCount)
    {
        var queueManager = new TaskQueueManager(boundedCapacity);
        int processed = 0;
        var sw = Stopwatch.StartNew();

        var processingTask = queueManager.ProcessTasks(workerCount);

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

        processingTask.Wait();

        sw.Stop();

        queueManager.Dispose();

        return (sw.ElapsedMilliseconds, processed);
    }

    public (long ElapsedMs, int SuccessOps, int FailedOps) BenchmarkSynchronizedDictionary(int booksCount, int operationCount, int threadCount)
    {
        var dict = new Dictionary<string, Book>();
        var lockObj = new object();
        int success = 0;
        int failed = 0;

        for (int i = 0; i < operationCount; i++)
            dict[$"Title_{i}"] = new Book($"Title_{i}", $"Author_{i}");

        var rnd = new Random(42);
        var sw = Stopwatch.StartNew();

        Parallel.ForEach(
            Enumerable.Range(0, operationCount),
            new ParallelOptions { MaxDegreeOfParallelism = threadCount }, i =>
        {
            int keyIdx = rnd.Next(operationCount);
            string title = $"Title_{keyIdx}";
            int op = rnd.Next(3);

            lock (lockObj)
            {
                if (op == 0) // Add
                {
                    string newKey = $"New_Title_{keyIdx}";
                    if (!dict.ContainsKey(newKey))
                    {
                        dict.Add(newKey, new Book(newKey, $"Author_{i}"));
                        Interlocked.Increment(ref success);
                    }
                    else
                        Interlocked.Increment(ref failed);
                }
                else if (op == 1) // Search
                {
                    var results = new List<Book>();

                    foreach (var book in dict.Values)
                    {
                        if (book.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(book);
                        }
                    }

                    if (results.Count() > 0)
                    {
                        Interlocked.Increment(ref success);
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                    }
                }
                else // Remove
                {
                    if (dict.Remove(title))
                        Interlocked.Increment(ref success);
                    else
                        Interlocked.Increment(ref failed);
                }
            }

        });

        sw.Stop();
        return (sw.ElapsedMilliseconds, success, failed);
    }

    public (long ElapsedMs, int SuccessOps, int FailedOps) BenchmarkConcurrentCache(int cacheSize, int operationCount, int threadCount)
    {
        int success = 0;
        int failed = 0;
        var rnd = new Random(42);

        var cache = new ConcurrentCache();
        for (int i = 0; i < cacheSize; i++)
        {
            cache.AddToCache($"key_{i}", $"value_{i}");
        }

        var sw = Stopwatch.StartNew();
        var cacheTasks = new Task[threadCount];

        for (int t = 0; t < threadCount; t++)
        {
            cacheTasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < operationCount / threadCount; i++)
                {
                    if (rnd.Next(10) < 7)
                    {
                        if (cache.TryGetFromCache($"key_{rnd.Next(cacheSize)}", out _))
                            Interlocked.Increment(ref success);
                        else
                            Interlocked.Increment(ref failed);
                    }
                    else
                    {
                        cache.AddToCache($"key_{rnd.Next(cacheSize)}", $"new_value_{i}");
                        Interlocked.Increment(ref success);
                    }
                }
            });
        }
        Task.WaitAll(cacheTasks);
        sw.Stop();

        return (sw.ElapsedMilliseconds, success, failed);
    }

    public void CompareAllCollections()
    {
        int ops = 10000;
        int bookCount = 1000;
        int threads = 50;

        int tasks = 1000;
        int boundedCapacity = 1000;
        int workers = 50;

        var cdResult = BenchmarkConcurrentDictionary(bookCount, ops, threads);
        var bcResult = BenchmarkBlockingCollection(boundedCapacity, tasks, workers);
        var syncResult = BenchmarkSynchronizedDictionary(bookCount, ops, threads);

        double cdPerSec = cdResult.ElapsedMs > 0 ? cdResult.SuccessOps + cdResult.FailedOps / (cdResult.ElapsedMs / 1000.0) : 0;
        double bcPerSec = bcResult.ElapsedMs > 0 ? bcResult.ProcessedTasks / (bcResult.ElapsedMs / 1000.0) : 0;
        double syncPerSec = syncResult.ElapsedMs > 0 ? syncResult.SuccessOps + syncResult.FailedOps / (syncResult.ElapsedMs / 1000.0) : 0;
        double speedup = syncPerSec > 0 ? cdPerSec / syncPerSec : 0;

        Console.WriteLine("\n=== Сравнение потокобезопасных коллекций ===");
        Console.WriteLine("ConcurrentDictionary:");
        Console.WriteLine($"  Время выполнения: {cdResult.ElapsedMs} мс");
        Console.WriteLine($"  Попытки операции: {cdResult.SuccessOps + cdResult.FailedOps}");
        Console.WriteLine($"  Успешные операции: {cdResult.SuccessOps}");
        Console.WriteLine($"  Производительность: {cdPerSec:F2} операций/сек");

        Console.WriteLine("\nBlockingCollection:");
        Console.WriteLine($"  Время выполнения: {bcResult.ElapsedMs} мс");
        Console.WriteLine($"  Обработанные задачи: {bcResult.ProcessedTasks}");
        Console.WriteLine($"  Производительность: {bcPerSec:F2} задач/сек");

        Console.WriteLine("\nSynchronized Dictionary (с lock):");
        Console.WriteLine($"  Время выполнения: {syncResult.ElapsedMs} мс");
        Console.WriteLine($"  Попытки операции: {cdResult.SuccessOps + cdResult.FailedOps}");
        Console.WriteLine($"  Успешные операции: {syncResult.SuccessOps}");
        Console.WriteLine($"  Производительность: {syncPerSec:F2} операций/сек");

        Console.WriteLine($"\nУскорение ConcurrentDictionary vs Synchronized: {speedup:F2}x");

        double overheadPercent = (double)(syncResult.ElapsedMs - cdResult.ElapsedMs) / syncResult.ElapsedMs * 100;
        Console.WriteLine($"Накладные расходы синхронизации: {overheadPercent:F1}%");
    }
}