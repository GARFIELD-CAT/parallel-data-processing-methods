using System;
using System.Threading;

public class BankAccount
{
    private static int _idCounter = 0;
    public int Id { get; }
    public decimal Balance { get; private set; }
    private readonly object _lock = new object();

    public BankAccount(decimal initial = 0m)
    {
        Id = Interlocked.Increment(ref _idCounter);
        Balance = initial;
    }

    // -------------------------
    // 1) Базовые методы (без синхронизации)
    // -------------------------
    public void Deposit(decimal amount)
    {
        // Демонстрация: потенциально небезопасно при параллельном доступе
        Balance += amount;
    }

    public void Withdraw(decimal amount)
    {
        Balance -= amount;
    }

    public void Transfer(BankAccount target, decimal amount)
    {
        this.Withdraw(amount);
        target.Deposit(amount);
    }

    // -------------------------
    // 2) Реализация с lock
    // -------------------------
    public void DepositWithLock(decimal amount)
    {
        lock (_lock)
        {
            Balance += amount;
        }
    }

    public void WithdrawWithLock(decimal amount)
    {
        lock (_lock)
        {
            Balance -= amount;
        }
    }

    public void TransferWithLock(BankAccount target, decimal amount)
    {
        // Чтобы избежать deadlock, захватываем блокировки в порядке Id
        BankAccount first = this.Id < target.Id ? this : target;
        BankAccount second = this.Id < target.Id ? target : this;

        lock (first._lock)
        {
            lock (second._lock)
            {
                // Если first == target, то снятие/добавление корректны по отношению к this/target
                if (first == this)
                {
                    this.Balance -= amount;
                    target.Balance += amount;
                }
                else
                {
                    target.Balance -= amount;
                    this.Balance += amount;
                }
            }
        }
    }

    // -------------------------
    // 3) Реализация с Monitor
    // -------------------------
    public void DepositWithMonitor(decimal amount)
    {
        bool lockTaken = false;
        try
        {
            Monitor.Enter(_lock, ref lockTaken);
            Balance += amount;
        }
        finally
        {
            if (lockTaken) Monitor.Exit(_lock);
        }
    }

    public void WithdrawWithMonitor(decimal amount)
    {
        bool lockTaken = false;
        try
        {
            Monitor.Enter(_lock, ref lockTaken);
            Balance -= amount;
        }
        finally
        {
            if (lockTaken) Monitor.Exit(_lock);
        }
    }

    public void TransferWithMonitor(BankAccount target, decimal amount)
    {
        // Захват в едином порядке по Id для предотвращения deadlock
        BankAccount first = this.Id < target.Id ? this : target;
        BankAccount second = this.Id < target.Id ? target : this;

        bool firstTaken = false;
        bool secondTaken = false;
        try
        {
            Monitor.Enter(first._lock, ref firstTaken);
            Monitor.Enter(second._lock, ref secondTaken);

            if (first == this)
            {
                this.Balance -= amount;
                target.Balance += amount;
            }
            else
            {
                target.Balance -= amount;
                this.Balance += amount;
            }
        }
        finally
        {
            if (secondTaken) Monitor.Exit(second._lock);
            if (firstTaken) Monitor.Exit(first._lock);
        }
    }

    // -------------------------
    // 4) Таймауты c Monitor.TryEnter
    // -------------------------
    public bool DepositWithTimeout(decimal amount, int timeoutMs)
    {
        bool taken = false;
        try
        {
            if (Monitor.TryEnter(_lock, timeoutMs))
            {
                taken = true;
                Balance += amount;
                return true;
            }
            else
            {
                return false;
            }
        }
        finally
        {
            if (taken) Monitor.Exit(_lock);
        }
    }

    public bool WithdrawWithTimeout(decimal amount, int timeoutMs)
    {
        bool taken = false;
        try
        {
            if (Monitor.TryEnter(_lock, timeoutMs))
            {
                taken = true;
                Balance -= amount;
                return true;
            }
            else
            {
                return false;
            }
        }
        finally
        {
            if (taken) Monitor.Exit(_lock);
        }
    }
}
