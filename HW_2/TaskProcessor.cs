using System;


// Класс для обработки задач данных
public class TaskProcessor
{
    // Разделяем работу на N частей, где N = min(8, Environment.ProcessorCount)
    // Это соответствует требованию "на 8 частей", но не жестко кодирует потоков при малом числе CPU.
    private int GetPartsCount() => Math.Min(8, Environment.ProcessorCount);

    // Процесс одного элемента — вынесено в отдельный метод для ясности
    public static decimal ProcessItem(decimal value)
    {
        double v = (double)value;
        double result = Math.Sqrt(v) * Math.Log10(v + 1.0);

        return (decimal)result;
    }

    // Использует ThreadPool и CountdownEvent для синхронизации
    public decimal[] ProcessDataWithThreadPool(decimal[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        int length = data.Length;
        decimal[] result = new decimal[length];

        int parts = GetPartsCount();
        int partSize = length / parts;
        var countdown = new CountdownEvent(parts);
        Exception? capturedException = null;

        for (int p = 0; p < parts; p++)
        {
            int start = p * partSize;
            int end = (p == parts - 1) ? length : start + partSize; // последний кусок может быть больше
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Обрабатываем свою часть и записываем в результирующий массив.
                    // Запись безопасна, так как сегменты не пересекаются.
                    for (int i = start; i < end; i++)
                    {
                        result[i] = ProcessItem(data[i]);
                    }
                }
                catch (Exception ex)
                {
                    // Сохраняем первую ошибку, остальные потоки могут продолжать
                    Interlocked.CompareExchange(ref capturedException, ex, null);
                }
                finally
                {
                    countdown.Signal();
                }
            });
        }

        // Ждём завершения всех задач
        countdown.Wait();
        if (capturedException != null) throw capturedException;
        return result;
    }

    // TAP: Task-based async pattern. Использует Task.Run и Task.WhenAll.
    public Task<decimal[]> ProcessDataAsync(decimal[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        int length = data.Length;
        decimal[] result = new decimal[length];

        int parts = GetPartsCount();
        int partSize = length / parts;
        var tasks = new List<Task>(parts);

        for (int p = 0; p < parts; p++)
        {
            int start = p * partSize;
            int end = (p == parts - 1) ? length : start + partSize;
            // Каждая задача выполняется в фоновой нише через Task.Run
            tasks.Add(Task.Run(() =>
            {
                for (int i = start; i < end; i++)
                {
                    result[i] = ProcessItem(data[i]);
                }
            }));
        }

        return Task.WhenAll(tasks).ContinueWith(_ => result);
    }

    // --- APM (Begin/End) ---
    // Вспомогательный класс IAsyncResult
    private class ProcessAsyncResult : IAsyncResult
    {
        private readonly ManualResetEvent _waitHandle = new(false);
        public decimal[]? Result;
        public Exception? Exception;
        public AsyncCallback? Callback;
        public object? AsyncState { get; }
        public bool CompletedSynchronously => false;
        public bool IsCompleted { get; private set; }
        public WaitHandle AsyncWaitHandle => _waitHandle;

        public ProcessAsyncResult(object? state)
        {
            AsyncState = state;
        }

        public void SetCompleted()
        {
            IsCompleted = true;
            _waitHandle.Set();
            Callback?.Invoke(this);
        }
    }

    // BeginProcessData: стартует обработку в ThreadPool и возвращает IAsyncResult
    public IAsyncResult BeginProcessData(decimal[] data, AsyncCallback? callback, object? state)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        var ar = new ProcessAsyncResult(state)
        {
            Callback = callback
        };

        // Обработка в ThreadPool — полностью в рамках APM (без async/await)
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var res = new decimal[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    res[i] = ProcessItem(data[i]);
                }
                ar.Result = res;
            }
            catch (Exception ex)
            {
                ar.Exception = ex;
            }
            finally
            {
                ar.SetCompleted();
            }
        });

        return ar;
    }

    // EndProcessData: ожидает завершения операции и возвращает результат или пробрасывает исключение
    public decimal[] EndProcessData(IAsyncResult asyncResult)
    {
        if (asyncResult is not ProcessAsyncResult ar) throw new ArgumentException("Invalid IAsyncResult", nameof(asyncResult));
        // Ждём сигнала
        ar.AsyncWaitHandle.WaitOne();
        if (ar.Exception != null) throw ar.Exception;
        return ar.Result ?? Array.Empty<decimal>();
    }

    public decimal[] ProcessDataSequential(decimal[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        var result = new decimal[data.Length];

        for (int i = 0; i < data.Length; i++)
        {
            // Временное приведение к double для Math.*; результат обратно в decimal
            double v = (double)data[i];
            double r = Math.Sqrt(v) * Math.Log10(v + 1.0);
            result[i] = (decimal)r;
        }

        return result;
    }

}
