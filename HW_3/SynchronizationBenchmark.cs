using System;
using System.Collections.Generic;
using System.Diagnostics;

public static class SynchronizationBenchmark
{
    public static (long ms, decimal finalBalance, bool correct) BenchmarkNoSync(BankAccount account, List<decimal> transactions, decimal expected)
    {
        var sw = Stopwatch.StartNew();
        var result = TransactionProcessor.ProcessTransactionsConcurrently(account, transactions);
        sw.Stop();
        return (sw.ElapsedMilliseconds, result, result == expected);
    }

    public static (long ms, decimal finalBalance, bool correct) BenchmarkWithLock(BankAccount account, List<decimal> transactions, decimal expected)
    {
        var sw = Stopwatch.StartNew();
        var result = TransactionProcessor.ProcessTransactionsWithLock(account, transactions);
        sw.Stop();
        return (sw.ElapsedMilliseconds, result, result == expected);
    }

    public static (long ms, decimal finalBalance, bool correct) BenchmarkWithMonitor(BankAccount account, List<decimal> transactions, decimal expected)
    {
        var sw = Stopwatch.StartNew();
        var result = TransactionProcessor.ProcessTransactionsWithMonitor(account, transactions);
        sw.Stop();
        return (sw.ElapsedMilliseconds, result, result == expected);
    }

    public static void CompareAllApproaches(List<decimal> transactions)
    {
        // Создаём новые аккаунты для каждого теста, чтобы не портить состояние
        decimal expected = 0m;
        foreach (var t in transactions) expected += t;

        var a1 = new BankAccount(0m);
        var a2 = new BankAccount(0m);
        var a3 = new BankAccount(0m);

        var rNo = BenchmarkNoSync(a1, transactions, expected);
        var rLock = BenchmarkWithLock(a2, transactions, expected);
        var rMon = BenchmarkWithMonitor(a3, transactions, expected);

        Console.WriteLine("=== Сравнение подходов к синхронизации ===");
        Console.WriteLine($"Без синхронизации: {rNo.ms} мс, результат: {(rNo.correct ? "корректный" : "некорректный")}");
        Console.WriteLine($"С использованием lock: {rLock.ms} мс, результат: {(rLock.correct ? "корректный" : "некорректный")}");
        Console.WriteLine($"С использованием Monitor: {rMon.ms} мс, результат: {(rMon.correct ? "корректный" : "некорректный")}");
    }
}
