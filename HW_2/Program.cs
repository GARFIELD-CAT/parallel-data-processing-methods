using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


class Program
{
    static void Main(string[] args)
    {
        const int dataSize = 10_000_000;
        const int randomSeed = 42;

        // Генерация данных
        decimal[] data = GenerateData(dataSize, randomSeed);

        var processor = new TaskProcessor();

        // Последовательная обработка
        var sw = Stopwatch.StartNew();
        decimal[] seq = ProcessDataSequential(data);
        sw.Stop();
        long seqMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"Последовательная обработка: {seqMs} мс");

        // ThreadPool обработка
        sw.Restart();
        decimal[] tp = processor.ProcessDataWithThreadPool(data);
        sw.Stop();
        long tpMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"ThreadPool обработка: {tpMs} мс");

        // TAP обработка
        sw.Restart();
        decimal[] tap = processor.ProcessDataAsync(data).GetAwaiter().GetResult();
        sw.Stop();
        long tapMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"TAP обработка: {tapMs} мс");

        // APM обработка
        sw.Restart();
        IAsyncResult ar = processor.ProcessDataWithAPM(data, null, null);
        // Ожидаем
        var result = ar.AsyncWaitHandle;
        result.WaitOne();
        decimal[] apm = processor.EndProcessData(ar);
        sw.Stop();
        long apmMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"APM обработка: {apmMs} мс");

        // Сравнение результатов (точность 0.0001)
        bool equalTp = CompareArrays(seq, tp);
        bool equalTap = CompareArrays(seq, tap);
        bool equalApm = CompareArrays(seq, apm);
        bool allEqual = equalTp && equalTap && equalApm;

        double speedupTp = Math.Round((double)seqMs / Math.Max(1, tpMs), 2);
        double speedupTap = Math.Round((double)seqMs / Math.Max(1, tapMs), 2);
        double speedupApm = Math.Round((double)seqMs / Math.Max(1, apmMs), 2);

        string summary = $@"
=== Результаты обработки ===
Размер данных: {dataSize:N0} элементов
Последовательная обработка: {seqMs} мс
ThreadPool обработка: {tpMs} мс
TAP обработка: {tapMs} мс
APM обработка: {apmMs} мс
Ускорение ThreadPool: {speedupTp}x
Ускорение TAP: {speedupTap}x
Ускорение APM: {speedupApm}x
Результаты совпадают: {(allEqual ? "Да" : "Нет")}
";
        Console.WriteLine(summary);

        // Логируем асинхронно время (пример)
        AsyncLogger.LogAsync($"Summary: seq={seqMs} мс, tp={tpMs} мс, tap={tapMs} мс, apm={apmMs} мс").GetAwaiter().GetResult();

        // Пример логирования с callback (APM-style)
        AsyncLogger.LogWithCallback("Completed APM log entry", () => Console.WriteLine("Log callback invoked."));

        // Убедимся, что сообщение с callback успеет записаться
        Thread.Sleep(200);
    }

    static decimal[] GenerateData(int dataSize, int randomSeed)
    {
        var random = new Random(randomSeed);
        decimal[] data = new decimal[dataSize];

        // Массив размером dataSize с элементами double между 1.0 and 1000.0
        for (int i = 0; i < dataSize; i++)
        {
            data[i] = (decimal)(random.NextDouble() * 999.0 + 1.0);
        }

        return data;
    }

    // Простая последовательная обработка
    public static decimal[] ProcessDataSequential(decimal[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        int n = data.Length;
        var result = new decimal[n];

        for (int i = 0; i < n; i++)
        {
            // Временный приведение к double для Math.*; результат обратно в decimal
            double v = (double)data[i];
            double r = Math.Sqrt(v) * Math.Log10(v + 1.0);
            result[i] = (decimal)r;
        }

        return result;
    }


    static bool CompareArrays(decimal[] a, decimal[] b, decimal tolerance = 0.0001m)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (Math.Abs(a[i] - b[i]) > tolerance) return false;
        }
        return true;
    }
}
