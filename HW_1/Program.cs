using System;


class Program
{
    static void Main()
    {
        const int dataSize = 10_000_000;
        const int threadCount = 4;
        const int randomSeed = 42;

        // Генерация данных
        decimal[] data = GenerateData(dataSize, randomSeed);

        // Запуск последовательной обработки
        var (seqTime, seqResult) = PerformanceMeter.MeasureExecutionTime(
            () => DataProcessor.ProcessDataSequential(data),
            "Последовательная обработка");

        // Запуск параллельной обработки в threadCount потоков
        var (parTime, parResult) = PerformanceMeter.MeasureExecutionTime(
            () => DataProcessor.ProcessDataParallel(data, threadCount),
            $"Параллельная обработка ({threadCount} потоков)");

        bool equal = PerformanceMeter.CompareResults(seqResult, parResult);
        double speedup = seqTime > 0 ? Math.Round(seqTime / (double)parTime, 2) : 0;

        Console.WriteLine();
        Console.WriteLine("=== Результаты обработки ===");
        Console.WriteLine("Размер данных: {0:N0} элементов", dataSize);
        Console.WriteLine("Последовательная обработка: {0} мс", seqTime);
        Console.WriteLine("Параллельная обработка ({0} потоков): {1} мс", threadCount, parTime);
        Console.WriteLine("Ускорение: {0}x", speedup);
        Console.WriteLine("Результаты совпадают: {0}", equal ? "Да" : "Нет");
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
