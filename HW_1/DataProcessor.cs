using System;
using System.Threading;


public static class DataProcessor
{
    public static decimal[] ProcessDataSequential(decimal[] data)
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

    public static decimal[] ProcessDataParallel(decimal[] data, int threadCount)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        if (threadCount <= 0) throw new ArgumentOutOfRangeException(nameof(threadCount));

        var result = new decimal[data.Length];

        var doneEvents = new ManualResetEvent[threadCount];
        var exceptions = new Exception[threadCount];

        int chunkSize = data.Length / threadCount;
        int remainder = data.Length % threadCount;

        for (int tr = 0; tr < threadCount; tr++)
        {
            doneEvents[tr] = new ManualResetEvent(false);
            int localTr = tr;

            ThreadPoolWork(localTr, data, result, chunkSize, remainder, doneEvents, exceptions);
        }

        // Ожидание завершения всех потоков
        WaitHandle.WaitAll(doneEvents);

        // Если были исключения — выбрасываем первое
        for (int i = 0; i < exceptions.Length; i++)
        {
            if (exceptions[i] != null)
            {
                // Очистить события перед выбрасыванием
                foreach (var ev in doneEvents) ev.Dispose();
                throw new AggregateException("Исключение в рабочем потоке.", exceptions);
            }
        }

        // Освобождение ресурсов
        foreach (var ev in doneEvents) ev.Dispose();

        return result;
    }

    private static void ThreadPoolWork(int localTr, decimal[] data, decimal[] result, int chunkSize, int remainder, ManualResetEvent[] doneEvents, Exception[] exceptions)
    {
        Thread thread = new Thread(() =>
        {
            try
            {
                int start = localTr * chunkSize + Math.Min(localTr, remainder);
                int len = chunkSize + (localTr < remainder ? 1 : 0);
                int end = start + len;

                for (int i = start; i < end; i++)
                {
                    double v = (double)data[i];
                    double r = Math.Sqrt(v) * Math.Log10(v + 1.0);
                    result[i] = (decimal)r;
                }
            }
            catch (Exception ex)
            {
                exceptions[localTr] = ex;
            }
            finally
            {
                doneEvents[localTr].Set();
            }
        });

        thread.Start();
    }
}
