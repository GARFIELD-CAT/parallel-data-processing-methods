using System.Threading;


public class AtomicCounter
{
    private long _value;

    public long Value
    {
        get { return Interlocked.Read(ref _value); }
    }

    public AtomicCounter(long initialValue = 0)
    {
        _value = initialValue;
    }

    public long Increment()
    {
        return Interlocked.Increment(ref _value);
    }

    public long Decrement()
    {
        return Interlocked.Decrement(ref _value);
    }

    public long Add(long value)
    {
        return Interlocked.Add(ref _value, value);
    }

    public long Exchange(long newValue)
    {
        return Interlocked.Exchange(ref _value, newValue);
    }

    public long CompareExchange(long newValue, long comparand)
    {
        return Interlocked.CompareExchange(ref _value, newValue, comparand);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _value, 0);
    }

    public long GetAndIncrement()
    {
        long original, incremented;
        do
        {
            original = Value;
            incremented = original + 1;
        }
        while (CompareExchange(incremented, original) != original);

        return original;
    }

    public long GetAndDecrement()
    {
        long original, decremented;
        do
        {
            original = Interlocked.Read(ref _value);
            decremented = original - 1;
        }
        while (CompareExchange(decremented, original) != original);

        return original;
    }
}
