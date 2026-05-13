using System;
using System.Collections.Concurrent;
using System.Linq;

public class CacheItem
{
    public object Value { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan ExpirationTime { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsExpired()
    {
        bool result = DateTime.UtcNow - Timestamp > ExpirationTime;
        return result;
    }

    public CacheItem(object value, DateTime timestamp)
    {
        Value = value;
        Timestamp = timestamp;
    }
}

public class ConcurrentCache
{
    public ConcurrentDictionary<string, ConcurrentBag<CacheItem>> Cache { get; } =
        new ConcurrentDictionary<string, ConcurrentBag<CacheItem>>(StringComparer.Ordinal);

    public void AddToCache(string key, object value)
    {
        var bag = Cache.GetOrAdd(key, _ => new ConcurrentBag<CacheItem>());
        bag.Add(
            new CacheItem(value, DateTime.UtcNow)
        );
    }

    public bool TryGetFromCache(string key, out object value)
    {
        value = null;

        if (!Cache.TryGetValue(key, out var bag)) return false;

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

    public bool RemoveFromCache(string key)
    {
        return Cache.TryRemove(key, out _);
    }

    public void ClearCache()
    {
        Cache.Clear();
    }

    public int GetCacheSize()
    {
        return Cache.Values.Sum(b => b.Count);
    }
}
