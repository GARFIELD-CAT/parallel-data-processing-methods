using System.Collections.Concurrent;


public class CacheItem
{
    public object Value { get; set; }
    public DateTime Timestamp { get; } = DateTime.Now;
    public TimeSpan ExpirationTime { get; set; } = TimeSpan.FromMinutes(5);

    public CacheItem(object value)
    {
        Value = value;
    }

    public bool IsExpired()
    {
        return DateTime.Now - Timestamp > ExpirationTime;
    }
}

public class ConcurrentCache
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<CacheItem>> _cache = new();

    public void AddToCache(string key, object value)
    {
        var bag = _cache.GetOrAdd(key, _ => new ConcurrentBag<CacheItem>());
        bag.Add(
            new CacheItem(value)
        );
    }

    public bool TryGetFromCache(string key, out object? value)
    {
        value = null;

        if (!_cache.TryGetValue(key, out var bag))
            return false;

        foreach (var item in bag)
        {
            if (!item.IsExpired())
            {
                value = item.Value;
                return true;
            }
        }
        return false;
    }

    public void RemoveFromCache(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public void ClearCache()
    {
        _cache.Clear();
    }

    public int GetCacheSize()
    {
        int count = 0;

        foreach (var bag in _cache.Values)
            count += bag.Count;

        return count;
    }
}