using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static void Main()
    {
        const int booksCount = 1000;
        const int tasksCount = 1000;
        const int cacheCount = 500;
        const int randomSeed = 42;

        var rand = new Random(randomSeed);

        // Подготовка тестовых данных для каталога
        var catalog = new ConcurrentLibraryCatalog();

        for (int i = 0; i < booksCount; i++)
        {
            string title = $"Book-{i}";
            string author = $"Author-{rand.Next(1, 100)}";
            catalog.AddBook(title, author);
        }

        var benchmark = new CollectionBenchmark();

        // Тестирование ConcurrentDictionary
        var (timeConcurrent, succOps, failOps) = benchmark.BenchmarkConcurrentDictionary(booksCount, randomSeed);
        var (timeSync, succSync) = benchmark.BenchmarkSynchronizedDictionary(booksCount, randomSeed);

        double cdThroughput = succOps / (timeConcurrent / 1000.0);
        double sdThroughput = succSync / (timeSync / 1000.0);
        double speedup = timeConcurrent > 0 ? Math.Round((double)timeSync / timeConcurrent, 2) : double.NaN;
        double syncOverheadPct = timeSync > 0 ? Math.Round(((double)(timeSync - timeConcurrent) / timeSync) * 100.0, 2) : double.NaN;

        // Тестирование BlockingCollection
        int boundedCapacity = 200;
        int workerCount = 10;
        var queueManager = new TaskQueueManager(boundedCapacity);
        for (int i = 0; i < tasksCount; i++)
        {
            int local = i;
            // Имитация полезной нагрузки
            queueManager.AddTask($"Task #{i}", () => { Thread.SpinWait(5000 + rand.Next(0, 1000)); });
        }

        var (timeBlocking, processed) = benchmark.BenchmarkBlockingCollection(queueManager, workerCount, tasksCount);

        // Тестирование ConcurrentCache
        var cache = new ConcurrentCache();

        for (int i = 0; i < cacheCount; i++)
            cache.AddToCache($"key_{i % 50}", new { Index = i });

        var (timeCache, cacheSuccess) = benchmark.BenchmarkConcurrentCache(cacheCount, randomSeed);

        // Проверки целостности
        bool catalogIntegrity = catalog.GetBookCount() == booksCount;
        bool queueIntegrity = processed == tasksCount;
        bool cacheIntegrity = cache.GetCacheSize() >= 0;

        // Вывод результатов
        Console.WriteLine("=== Результаты тестирования потокобезопасных коллекций ===\n");

        Console.WriteLine("ConcurrentDictionary:");
        Console.WriteLine($"  Количество операций: {booksCount}");
        Console.WriteLine($"  Время выполнения: {timeConcurrent} мс");
        Console.WriteLine($"  Успешные операции: {succOps}");
        Console.WriteLine($"  Производительность: {Math.Round(cdThroughput, 2)} операций/сек");
        Console.WriteLine($"  Целостность данных: {(catalogIntegrity ? "Да" : "Нет")}\n");

        Console.WriteLine("BlockingCollection:");
        Console.WriteLine($"  Количество задач: {tasksCount}");
        Console.WriteLine($"  Количество обработчиков: 10");
        Console.WriteLine($"  Время выполнения: {timeBlocking} мс");
        Console.WriteLine($"  Обработанные задачи: {processed}");
        Console.WriteLine($"  Производительность: {Math.Round(processed / (timeBlocking / 1000.0), 2)} задач/сек\n");

        Console.WriteLine("ConcurrentCache:");
        Console.WriteLine($"  Количество операций: {cacheCount}");
        Console.WriteLine($"  Время выполнения: {timeCache} мс");
        Console.WriteLine($"  Успешные операции: {cacheSuccess}\n");

        Console.WriteLine("Сравнение производительности:");
        Console.WriteLine($"  ConcurrentDictionary vs Synchronized Dictionary: {speedup}x");
        Console.WriteLine($"  Накладные расходы синхронизации: {syncOverheadPct}%\n");

        // Дополнительное сравнение всех коллекций
        benchmark.CompareAllCollections(booksCount, tasksCount, cacheCount, randomSeed);
    }
}