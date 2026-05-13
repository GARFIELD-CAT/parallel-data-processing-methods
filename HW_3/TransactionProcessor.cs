using System;
using System.Collections.Generic;
using System.Threading;


public static class TransactionProcessor
{
    // Обрабатывает список транзакций без синхронизации – каждый вызов в отдельном потоке.
    public static decimal ProcessTransactionsConcurrently(BankAccount account, List<decimal> transactions)
    {
        // Список для хранения ссылок на потоки, чтобы дождаться их завершения.
        List<Thread> threads = new List<Thread>();

        foreach (decimal amount in transactions)
        {
            Thread t = new Thread(() =>
            {
                // Для демонстрации гонки данных добавил.
                Thread.Sleep(1);

                if (amount >= 0)
                    account.Deposit(amount);
                else
                    account.Withdraw(-amount);
            });
            threads.Add(t);
        }

        foreach (Thread t in threads)
        {
            t.Start();
        }

        foreach (Thread t in threads)
        {
            t.Join();
        }

        return account.Balance;
    }

    // Обработка с использованием lock.
    public static decimal ProcessTransactionsWithLock(BankAccount account, List<decimal> transactions)
    {
        List<Thread> threads = new List<Thread>();

        foreach (decimal amount in transactions)
        {
            Thread t = new Thread(() =>
            {
                if (amount >= 0)
                    account.DepositWithLock(amount);
                else
                    account.WithdrawWithLock(-amount);
            });
            threads.Add(t);
        }

        foreach (Thread t in threads)
        {
            t.Start();
        }

        foreach (Thread t in threads)
        {
            t.Join();
        }

        return account.Balance;
    }

    // Обработка с использованием Monitor.
    public static decimal ProcessTransactionsWithMonitor(BankAccount account, List<decimal> transactions)
    {
        List<Thread> threads = new List<Thread>();

        foreach (decimal amount in transactions)
        {
            Thread t = new Thread(() =>
            {
                if (amount >= 0)
                    account.DepositWithMonitor(amount);
                else
                    account.WithdrawWithMonitor(-amount);
            });
            threads.Add(t);
        }

        foreach (Thread t in threads)
        {
            t.Start();
        }

        foreach (Thread t in threads)
        {
            t.Join();
        }

        return account.Balance;
    }

    // Демонстрирует проблему deadlock и правильное её решение при параллельных переводах между счетами.
    public static void ProcessConcurrentTransfers(List<BankAccount> accounts, int transferCount)
    {
        Console.WriteLine("\n=== Демонстрация deadlock и безопасного перевода ===");
        // Сохраним балансы счетов до всех переводов.
        decimal expectedTotal = 0;

        foreach (var acc in accounts)
            expectedTotal += acc.Balance;

        Random rnd = new Random(42);

        // Часть 1: НЕБЕЗОПАСНЫЕ переводы (могут привести к deadlock).
        BankAccount a = new BankAccount(accounts[0].Balance);
        BankAccount b = new BankAccount(accounts[1].Balance);

        Thread t1 = new Thread(() =>
        {
            a.TransferWithMonitor(b, 10);
        });
        Thread t2 = new Thread(() =>
        {
            b.TransferWithMonitor(a, 20); // обратное направление -> deadlock
        });

        Console.WriteLine("Запуск двух потоков с небезопасным порядком захвата блокировок...");
        t1.Start();
        t2.Start();

        // Ждём не более 2 секунд; если за это время потоки не завершились – deadlock.
        bool finished = t1.Join(2000) && t2.Join(2000);

        if (!finished)
        {
            Console.WriteLine("Обнаружен DEADLOCK! Потоки не завершились за отведённое время.");
            // Принудительно прерываем зависшие потоки.
            t1.Interrupt();
            t2.Interrupt();
        }
        else
        {
            Console.WriteLine("Потоки завершились (deadlock не произошёл, возможно, повезло).");
        }

        // Часть 2: БЕЗОПАСНЫЕ переводы с упорядочиванием блокировок.
        List<Thread> safeThreads = new List<Thread>();

        for (int i = 0; i < transferCount; i++)
        {
            decimal amount = (decimal)(rnd.NextDouble() * 100 + 1);
            BankAccount from = accounts[rnd.Next(accounts.Count)];
            BankAccount to = accounts[rnd.Next(accounts.Count)];

            Thread t = new Thread(() =>
            {
                from.SafeTransferWithMonitor(to, amount);
            });
            safeThreads.Add(t);

        }

        foreach (Thread t in safeThreads)
            t.Start();

        foreach (Thread t in safeThreads)
            t.Join();

        Console.WriteLine($"Безопасные переводы ({transferCount} шт.) выполнены успешно, deadlock отсутствует.");

        // Выведем балансы счетов после всех переводов (сумма должна сохраниться).
        decimal total = 0;

        foreach (var acc in accounts)
            total += acc.Balance;

        Console.WriteLine($"Суммарный баланс всех счетов: {total:F2} (должен быть {expectedTotal})");
    }
}