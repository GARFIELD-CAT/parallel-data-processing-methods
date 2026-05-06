using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

public static class SynchronizationBenchmark
{
    // ReaderWriterLockSlim benchmark: создаём readerCount читателей и writerCount писателей.
    public static (long readMs, long writeMs, long totalMs) BenchmarkReaderWriterLock(LibraryCatalog catalog, int readerCount, int writerCount)
    {
        var rand = new Random(42);
        var readers = new List<Thread>();
        var writers = new List<Thread>();
        var swTotal = Stopwatch.StartNew();

        long readTime = 0;
        long writeTime = 0;

        // Readers
        for (int i = 0; i < readerCount; i++)
        {
            var th = new Thread(() =>
            {
                var sw = Stopwatch.StartNew();
                var k = rand.Next(0, 1000).ToString();
                catalog.SearchBooks(k);
                sw.Stop();
                Interlocked.Add(ref readTime, sw.ElapsedMilliseconds);
            })
            { IsBackground = true };
            readers.Add(th);
        }

        // Writers
        for (int i = 0; i < writerCount; i++)
        {
            int writerId = i;
            var th = new Thread(() =>
            {
                var sw = Stopwatch.StartNew();
                string title = $"W{writerId}-{rand.Next(100000)}";
                catalog.AddBook(title, "Author");
                // иногда обновляем
                if (i % 5 == 0)
                    catalog.UpdateBook(title, title + "-u", "AuthorU");
                sw.Stop();
                Interlocked.Add(ref writeTime, sw.ElapsedMilliseconds);
            })
            { IsBackground = true };
            writers.Add(th);
        }

        // Start writers then readers to stress writer priority scenario
        foreach (var w in writers) w.Start();
        foreach (var r in readers) r.Start();
        foreach (var w in writers) w.Join();
        foreach (var r in readers) r.Join();

        swTotal.Stop();
        return (readTime, writeTime, swTotal.ElapsedMilliseconds);
    }

    // SemaphoreSlim benchmark: spawn requestCount threads requesting resources then releasing
    public static (long totalMs, int success, int fail) BenchmarkSemaphore(ResourcePool pool, int requestCount, int timeoutMs = 500)
    {
        int success = 0;
        int fail = 0;
        var threads = new List<Thread>();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < requestCount; i++)
        {
            var th = new Thread(() =>
            {
                if (pool.TryAcquireResource(timeoutMs))
                {
                    try
                    {
                        Interlocked.Increment(ref success);
                        // симуляция работы с ресурсом
                        Thread.Sleep(10);
                    }
                    finally
                    {
                        pool.ReleaseResource();
                    }
                }
                else
                {
                    Interlocked.Increment(ref fail);
                }
            })
            { IsBackground = true };
            threads.Add(th);
            th.Start();
        }

        foreach (var t in threads) t.Join();
        sw.Stop();
        return (sw.ElapsedMilliseconds, success, fail);
    }

    // Mutex benchmark: выполняем count операций ExecuteWithGlobalLock (локально, но через именованный мьютекс)
    public static (long totalMs, int success) BenchmarkMutex(string mutexName, int count, int timeoutMs = 1000)
    {
        int success = 0;
        var threads = new List<Thread>();
        var sw = Stopwatch.StartNew();
        var rand = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var th = new Thread(() =>
            {
                bool ok = CrossProcessSync.TryExecuteWithGlobalLock(mutexName, () =>
                {
                    // короткая критическая секция
                    Thread.Sleep(10);
                }, timeoutMs);

                if (ok)
                {
                    Interlocked.Increment(ref success);
                }

            })
            { IsBackground = true };
            threads.Add(th);
            th.Start();
        }

        foreach (var t in threads) t.Join();
        sw.Stop();
        return (sw.ElapsedMilliseconds, success);
    }

    public static void CompareAllPrimitives(LibraryCatalog catalog, ResourcePool pool, string mutexName)
    {
        Console.WriteLine("=== Сравнение примитивов синхронизации ===");

        var rw = BenchmarkReaderWriterLock(catalog, 100, 10);
        Console.WriteLine("ReaderWriterLockSlim:");
        Console.WriteLine($"  Чтение: {rw.readMs} мс");
        Console.WriteLine($"  Запись: {rw.writeMs} мс");
        Console.WriteLine($"  Общее время: {rw.totalMs} мс");

        var sem = BenchmarkSemaphore(pool, 100, 200);
        Console.WriteLine("\nSemaphoreSlim:");
        Console.WriteLine($"  Время выполнения: {sem.totalMs} мс");
        Console.WriteLine($"  Успешные запросы: {sem.success}");
        Console.WriteLine($"  Неудачные запросы: {sem.fail}");

        var mu = BenchmarkMutex(mutexName, 100, 500);
        Console.WriteLine("\nMutex:");
        Console.WriteLine($"  Время выполнения: {mu.totalMs} мс");
        Console.WriteLine($"  Успешные операции: {mu.success}");

        double overheadRw = rw.totalMs > 0 ? (rw.totalMs - rw.totalMs) * 100.0 / rw.totalMs : 0;
        double overheadSem = rw.totalMs > 0 ? (sem.totalMs - rw.totalMs) * 100.0 / rw.totalMs : 0;
        double overheadMutex = rw.totalMs > 0 ? (mu.totalMs - rw.totalMs) * 100.0 / rw.totalMs : 0;

        Console.WriteLine("\nСравнение производительности:");
        Console.WriteLine($"  Накладные расходы ReaderWriterLockSlim: {overheadRw:F1}%");
        Console.WriteLine($"  Накладные расходы SemaphoreSlim: {overheadSem:F1}%");
        Console.WriteLine($"  Накладные расходы Mutex: {overheadMutex:F1}%");
    }
}
