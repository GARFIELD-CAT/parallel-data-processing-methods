using System;
using System.Threading;
using System.Threading.Tasks;

namespace AtomicOperationsDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== Результаты тестирования атомарных операций ===");
            Console.WriteLine();

            // Фиксированный seed для воспроизводимости
            int seed = 42;
            Random rnd = new Random(seed);

            // Параметры тестов
            int counterOpCount = 1_000_000;   // всего операций счётчика (инкременты + декременты)
            int counterThreads = 100;        // потоков для счётчика
            int stackOpCount = 100_000;      // операций со стеком
            int stackThreads = 50;           // потоков для стека
            int trackerReqCount = 50_000;    // запросов для трекера
            int trackerThreads = 200;        // потоков для трекера

            // ====== 1. Тестирование AtomicCounter ======
            Console.WriteLine("--- AtomicCounter ---");
            var interlockedRes = AtomicBenchmark.BenchmarkInterlockedCounter(counterOpCount, counterThreads);
            Console.WriteLine($"  Количество операций: {counterOpCount}");
            Console.WriteLine($"  Количество потоков: {counterThreads}");
            Console.WriteLine($"  Время выполнения: {interlockedRes.elapsedMs} мс");
            Console.WriteLine($"  Итоговое значение: {interlockedRes.finalValue}");
            Console.WriteLine($"  Корректность: {(interlockedRes.isCorrect ? "Да" : "Нет")}");
            Console.WriteLine();

            // ====== 2. Тестирование Locked Counter ======
            Console.WriteLine("--- Locked Counter ---");
            var lockedRes = AtomicBenchmark.BenchmarkLockedCounter(counterOpCount, counterThreads);
            Console.WriteLine($"  Количество операций: {counterOpCount}");
            Console.WriteLine($"  Количество потоков: {counterThreads}");
            Console.WriteLine($"  Время выполнения: {lockedRes.elapsedMs} мс");
            Console.WriteLine($"  Итоговое значение: {lockedRes.finalValue}");
            Console.WriteLine($"  Корректность: {(lockedRes.isCorrect ? "Да" : "Нет")}");
            Console.WriteLine();

            // ====== 3. Тестирование LockFreeStack (с проверкой целостности) ======
            Console.WriteLine("--- Lock-Free Stack ---");
            var stackRes = AtomicBenchmark.BenchmarkLockFreeStackWithValidation(stackOpCount, stackThreads);
            int actualStackCount = stackRes.stack.UnsafeCount();
            int expectedStackCount = stackRes.totalPushes - stackRes.successfulPops;
            bool stackCorrect = actualStackCount == expectedStackCount;

            Console.WriteLine($"  Количество операций: {stackOpCount}");
            Console.WriteLine($"  Количество потоков: {stackThreads}");
            Console.WriteLine($"  Время выполнения: {stackRes.elapsedMs} мс");
            Console.WriteLine($"  Успешные операции (Pop): {stackRes.successfulPops}");
            Console.WriteLine($"  Всего Push: {stackRes.totalPushes}");
            Console.WriteLine($"  Ожидаемое количество элементов: {expectedStackCount}");
            Console.WriteLine($"  Фактическое количество элементов: {actualStackCount}");
            Console.WriteLine($"  Корректность: {(stackCorrect ? "Да" : "Нет")}");
            Console.WriteLine();

            // ====== 4. Тестирование StatisticsTracker ======
            Console.WriteLine("--- Statistics Tracker ---");
            var trackerRes = AtomicBenchmark.BenchmarkStatisticsTracker(trackerReqCount, trackerThreads);
            Console.WriteLine($"  Количество запросов: {trackerReqCount}");
            Console.WriteLine($"  Количество потоков: {trackerThreads}");
            Console.WriteLine($"  Время выполнения: {trackerRes.elapsedMs} мс");
            Console.WriteLine($"  Успешные запросы: {trackerRes.success}");
            Console.WriteLine($"  Неудачные запросы: {trackerRes.failed}");
            Console.WriteLine();

            // ====== 5. Сравнение производительности ======
            if (lockedRes.elapsedMs > 0)
            {
                double speedup = (double)lockedRes.elapsedMs / interlockedRes.elapsedMs;
                double overheadPercent = (double)(lockedRes.elapsedMs - interlockedRes.elapsedMs) / lockedRes.elapsedMs * 100;
                Console.WriteLine("Сравнение производительности:");
                Console.WriteLine($"  Interlocked vs Locked: {speedup:F2}x");
                Console.WriteLine($"  Накладные расходы блокировок: {overheadPercent:F1}%");
            }
        }
    }
}