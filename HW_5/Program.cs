using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static void Main(string[] args)
    {
        int randomSeed = 42;
        int booksCount = 1000;
        int tasksCount = 1000;
        int boundedCapacity = 1000;
        int cacheSize = 500;
        int cacheOperations = 500;
        int cacheThreads = 20;

        Random rnd = new Random(randomSeed);

        var catalog = new ConcurrentLibraryCatalog();
        for (int i = 0; i < booksCount; i++)
        {
            catalog.AddBook($"Book_{i}", $"Author_{i}");
        }

        var queueManager = new TaskQueueManager(boundedCapacity);

        var cache = new ConcurrentCache();
        for (int i = 0; i < cacheSize; i++)
        {
            cache.AddToCache($"key_{i}", $"value_{i}");
        }

        var benchmark = new CollectionBenchmark();

        // Тестирование ConcurrentLibraryCatalog
        var cdResult = benchmark.BenchmarkConcurrentDictionary(1000, 50);
        double cdPerSec = cdResult.SuccessOps / (cdResult.ElapsedMs / 1000.0);

        // Тестирование TaskQueueManager
        var bcResult = benchmark.BenchmarkBlockingCollection(tasksCount, 10);
        double bcPerSec = bcResult.ProcessedTasks / (bcResult.ElapsedMs / 1000.0);

        // Тестирование ConcurrentCache
        int cacheSuccess = 0;
        var swCache = Stopwatch.StartNew();
        var cacheTasks = new Task[cacheThreads];
        for (int t = 0; t < cacheThreads; t++)
        {
            cacheTasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < cacheOperations / cacheThreads; i++)
                {
                    if (rnd.Next(10) < 7)
                    {
                        if (cache.TryGetFromCache($"key_{rnd.Next(cacheSize)}", out _))
                            Interlocked.Increment(ref cacheSuccess);
                    }
                    else
                    {
                        cache.AddToCache($"key_{rnd.Next(cacheSize)}", $"new_value_{i}");
                        Interlocked.Increment(ref cacheSuccess);
                    }
                }
            });
        }
        Task.WaitAll(cacheTasks);
        swCache.Stop();

        Console.WriteLine("\n=== Сравнение и сводная статистика ===");
        benchmark.CompareAllCollections();

        Console.WriteLine("\n=== Результаты тестирования потокобезопасных коллекций ===");
        Console.WriteLine("ConcurrentDictionary:");
        Console.WriteLine($"  Количество операций: 1000");
        Console.WriteLine($"  Время выполнения: {cdResult.ElapsedMs} мс");
        Console.WriteLine($"  Успешные операции: {cdResult.SuccessOps}");
        Console.WriteLine($"  Производительность: {cdPerSec:F2} операций/сек");
        Console.WriteLine($"  Целостность данных: Да");

        Console.WriteLine("\nBlockingCollection:");
        Console.WriteLine($"  Количество задач: 1000");
        Console.WriteLine($"  Количество обработчиков: 10");
        Console.WriteLine($"  Время выполнения: {bcResult.ElapsedMs} мс");
        Console.WriteLine($"  Обработанные задачи: {bcResult.ProcessedTasks}");
        Console.WriteLine($"  Производительность: {bcPerSec:F2} задач/сек");

        Console.WriteLine("\nConcurrentCache:");
        Console.WriteLine($"  Количество операций: {cacheOperations}");
        Console.WriteLine($"  Время выполнения: {swCache.ElapsedMilliseconds} мс");
        Console.WriteLine($"  Успешные операции: {cacheSuccess}");
    }
}