using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;


public class TPLBenchmark
{
    public double ProcessItem(int value)
    {
        return Math.Sqrt(value);
    }

    public (long ElapsedMilliseconds, long SuccessfulOps, bool Correctness) BenchmarkTaskRun(int operationCount, int threadCount)
    {
        int opsPerTask = operationCount / threadCount;
        int remaining = operationCount % threadCount;
        var tasks = new Task[threadCount];
        int successful = 0;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < threadCount; i++)
        {
            int currentOps = opsPerTask + (i < remaining ? 1 : 0);
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < currentOps; j++)
                {
                    ProcessItem(j);
                    Interlocked.Increment(ref successful);
                }
            });
        }

        Task.WaitAll(tasks);
        sw.Stop();

        bool correctness = successful == operationCount;
        return (sw.ElapsedMilliseconds, successful, correctness);
    }

    public (long ElapsedMilliseconds, long SuccessfulOps) BenchmarkTaskFactory(int operationCount, int threadCount)
    {
        int opsPerTask = operationCount / threadCount;
        int remaining = operationCount % threadCount;
        var tasks = new Task[threadCount];
        int successful = 0;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < threadCount; i++)
        {
            int currentOps = opsPerTask + (i < remaining ? 1 : 0);
            tasks[i] = Task.Factory.StartNew(() =>
            {
                for (int j = 0; j < currentOps; j++)
                {
                    ProcessItem(j);
                    Interlocked.Increment(ref successful);
                }
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        }

        Task.WaitAll(tasks);
        sw.Stop();
        return (sw.ElapsedMilliseconds, successful);
    }

    public (long ElapsedMilliseconds, long SuccessfulOps) BenchmarkThreadAPI(int operationCount, int threadCount)
    {
        int opsPerTask = operationCount / threadCount;
        int remaining = operationCount % threadCount;
        Thread[] threads = new Thread[threadCount];
        int successful = 0;
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < threadCount; i++)
        {
            int currentOps = opsPerTask + (i < remaining ? 1 : 0);
            threads[i] = new Thread(() =>
            {
                for (int j = 0; j < currentOps; j++)
                {
                    ProcessItem(j);
                    Interlocked.Increment(ref successful);
                }
            });
            threads[i].Start();
        }

        foreach (Thread t in threads)
        {
            t.Join();
        }
        sw.Stop();
        return (sw.ElapsedMilliseconds, successful);
    }

    public void CompareAllApproaches()
    {
        const int operationCount = 1_000_000;
        const int threadCount = 4;

        var (timeRun, successRun, _) = BenchmarkTaskRun(operationCount, threadCount);
        double opsPerSecRun = successRun / (timeRun / 1000.0);

        var (timeFactory, successFactory) = BenchmarkTaskFactory(operationCount, threadCount);
        double opsPerSecFactory = successFactory / (timeFactory / 1000.0);

        var (timeThread, successThread) = BenchmarkThreadAPI(operationCount, threadCount);
        double opsPerSecThread = successThread / (timeThread / 1000.0);

        // Вывод
        Console.WriteLine("Task.Run:");
        Console.WriteLine($"  Время выполнения: {timeRun} мс");
        Console.WriteLine($"  Успешные операции: {successRun}");
        Console.WriteLine($"  Производительность: {opsPerSecRun:F2} операций/сек");

        Console.WriteLine("\nTask.Factory.StartNew:");
        Console.WriteLine($"  Время выполнения: {timeFactory} мс");
        Console.WriteLine($"  Успешные операции: {successFactory}");
        Console.WriteLine($"  Производительность: {opsPerSecFactory:F2} операций/сек");

        Console.WriteLine("\nThread API (ручное управление):");
        Console.WriteLine($"  Время выполнения: {timeThread} мс");
        Console.WriteLine($"  Успешные операции: {successThread}");
        Console.WriteLine($"  Производительность: {opsPerSecThread:F2} операций/сек");

        double speedup = timeThread / (double)timeRun;
        Console.WriteLine($"\nУскорение TPL vs Thread API: {speedup:F2}x");
    }
}
