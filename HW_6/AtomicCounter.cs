using System.Threading;

namespace AtomicOperationsDemo
{
    /// <summary>
    /// Потокобезопасный счётчик на основе атомарных операций Interlocked.
    /// Не использует блокировки (lock, Monitor и т.п.).
    /// </summary>
    public class AtomicCounter
    {
        // Значение счётчика, операции с ним выполняются только через Interlocked
        private long _value;

        /// <summary>
        /// Текущее значение счётчика (только для чтения).
        /// Чтение также выполняется атомарно через Interlocked.Read.
        /// </summary>
        public long Value => Interlocked.Read(ref _value);

        /// <summary>
        /// Создаёт счётчик с указанным начальным значением.
        /// </summary>
        public AtomicCounter(long initialValue = 0)
        {
            _value = initialValue;
        }

        /// <summary>
        /// Атомарно увеличивает счётчик на 1.
        /// </summary>
        public long Increment()
        {
            return Interlocked.Increment(ref _value);
        }

        /// <summary>
        /// Атомарно уменьшает счётчик на 1.
        /// </summary>
        public long Decrement()
        {
            return Interlocked.Decrement(ref _value);
        }

        /// <summary>
        /// Атомарно прибавляет указанное значение к счётчику.
        /// </summary>
        public long Add(long value)
        {
            return Interlocked.Add(ref _value, value);
        }

        /// <summary>
        /// Атомарно заменяет значение счётчика новым и возвращает предыдущее.
        /// </summary>
        public long Exchange(long newValue)
        {
            return Interlocked.Exchange(ref _value, newValue);
        }

        /// <summary>
        /// Атомарно сравнивает текущее значение с comparand и, если они равны,
        /// заменяет его на newValue. Возвращает исходное значение.
        /// </summary>
        public long CompareExchange(long newValue, long comparand)
        {
            return Interlocked.CompareExchange(ref _value, newValue, comparand);
        }

        /// <summary>
        /// Сбрасывает счётчик в 0.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _value, 0);
        }

        /// <summary>
        /// Возвращает текущее значение и затем увеличивает счётчик на 1 (атомарно).
        /// Реализовано через CompareExchange в цикле, что гарантирует атомарность
        /// операции "получить и увеличить".
        /// </summary>
        public long GetAndIncrement()
        {
            long original, incremented;
            do
            {
                original = Interlocked.Read(ref _value);
                incremented = original + 1;
            }
            while (Interlocked.CompareExchange(ref _value, incremented, original) != original);
            return original;
        }

        /// <summary>
        /// Возвращает текущее значение и затем уменьшает счётчик на 1 (атомарно).
        /// </summary>
        public long GetAndDecrement()
        {
            long original, decremented;
            do
            {
                original = Interlocked.Read(ref _value);
                decremented = original - 1;
            }
            while (Interlocked.CompareExchange(ref _value, decremented, original) != original);
            return original;
        }
    }
}