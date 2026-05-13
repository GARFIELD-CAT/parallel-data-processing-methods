using System;
using System.Threading;

public static class CrossProcessSync
{
    // Выполнение действия под глобальным именованным мьютексом (без таймаута)
    public static void ExecuteWithGlobalLock(string mutexName, Action action)
    {
        if (string.IsNullOrEmpty(mutexName)) throw new ArgumentException(nameof(mutexName));
        Mutex? mutex = null;
        bool createdNew = false;
        try
        {
            // Создаём/открываем именованный мьютекс
            mutex = new Mutex(false, mutexName, out createdNew);
            mutex.WaitOne(); // блокируем до освобождения
            action();
        }
        finally
        {
            try
            {
                mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Если текущий поток не владеет мьютексом — ошибку выше пробрасываем
                throw;
            }
            finally
            {
                mutex?.Dispose();
            }
        }
    }

    // С таймаутом; возвращает true при выполнении action
    public static bool TryExecuteWithGlobalLock(string mutexName, Action action, int timeoutMs)
    {
        if (string.IsNullOrEmpty(mutexName)) throw new ArgumentException(nameof(mutexName));
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, mutexName);
            bool entered = mutex.WaitOne(timeoutMs);
            if (!entered) return false;
            try
            {
                action();
                return true;
            }
            finally
            {
                try { mutex.ReleaseMutex(); }
                catch (ApplicationException) { throw; }
            }
        }
        finally
        {
            mutex?.Dispose();
        }
    }
}
