using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TplAssignment
{
    /// <summary>
    /// Класс для сравнения производительности TPL (Task.Run, Task.Factory) и ручного создания потоков (Thread).
    /// </summary>
    public class TPLBenchmark
    {
        /// <summary>
        /// Бенчмарк с использованием Task.Run.
        /// </summary>
        /// <param name="operationCount">Общее количество выполняемых операций (инкрементов)</param>
        /// <param name="threadCount">Количество параллельных задач</param>
        /// <returns>Кортеж (время в мс, успешные операции, корректность)</returns>
        public (long ElapsedMilliseconds, long SuccessfulOps, bool Correctness) BenchmarkTaskRun(int operationCount, int threadCount)
        {
            // Распределяем операции между задачами
            int opsPerTask = operationCount / threadCount;
            int remaining = operationCount % threadCount;
            var tasks = new Task[threadCount];
            int successful = 0; // общий счётчик успешных операций (используем Interlocked)
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < threadCount; i++)
            {
                int currentOps = opsPerTask + (i < remaining ? 1 : 0); // распределение остатка
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < currentOps; j++)
                    {
                        // Простейшая операция: инкремент глобального счётчика
                        Interlocked.Increment(ref successful);
                    }
                });
            }

            Task.WaitAll(tasks);
            sw.Stop();

            // Корректность: проверяем, что все операции выполнены
            bool correctness = (successful == operationCount);
            return (sw.ElapsedMilliseconds, successful, correctness);
        }

        /// <summary>
        /// Бенчмарк с использованием Task.Factory.StartNew.
        /// </summary>
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
                        Interlocked.Increment(ref successful);
                    }
                }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            }

            Task.WaitAll(tasks);
            sw.Stop();
            return (sw.ElapsedMilliseconds, successful);
        }

        /// <summary>
        /// Бенчмарк с ручным созданием потоков (Thread).
        /// </summary>
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
                        Interlocked.Increment(ref successful);
                    }
                });
                threads[i].Start();
            }

            // Ожидаем завершения всех потоков
            foreach (Thread t in threads)
            {
                t.Join();
            }
            sw.Stop();
            return (sw.ElapsedMilliseconds, successful);
        }

        /// <summary>
        /// Сравнение всех подходов и вывод отформатированной таблицы.
        /// </summary>
        public void CompareAllApproaches()
        {
            // Параметры тестирования: можно менять для масштабирования
            const int operationCount = 100_000;
            const int threadCount = 4;

            Console.WriteLine($"Тестирование с {operationCount} операций, {threadCount} потоков/задач:");

            // Task.Run
            var (timeRun, successRun, correctRun) = BenchmarkTaskRun(operationCount, threadCount);
            double opsPerSecRun = operationCount / (timeRun / 1000.0);

            // Task.Factory
            var (timeFactory, successFactory) = BenchmarkTaskFactory(operationCount, threadCount);
            double opsPerSecFactory = operationCount / (timeFactory / 1000.0);

            // Thread API
            var (timeThread, successThread) = BenchmarkThreadAPI(operationCount, threadCount);
            double opsPerSecThread = operationCount / (timeThread / 1000.0);

            // Вывод
            Console.WriteLine("Task.Run:");
            Console.WriteLine($"  Время выполнения: {timeRun} мс");
            Console.WriteLine($"  Успешные операции: {successRun}");
            Console.WriteLine($"  Производительность: {opsPerSecRun:F2} оп/сек");
            Console.WriteLine($"  Корректность: {(correctRun ? "Да" : "Нет")}");

            Console.WriteLine("\nTask.Factory.StartNew:");
            Console.WriteLine($"  Время выполнения: {timeFactory} мс");
            Console.WriteLine($"  Успешные операции: {successFactory}");
            Console.WriteLine($"  Производительность: {opsPerSecFactory:F2} оп/сек");

            Console.WriteLine("\nThread API (ручное управление):");
            Console.WriteLine($"  Время выполнения: {timeThread} мс");
            Console.WriteLine($"  Успешные операции: {successThread}");
            Console.WriteLine($"  Производительность: {opsPerSecThread:F2} оп/сек");

            // Ускорение TPL (Task.Run) относительно Thread API
            double speedup = timeThread / (double)timeRun;
            Console.WriteLine($"\nУскорение TPL vs Thread API: {speedup:F2}x");
        }
    }
}