using System;
using System.Collections.Generic;

class Program
{
    static void Main(string[] args)
    {
        // ------------------ Тестирование обработки транзакций ------------------
        const decimal initialBalance = 10000;
        int randomSeed = 42;
        const int transactionCount = 1000;
        Random rnd = new Random(42);

        // Генерация 1000 транзакций: 500 депозитов, 500 снятий, суммы от 10 до 1000
        List<decimal> transactions = GenerateData(transactionCount, randomSeed);
        decimal expectedBalance = initialBalance + transactions.Sum();
        Console.WriteLine($"Ожидаемый баланс после транзакций: {expectedBalance:F2}\n");

        var accountNoSync = new BankAccount(initialBalance);
        Console.WriteLine($"accountNoSync: {accountNoSync.Balance}");
        SynchronizationBenchmark benchmark = new SynchronizationBenchmark(accountNoSync, transactions);

        // Тест без синхронизации
        var (timeNoSync, balanceNoSync, correctNoSync) =
            benchmark.BenchmarkNoSync(accountNoSync, transactions);

        // Тест с lock
        var accountLock = new BankAccount(initialBalance);
        var (timeLock, balanceLock, correctLock) =
            benchmark.BenchmarkWithLock(accountLock, transactions);

        // Тест с Monitor
        var accountMonitor = new BankAccount(initialBalance);
        var (timeMonitor, balanceMonitor, correctMonitor) =
            benchmark.BenchmarkWithMonitor(accountMonitor, transactions);

        // Расчёт накладных расходов в процентах относительно времени без синхронизации
        double overheadLock = timeNoSync == 0 ? 0 : (double)(timeLock - timeNoSync) / timeNoSync * 100;
        double overheadMonitor = timeNoSync == 0 ? 0 : (double)(timeMonitor - timeNoSync) / timeNoSync * 100;

        // Вывод сводной статистики
        Console.WriteLine("=== Результаты тестирования синхронизации ===");
        Console.WriteLine($"Количество транзакций: {transactionCount}");

        Console.WriteLine("\nБез синхронизации:");
        Console.WriteLine($"  Время: {timeNoSync} мс");
        Console.WriteLine($"  Итоговый баланс: {balanceNoSync:F2}");
        Console.WriteLine($"  Корректность: {(correctNoSync ? "Да" : "Нет")}");
        Console.WriteLine($"  Гонки данных: {(correctNoSync ? "Нет" : "Да")}");

        Console.WriteLine("\nС использованием lock:");
        Console.WriteLine($"  Время: {timeLock} мс");
        Console.WriteLine($"  Итоговый баланс: {balanceLock:F2}");
        Console.WriteLine($"  Корректность: {(correctLock ? "Да" : "Нет")}");
        Console.WriteLine($"  Накладные расходы: {overheadLock:F1}%");

        Console.WriteLine("\nС использованием Monitor:");
        Console.WriteLine($"  Время: {timeMonitor} мс");
        Console.WriteLine($"  Итоговый баланс: {balanceMonitor:F2}");
        Console.WriteLine($"  Корректность: {(correctMonitor ? "Да" : "Нет")}");
        Console.WriteLine($"  Накладные расходы: {overheadMonitor:F1}%");

        // Сравнение производительности lock vs Monitor
        double speedup = timeMonitor == 0 ? 0 : (double)timeLock / timeMonitor;
        Console.WriteLine("\nСравнение производительности:");
        Console.WriteLine($"  Ускорение lock vs Monitor: {speedup:F2}x");
        Console.WriteLine($"  Накладные расходы lock: {overheadLock:F1}%");
        Console.WriteLine($"  Накладные расходы Monitor: {overheadMonitor:F1}%");

        // ------------------ Тестирование переводов и deadlock ------------------
        const int accountsCount = 10;
        List<BankAccount> transferAccounts = new List<BankAccount>();

        for (int i = 0; i < accountsCount; i++)
            transferAccounts.Add(new BankAccount(initialBalance));

        TransactionProcessor.ProcessConcurrentTransfers(transferAccounts, transactionCount);

        // ------------------ Вывод итогового сравнения (из метода CompareAllApproaches) ------------------
        Console.WriteLine("\n");
        benchmark.CompareAllApproaches();
    }

    static List<decimal> GenerateData(int dataSize, int randomSeed)
    {
        // Генерация транзакций
        var random = new Random(randomSeed);
        var transactions = new List<decimal>();
        var n = dataSize / 2;

        for (int i = 0; i < n; i++)
        {
            decimal deposit = (decimal)(random.NextDouble() * 990 + 10);
            transactions.Add(deposit);

            decimal withdraw = (decimal)(random.NextDouble() * 990 + 10);
            transactions.Add(-withdraw);
        }
        // Перемешиваем, чтобы депозиты и снятия шли в случайном порядке
        transactions = transactions.OrderBy(x => random.Next()).ToList();

        Console.WriteLine($"Сгенерировано транзакций: {transactions.Count}");

        return transactions;
    }
}