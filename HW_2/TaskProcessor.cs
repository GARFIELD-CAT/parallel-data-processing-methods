using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class TaskProcessor
{
    // Обработчик для части массива: пример обработки — вычисление sqrt и умножение (любой детерминированный CPU-bound work)
    private static void ProcessPartData(decimal[] input, decimal[] output, int start, int length)
    {
        int end = start + length;

        for (int i = start; i < end; i++)
        {
            // Временный приведение к double для Math.*; результат обратно в decimal
            double v = (double)input[i];
            double r = Math.Sqrt(v) * Math.Log10(v + 1.0);
            output[i] = (decimal)r;
        }
    }

    // 1) ThreadPool-based processing: делим на 8 частей и используем ThreadPool
    public decimal[] ProcessDataWithThreadPool(decimal[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        int length = data.Length;
        decimal[] result = new decimal[length];

        int partitions = 8;
        if (partitions > length) partitions = length;

        int baseSize = length / partitions;
        int remainder = length % partitions;

        using (var countdown = new CountdownEvent(partitions))
        {
            int offset = 0;
            for (int p = 0; p < partitions; p++)
            {
                int size = baseSize + (p < remainder ? 1 : 0);
                int start = offset;
                offset += size;

                ThreadPool.QueueUserWorkItem(state =>
                {
                    try
                    {
                        ProcessPartData(data, result, start, size);
                    }
                    finally
                    {
                        countdown.Signal();
                    }
                });
            }

            // Подождём завершения всех рабочих задач
            countdown.Wait();
        }

        return result;
    }

    // 2) TAP: возвращаем Task<decimal[]>, используем Task.Run и объединяем результаты
    public Task<decimal[]> ProcessDataAsync(decimal[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        int length = data.Length;
        decimal[] result = new decimal[length];

        // Решаем деление на N равных частей, где N = Environment.ProcessorCount * 2 (пример разумного дефолта)
        int partitions = Math.Max(1, Environment.ProcessorCount * 2);
        if (partitions > length) partitions = length;
        int baseSize = length / partitions;
        int remainder = length % partitions;

        var tasks = new List<Task>();

        int offset = 0;
        for (int p = 0; p < partitions; p++)
        {
            int size = baseSize + (p < remainder ? 1 : 0);
            int start = offset;
            offset += size;

            // Task.Run выполняет работу в фоновом потоке (может использовать ThreadPool)
            tasks.Add(Task.Run(() => ProcessPartData(data, result, start, size)));
        }

        return Task.WhenAll(tasks).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                // Пробрасываем первую ошибку (Task.Exception содержит AggregateException)
                throw t.Exception!;
            }
            return result;
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    // 3) APM: BeginProcessData / EndProcessData реализация с использованием ThreadPool и IAsyncResult
    private class ProcessDataAsyncResult : IAsyncResult
    {
        private readonly ManualResetEvent _waitHandle;
        public decimal[]? Result { get; set; }
        public Exception? Exception { get; set; }
        public AsyncCallback? Callback { get; }
        public object? AsyncStateObj { get; }

        public ProcessDataAsyncResult(object? state, AsyncCallback? callback)
        {
            AsyncStateObj = state;
            Callback = callback;
            _waitHandle = new ManualResetEvent(false);
        }

        public object AsyncState => AsyncStateObj!;
        public WaitHandle AsyncWaitHandle => _waitHandle;
        public bool CompletedSynchronously => false;
        public bool IsCompleted { get; private set; }

        public void SetCompleted()
        {
            IsCompleted = true;
            _waitHandle.Set();
            Callback?.Invoke(this);
        }

        public void Dispose() => _waitHandle.Dispose();
    }

    public IAsyncResult BeginProcessData(decimal[] data, AsyncCallback? callback, object? state)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        var asyncResult = new ProcessDataAsyncResult(state, callback);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                decimal[] res = ProcessDataWithThreadPool(data); // Reuse ThreadPool method (safe)
                asyncResult.Result = res;
            }
            catch (Exception ex)
            {
                asyncResult.Exception = ex;
            }
            finally
            {
                asyncResult.SetCompleted();
            }
        });

        return asyncResult;
    }

    public decimal[] EndProcessData(IAsyncResult asyncResult)
    {
        if (asyncResult == null) throw new ArgumentNullException(nameof(asyncResult));
        if (asyncResult is not ProcessDataAsyncResult r) throw new ArgumentException("Invalid IAsyncResult", nameof(asyncResult));

        // Ждём завершения, если не завершено
        if (!r.IsCompleted)
        {
            r.AsyncWaitHandle.WaitOne();
        }

        if (r.Exception != null) throw r.Exception;
        return r.Result ?? Array.Empty<decimal>();
    }

    // Обёртка, как требовалось: ProcessDataWithAPM
    public IAsyncResult ProcessDataWithAPM(decimal[] data, AsyncCallback? callback, object? state)
    {
        return BeginProcessData(data, callback, state);
    }
}
