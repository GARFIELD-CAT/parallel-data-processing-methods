using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        const int dataSize = 10_000_000;
        int randomSeed = 42;

        // Генерация данных
        Console.WriteLine("Генерация данных...");
        decimal[] data = GenerateData(dataSize, randomSeed);

        var processor = new TaskProcessor();

        // Последовательная обработка
        var sw = Stopwatch.StartNew();
        var seqRes = processor.ProcessDataSequential(data);
        sw.Stop();
        long seqMs = sw.ElapsedMilliseconds;
        await AsyncLogger.LogAsync($"Finished ProcessDataSequential at {DateTime.Now:u}");

        // ThreadPool обработка
        sw.Restart();
        var tpRes = processor.ProcessDataWithThreadPool(data);
        sw.Stop();
        long tpMs = sw.ElapsedMilliseconds;
        await AsyncLogger.LogAsync($"Finished ProcessDataWithThreadPool at {DateTime.Now:u}");

        // TAP обработка (Task-based)
        sw.Restart();
        var tapTask = processor.ProcessDataAsync(data);
        tapTask.Wait();
        var tapRes = tapTask.Result;
        sw.Stop();
        long tapMs = sw.ElapsedMilliseconds;
        await AsyncLogger.LogAsync($"Finished ProcessDataAsync at {DateTime.Now:u}");

        // APM обработка (Begin/End)
        sw.Restart();
        var asyncResult = processor.BeginProcessData(data, null, null);
        var apmRes = processor.EndProcessData(asyncResult);
        sw.Stop();
        long apmMs = sw.ElapsedMilliseconds;
        await AsyncLogger.LogAsync($"Finished APM at {DateTime.Now:u}");

        // Сравнение
        bool equalTp = seqRes.SequenceEqual(tpRes);
        bool equalTap = seqRes.SequenceEqual(tapRes);
        bool equalApm = seqRes.SequenceEqual(apmRes);
        bool allEqual = equalTp && equalTap && equalApm;

        Console.WriteLine();
        Console.WriteLine("=== Результаты обработки ===");
        Console.WriteLine($"Размер данных: {dataSize:N0} элементов");
        Console.WriteLine($"Последовательная обработка: {seqMs} мс");
        Console.WriteLine($"ThreadPool обработка: {tpMs} мс");
        Console.WriteLine($"TAP обработка: {tapMs} мс");
        Console.WriteLine($"APM обработка: {apmMs} мс");
        double SafeDiv(long a, long b) => b == 0 ? 0.0 : (double)a / b;
        Console.WriteLine($"Ускорение ThreadPool: {SafeDiv(seqMs, tpMs):F2}x");
        Console.WriteLine($"Ускорение TAP: {SafeDiv(seqMs, tapMs):F2}x");
        Console.WriteLine($"Ускорение APM: {SafeDiv(seqMs, apmMs):F2}x");
        Console.WriteLine($"Результаты совпадают: {(allEqual ? "Да" : "Нет")}");

        // Пример логирования (асинхронно) — логи складываем в файл results.log
        await AsyncLogger.LogAsync($"Finished processing at {DateTime.Now:u}. AllEqual={allEqual}");

        // Пример APM-логирования с callback
        AsyncLogger.LogWithCallback($"Finished APM callback at {DateTime.Now:u}", () =>
        {
            Console.WriteLine("APM логирование завершено (callback).");
        });

        Console.WriteLine("Готово.");
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
}
