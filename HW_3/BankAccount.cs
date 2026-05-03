using System;
using System.Threading;

public class BankAccount
{
    private readonly object _lock = new object();
    private decimal _balance;

    private static long _nextId = 0;

    // Уникальный идентификатор данного счёта.
    private readonly long _id;

    public BankAccount(decimal initialBalance)
    {
        _id = Interlocked.Increment(ref _nextId);
        _balance = initialBalance;
    }

    public decimal Balance
    {
        get { return _balance; }
    }

    // ---------- Базовая реализация (без синхронизации) ----------
    // Эти методы не содержат никакой защиты, при одновременном вызове из нескольких потоков
    // возникают гонки данных (data race), что приводит к некорректному итоговому балансу.
    public void Deposit(decimal amount)
    {
        _balance += amount;
    }

    public void Withdraw(decimal amount)
    {
        _balance -= amount;
    }


    public void Transfer(BankAccount target, decimal amount)
    {
        Withdraw(amount);
        target.Deposit(amount);
    }

    // ---------- Реализация с использованием lock ----------
    // Ключевое слово lock является синтаксическим сахаром для Monitor.Enter/Exit.
    // Оно гарантирует, что только один поток одновременно выполняет блок кода.
    public void DepositWithLock(decimal amount)
    {
        lock (_lock)
        {
            _balance += amount;
        }
    }

    public void WithdrawWithLock(decimal amount)
    {
        lock (_lock)
        {
            _balance -= amount;
        }
    }

    /// Перевод с использованием lock. Важно: блокировка захватывается на текущем счете (this),
    /// но не на целевом счете. Это может привести к взаимной блокировке (deadlock),
    /// если другой поток попытается перевести средства в обратном направлении.
    public void TransferWithLock(BankAccount target, decimal amount)
    {
        lock (_lock)
        {
            WithdrawWithLock(amount);
            target.DepositWithLock(amount);
        }
    }

    // ---------- Реализация с использованием Monitor ----------
    // Monitor предоставляет более тонкий контроль: можно задать таймаут, явно вызвать Exit.
    // ВАЖНО: Monitor.Exit обязательно должен вызываться в блоке finally, чтобы избежать вечной блокировки при исключениях.

    public void DepositWithMonitor(decimal amount)
    {
        Monitor.Enter(_lock); // захват блокировки
        try
        {
            _balance += amount;
        }
        finally
        {
            Monitor.Exit(_lock); // освобождение блокировки в любом случае
        }
    }

    public void WithdrawWithMonitor(decimal amount)
    {
        Monitor.Enter(_lock);
        try
        {
            _balance -= amount;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// Перевод с использованием Monitor (НЕБЕЗОПАСНЫЙ вариант для демонстрации deadlock).
    /// Захватываем сначала свой _lock, потом _lock цели.
    public void TransferWithMonitor(BankAccount target, decimal amount)
    {
        Monitor.Enter(_lock);
        try
        {
            WithdrawWithMonitor(amount);   // повторный захват того же объекта (_lock) разрешён
            target.DepositWithMonitor(amount); // потенциальный deadlock
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// Безопасный перевод с упорядочиванием блокировок для предотвращения deadlock.
    /// Блокировки захватываются всегда в одном и том же порядке, основанном на хэш-коде объектов.
    public void SafeTransferWithMonitor(BankAccount target, decimal amount)
    {
        // Упорядочиваем объекты, чтобы избежать циклического ожидания.
        object firstLock = this._lock;
        object secondLock = target._lock;

        if (this._id > target._id)
        {
            firstLock = target._lock;
            secondLock = this._lock;
        }

        bool firstAcquired = false;
        bool secondAcquired = false;

        try
        {
            Monitor.Enter(firstLock);
            firstAcquired = true;
            Monitor.Enter(secondLock);
            secondAcquired = true;

            _balance -= amount;
            target._balance += amount;
        }
        finally
        {
            if (secondAcquired) Monitor.Exit(secondLock);
            if (firstAcquired) Monitor.Exit(firstLock);
        }
    }

    // ---------- Реализация с таймаутами (Monitor.TryEnter) ----------
    // TryEnter пытается захватить блокировку за указанное время, возвращает false, если не удалось.
    public bool DepositWithTimeout(decimal amount, int timeoutMs)
    {
        if (Monitor.TryEnter(_lock, timeoutMs))
        {
            try
            {
                _balance += amount;
                return true;
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }
        return false; // не удалось захватить блокировку вовремя
    }

    public bool WithdrawWithTimeout(decimal amount, int timeoutMs)
    {
        if (Monitor.TryEnter(_lock, timeoutMs))
        {
            try
            {
                _balance -= amount;
                return true;
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }
        return false;
    }
}