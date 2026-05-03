using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;


public class SynchronizationBenchmark
{
    private BankAccount _account;
    private List<decimal> _transactions;

    public SynchronizationBenchmark(BankAccount account, List<decimal> transactions)
    {
        _account = account;
        _transactions = transactions;
    }

    public (long timeMs, decimal finalBalance, bool isCorrect) BenchmarkNoSync(
        BankAccount account, List<decimal> transactions)
    {
        decimal expected = account.Balance + transactions.Sum();

        Stopwatch sw = Stopwatch.StartNew();
        TransactionProcessor.ProcessTransactionsConcurrently(account, transactions);
        sw.Stop();

        return (sw.ElapsedMilliseconds, account.Balance, account.Balance == expected);
    }

    public (long timeMs, decimal finalBalance, bool isCorrect) BenchmarkWithLock(
        BankAccount account, List<decimal> transactions)
    {
        decimal expected = account.Balance + transactions.Sum();

        Stopwatch sw = Stopwatch.StartNew();
        TransactionProcessor.ProcessTransactionsWithLock(account, transactions);
        sw.Stop();

        return (sw.ElapsedMilliseconds, account.Balance, account.Balance == expected);
    }

    public (long timeMs, decimal finalBalance, bool isCorrect) BenchmarkWithMonitor(
        BankAccount account, List<decimal> transactions)
    {
        decimal expected = account.Balance + transactions.Sum();

        Stopwatch sw = Stopwatch.StartNew();
        TransactionProcessor.ProcessTransactionsWithMonitor(account, transactions);
        sw.Stop();

        return (sw.ElapsedMilliseconds, account.Balance, account.Balance == expected);
    }

    public void CompareAllApproaches()
    {
        Console.WriteLine("=== Сравнение подходов к синхронизации ===\n");

        // Бенчмарки
        var (timeNoSync, _, correctNoSync) = BenchmarkNoSync(_account, _transactions);
        var (timeLock, _, correctLock) = BenchmarkWithLock(_account, _transactions);
        var (timeMonitor, _, correctMonitor) = BenchmarkWithMonitor(_account, _transactions);

        // Вывод результатов
        Console.WriteLine($"Без синхронизации: {timeNoSync} мс, результат: {(correctNoSync ? "корректный" : "некорректный")}");
        Console.WriteLine($"С использованием lock: {timeLock} мс, результат: {(correctLock ? "корректный" : "некорректный")}");
        Console.WriteLine($"С использованием Monitor: {timeMonitor} мс, результат: {(correctMonitor ? "корректный" : "некорректный")}");
    }
}