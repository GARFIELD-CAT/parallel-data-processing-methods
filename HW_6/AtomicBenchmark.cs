using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;


public static class AtomicBenchmark
{
    public static (long elapsedMs, long finalValue, bool isCorrect) BenchmarkInterlockedCounter(int operationCount, int threadCount)
    {
        var counter = new AtomicCounter();
        int increments = operationCount / 2;
        int decrements = operationCount / 2;

        int incrementsPerThread = increments / threadCount;
        int decrementsPerThread = decrements / threadCount;

        Stopwatch sw = Stopwatch.StartNew();

        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < incrementsPerThread; i++)
                counter.Increment();
            for (int i = 0; i < decrementsPerThread; i++)
                counter.Decrement();
        });

        sw.Stop();

        long finalValue = counter.Value;
        bool isCorrect = finalValue == 0;
        return (sw.ElapsedMilliseconds, finalValue, isCorrect);
    }

    public static (long elapsedMs, long finalValue, bool isCorrect) BenchmarkLockedCounter(int operationCount, int threadCount)
    {
        long counter = 0;
        object lockObj = new object();

        int increments = operationCount / 2;
        int decrements = operationCount / 2;

        int incrementsPerThread = increments / threadCount;
        int decrementsPerThread = decrements / threadCount;

        Stopwatch sw = Stopwatch.StartNew();

        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < incrementsPerThread; i++)
            {
                lock (lockObj) { counter++; }
            }
            for (int i = 0; i < decrementsPerThread; i++)
            {
                lock (lockObj) { counter--; }
            }
        });

        sw.Stop();

        long finalValue = counter;
        bool isCorrect = finalValue == 0;
        return (sw.ElapsedMilliseconds, finalValue, isCorrect);
    }

    public static (long elapsedMs, int successfulPops, int totalPushes, LockFreeStack<int> stack) BenchmarkLockFreeStack(int operationCount, int threadCount)
    {
        var stack = new LockFreeStack<int>();
        Random rnd = new Random(42);
        int operationsPerThread = operationCount / threadCount;

        int totalPushes = 0;
        int totalSuccessfulPops = 0;

        Stopwatch sw = Stopwatch.StartNew();

        Parallel.For(0, threadCount, _ =>
        {
            Random threadRnd = new Random(Guid.NewGuid().GetHashCode());
            for (int i = 0; i < operationsPerThread; i++)
            {
                if (threadRnd.Next(2) == 0)
                {
                    stack.Push(threadRnd.Next(operationCount));
                    Interlocked.Increment(ref totalPushes);
                }
                else
                {
                    if (stack.TryPop(out _))
                    {
                        Interlocked.Increment(ref totalSuccessfulPops);
                    }
                }
            }
        });

        sw.Stop();

        return (sw.ElapsedMilliseconds, totalSuccessfulPops, totalPushes, stack);
    }

    public static (long elapsedMs, long total, long success, long failed) BenchmarkStatisticsTracker(int requestCount, int threadCount)
    {
        var tracker = new StatisticsTracker();
        Random rnd = new Random(42);
        int requestsPerThread = requestCount / threadCount;

        Stopwatch sw = Stopwatch.StartNew();

        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < requestsPerThread; i++)
            {
                bool success = rnd.NextDouble() < 0.8; // 80% успеха
                long processingTime = rnd.Next(1, 101); // 1-100 мс
                tracker.RecordRequest(success, processingTime);
            }
        });

        sw.Stop();

        return (sw.ElapsedMilliseconds, tracker.TotalRequests, tracker.SuccessfulRequests, tracker.FailedRequests);
    }

    public static void CompareAllApproaches()
    {
        Console.WriteLine("=== Сравнение атомарных операций и блокировок ===");

        // Параметры тестов
        int counterOpCount = 1_000_000;
        int counterThreads = 100;
        int stackOpCount = 100_000;
        int stackThreads = 50;
        int trackerReqCount = 50_000;
        int trackerThreads = 200;

        var interlockedResult = BenchmarkInterlockedCounter(counterOpCount, counterThreads);
        Console.WriteLine("Interlocked Counter:");
        Console.WriteLine($"  Время выполнения: {interlockedResult.elapsedMs} мс");
        Console.WriteLine($"  Итоговое значение: {interlockedResult.finalValue}");
        Console.WriteLine($"  Корректность: {(interlockedResult.isCorrect ? "Да" : "Нет")}");
        Console.WriteLine();

        var lockedResult = BenchmarkLockedCounter(counterOpCount, counterThreads);
        Console.WriteLine("Locked Counter (с lock):");
        Console.WriteLine($"  Время выполнения: {lockedResult.elapsedMs} мс");
        Console.WriteLine($"  Итоговое значение: {lockedResult.finalValue}");
        Console.WriteLine($"  Корректность: {(lockedResult.isCorrect ? "Да" : "Нет")}");
        Console.WriteLine();

        var stackResult = BenchmarkLockFreeStack(stackOpCount, stackThreads);
        Console.WriteLine("Lock-Free Stack:");
        Console.WriteLine($"  Время выполнения: {stackResult.elapsedMs} мс");
        Console.WriteLine($"  Успешные операции (Pop): {stackResult.successfulPops}");
        Console.WriteLine($"  Всего Push: {stackResult.totalPushes}");
        Console.WriteLine();

        var trackerResult = BenchmarkStatisticsTracker(trackerReqCount, trackerThreads);
        Console.WriteLine("Statistics Tracker:");
        Console.WriteLine($"  Количество запросов: {trackerReqCount}");
        Console.WriteLine($"  Количество потоков: {trackerThreads}");
        Console.WriteLine($"  Время выполнения: {trackerResult.elapsedMs} мс");
        Console.WriteLine($"  Успешные запросы: {trackerResult.success}");
        Console.WriteLine($"  Неудачные запросы: {trackerResult.failed}");
        Console.WriteLine();

        // Сравнение производительности
        if (lockedResult.elapsedMs > 0)
        {
            double speedup = (double)lockedResult.elapsedMs / interlockedResult.elapsedMs;
            double overheadPercent = (double)(lockedResult.elapsedMs - interlockedResult.elapsedMs) / lockedResult.elapsedMs * 100;
            Console.WriteLine("Сравнение производительности:");
            Console.WriteLine($"  Interlocked vs Locked: {speedup:F2}x");
            Console.WriteLine($"  Накладные расходы блокировок: {overheadPercent:F1}%");
            Console.WriteLine();
        }
    }
}
