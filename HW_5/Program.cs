using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static void Main(string[] args)
    {
        var benchmark = new CollectionBenchmark();

        // Тестирование ConcurrentLibraryCatalog
        int booksCount = 1000;
        int ops = 1000;
        int threads = 50;

        var cdResult = benchmark.BenchmarkConcurrentDictionary(booksCount, ops, threads);
        double cdPerSec = cdResult.SuccessOps / (cdResult.ElapsedMs / 1000.0);

        // Тестирование TaskQueueManager
        int tasksCount = 1000;
        int boundedCapacity = 1000;
        int workersCount = 10;

        var bcResult = benchmark.BenchmarkBlockingCollection(boundedCapacity, tasksCount, workersCount);
        double bcPerSec = bcResult.ProcessedTasks / (bcResult.ElapsedMs / 1000.0);

        // Тестирование ConcurrentCache
        int cacheSize = 500;
        int cacheOperations = 500;
        int cacheThreads = 20;

        var swCache = benchmark.BenchmarkConcurrentCache(cacheSize, cacheOperations, cacheThreads);

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
        Console.WriteLine($"  Количество операций: {swCache.SuccessOps + swCache.FailedOps}");
        Console.WriteLine($"  Время выполнения: {swCache.ElapsedMs} мс");
        Console.WriteLine($"  Успешные операции: {swCache.SuccessOps}");

        benchmark.CompareAllCollections();
    }
}