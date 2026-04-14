using System;
using System.Collections.Generic;
using System.Threading;

public static class TransactionProcessor
{
    public static decimal ProcessTransactionsConcurrently(BankAccount account, List<decimal> transactions)
    {
        var threads = new List<Thread>();
        foreach (var t in transactions)
        {
            var amount = t;
            var th = new Thread(() =>
            {
                if (amount >= 0) account.Deposit(amount);
                else account.Withdraw(-amount);
            })
            { IsBackground = true };
            threads.Add(th);
            th.Start();
        }
        foreach (var th in threads) th.Join();
        return account.Balance;
    }

    public static decimal ProcessTransactionsWithLock(BankAccount account, List<decimal> transactions)
    {
        var threads = new List<Thread>();
        foreach (var t in transactions)
        {
            var amount = t;
            var th = new Thread(() =>
            {
                if (amount >= 0) account.DepositWithLock(amount);
                else account.WithdrawWithLock(-amount);
            })
            { IsBackground = true };
            threads.Add(th);
            th.Start();
        }
        foreach (var th in threads) th.Join();
        return account.Balance;
    }

    public static decimal ProcessTransactionsWithMonitor(BankAccount account, List<decimal> transactions)
    {
        var threads = new List<Thread>();
        foreach (var t in transactions)
        {
            var amount = t;
            var th = new Thread(() =>
            {
                if (amount >= 0) account.DepositWithMonitor(amount);
                else account.WithdrawWithMonitor(-amount);
            })
            { IsBackground = true };
            threads.Add(th);
            th.Start();
        }
        foreach (var th in threads) th.Join();
        return account.Balance;
    }

    public static void ProcessConcurrentTransfers(List<BankAccount> accounts, int transferCount, int seed = 42)
    {
        var rand = new Random(seed);

        // 1) Демонстрация возможного deadlock: два потока, которые захватывают в разном порядке.
        // Создадим две пары для демонстрации.
        Console.WriteLine("=== Демонстрация deadlock (наивный порядок захвата) ===");
        var a = accounts[0];
        var b = accounts[1];

        Thread t1 = new Thread(() =>
        {
            // захватывает сначала a, затем b
            lock (a) // случай: наивный lock по объекту аккаунта (не по _lock) — для демонстрации
            {
                Thread.Sleep(50);
                lock (b)
                {
                    a.Withdraw(1);
                    b.Deposit(1);
                }
            }
        });

        Thread t2 = new Thread(() =>
        {
            // захватывает сначала b, затем a -> риск deadlock
            lock (b)
            {
                Thread.Sleep(50);
                lock (a)
                {
                    b.Withdraw(1);
                    a.Deposit(1);
                }
            }
        });

        t1.Start();
        t2.Start();

        // Ждём короткое время — если оба не завершились, возможен deadlock
        Thread.Sleep(500);
        if (t1.IsAlive || t2.IsAlive)
            Console.WriteLine("Deadlock вероятен: потоки не завершились (демонстрация).");
        else
            Console.WriteLine("Deadlock не произошёл в демонстрации.");

        // Пытаемся корректно завершить демонстрационные поток(ов) — пусть дочерние потоки сами завершатся со временем
        // (они могут остаться заблокированными; в тестовой среде это демонстрация)

        // 2) Решение: единообразный порядок захвата (по Id)
        Console.WriteLine("=== Решение: единообразный порядок захвата ===");
        var threads = new List<Thread>();
        for (int i = 0; i < transferCount; i++)
        {
            var from = accounts[rand.Next(accounts.Count)];
            var to = accounts[rand.Next(accounts.Count)];
            if (from == to) continue;
            decimal amount = 1m;

            var th = new Thread(() =>
            {
                // используем TransferWithLock — который захватывает по порядку Id
                from.TransferWithLock(to, amount);
            })
            { IsBackground = true };
            threads.Add(th);
            th.Start();
        }
        foreach (var th in threads) th.Join();
        Console.WriteLine("Корректные переводы выполнены с соблюдением порядка захвата.");
    }
}
