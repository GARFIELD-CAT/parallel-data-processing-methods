using System;
using System.Threading;

public class ResourcePool : IDisposable
{
    private readonly SemaphoreSlim _sem;

    public ResourcePool(int maxResources)
    {
        if (maxResources <= 0) throw new ArgumentOutOfRangeException(nameof(maxResources));
        _sem = new SemaphoreSlim(maxResources, maxResources);
    }

    public void AcquireResource()
    {
        _sem.Wait(); // блокирует до получения ресурса
        // Ресурс считается "захваченным" — пользователь должен вызвать ReleaseResource
    }

    public bool TryAcquireResource(int timeoutMs)
    {
        return _sem.Wait(timeoutMs);
    }

    public void ReleaseResource()
    {
        // Всегда освобождаем семафор
        _sem.Release();
    }

    public int AvailableCount => _sem.CurrentCount;

    public void Dispose() => _sem.Dispose();
}
