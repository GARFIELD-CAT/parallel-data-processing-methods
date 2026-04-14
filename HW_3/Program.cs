using System;
using System.Collections.Generic;
using System.Diagnostics;

class Program
{
    static void Main()
    {
        const int randomSeed = 42;
        const int dataSize = 1000;

        // Генерация данных
        List<decimal> transactions = GenerateData(dataSize, randomSeed);
        var expectedSum = transactions.Sum();

        Console.WriteLine($"Ожидаемый итоговый баланс: {expectedSum:F2}");

        decimal expected = 0;
        foreach (var t in transactions) expected += t;

        Console.WriteLine("=== Результаты тестирования синхронизации ===");
        Console.WriteLine($"Количество транзакций: {dataSize}");

        // Без синхронизации
        var accNo = new BankAccount(0);
        var benchNo = SynchronizationBenchmark.BenchmarkNoSync(accNo, transactions, expected);
        Console.WriteLine("Без синхронизации:");
        Console.WriteLine($"  Время: {benchNo.ms} мс");
        Console.WriteLine($"  Итоговый баланс: {benchNo.finalBalance}");
        Console.WriteLine($"  Корректность: {(benchNo.correct ? "Да" : "Нет")}");
        Console.WriteLine($"  Гонки данных: {(benchNo.correct ? "Нет" : "Да")}");

        // С использованием lock
        var accLock = new BankAccount(0);
        var benchLock = SynchronizationBenchmark.BenchmarkWithLock(accLock, transactions, expected);
        Console.WriteLine("\nС использованием lock:");
        Console.WriteLine($"  Время: {benchLock.ms} мс");
        Console.WriteLine($"  Итоговый баланс: {benchLock.finalBalance}");
        Console.WriteLine($"  Корректность: {(benchLock.correct ? "Да" : "Нет")}");
        double overheadLock = (benchLock.ms - benchNo.ms) > 0 ? (benchLock.ms - benchNo.ms) / (double)Math.Max(1, benchNo.ms) * 100.0 : 0.0;
        Console.WriteLine($"  Накладные расходы: {overheadLock:F2}%");

        // С использованием Monitor
        var accMon = new BankAccount(0);
        var benchMon = SynchronizationBenchmark.BenchmarkWithMonitor(accMon, transactions, expected);
        Console.WriteLine("\nС использованием Monitor:");
        Console.WriteLine($"  Время: {benchMon.ms} мс");
        Console.WriteLine($"  Итоговый баланс: {benchMon.finalBalance}");
        Console.WriteLine($"  Корректность: {(benchMon.correct ? "Да" : "Нет")}");
        double overheadMon = (benchMon.ms - benchNo.ms) > 0 ? (benchMon.ms - benchNo.ms) / (double)Math.Max(1, benchNo.ms) * 100.0 : 0.0;
        Console.WriteLine($"  Накладные расходы: {overheadMon:F2}%");

        // Сравнение производительности
        double speedupLockVsMon = benchMon.ms > 0 ? benchLock.ms / (double)benchMon.ms : 0.0;
        Console.WriteLine("\nСравнение производительности:");
        Console.WriteLine($"  Ускорение lock vs Monitor: {speedupLockVsMon:F2}x");
        Console.WriteLine($"  Накладные расходы lock: {overheadLock:F2}%");
        Console.WriteLine($"  Накладные расходы Monitor: {overheadMon:F2}%");

        // Тест перевода и deadlock
        Console.WriteLine("\n=== Тест перевода/deadlock ===");
        var accounts = new List<BankAccount>();
        for (int i = 0; i < 10; i++) accounts.Add(new BankAccount(1000)); // стартовый баланс

        TransactionProcessor.ProcessConcurrentTransfers(accounts, 1000, 42);

        // Сводная статистика по балансам
        decimal total = 0;
        foreach (var acc in accounts) total += acc.Balance;

        Console.WriteLine("\nИтоговые балансы после переводов:");
        for (int i = 0; i < accounts.Count; i++)
        {
            Console.WriteLine($"  Account#{accounts[i].Id}: {accounts[i].Balance}");
        }
        Console.WriteLine($"Общая сумма: {total}");
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