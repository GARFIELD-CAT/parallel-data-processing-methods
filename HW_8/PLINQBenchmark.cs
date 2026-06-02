using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;


public class PLINQBenchmark
{
    private readonly DataAnalyzer _analyzer = new DataAnalyzer();
    private const decimal MinPrice = 100m;
    private const decimal MaxPrice = 500m;


    public (long sequentialMs, long parallelMs, double speedup)
        BenchmarkFilter(int dataSize, int iterations)
    {
        var data = DataGenerator.GenerateMarketData(dataSize);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = _analyzer.FilterSequential(data, MinPrice, MaxPrice).ToList();
        sw.Stop();
        long seq = sw.ElapsedMilliseconds;

        sw.Restart();
        for (int i = 0; i < iterations; i++)
            _ = _analyzer.FilterParallel(data, MinPrice, MaxPrice).ToList();
        sw.Stop();
        long par = sw.ElapsedMilliseconds;

        double speedup = par > 0 ? (double)seq / par : 0;
        return (seq, par, speedup);
    }

    public (long sequentialMs, long parallelMs, double speedup)
        BenchmarkAggregation(int dataSize, int iterations)
    {
        var data = DataGenerator.GenerateMarketData(dataSize);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = _analyzer.AggregateSequential(data);
        sw.Stop();
        long seq = sw.ElapsedMilliseconds;

        sw.Restart();
        for (int i = 0; i < iterations; i++)
            _ = _analyzer.AggregateParallel(data);
        sw.Stop();
        long par = sw.ElapsedMilliseconds;

        double speedup = par > 0 ? (double)seq / par : 0;
        return (seq, par, speedup);
    }

    public (long sequentialMs, long parallelMs, double speedup)
        BenchmarkTransformation(int dataSize, int iterations)
    {
        var data = DataGenerator.GenerateMarketData(dataSize);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            _ = _analyzer.TransformSequential(data).ToList();
        sw.Stop();
        long seq = sw.ElapsedMilliseconds;

        sw.Restart();
        for (int i = 0; i < iterations; i++)
            _ = _analyzer.TransformParallel(data).ToList();
        sw.Stop();
        long par = sw.ElapsedMilliseconds;

        double speedup = par > 0 ? (double)seq / par : 0;
        return (seq, par, speedup);
    }

    public Dictionary<int, long> BenchmarkDifferentDegrees(int dataSize)
    {
        var data = DataGenerator.GenerateMarketData(dataSize);
        int[] degrees = { 1, 2, 4, 8, 16 };
        var results = new Dictionary<int, long>();

        foreach (var degree in degrees)
        {
            var sw = Stopwatch.StartNew();
            _ = data
                .AsParallel()
                .WithDegreeOfParallelism(degree)
                .Where(d => d.Price >= MinPrice && d.Price <= MaxPrice)
                .Select(d => new { d.Symbol, Norm = d.Price / 1000m })
                .ToList();
            sw.Stop();
            results[degree] = sw.ElapsedMilliseconds;
        }

        return results;
    }

    public void CompareAllApproaches()
    {
        const int dataSize = 1_000_000;
        const int iterations = 1;

        Console.WriteLine("=== Сравнение последовательных и параллельных запросов ===");

        var filter = BenchmarkFilter(dataSize, iterations);
        Console.WriteLine("Фильтрация данных:");
        Console.WriteLine($"  Последовательный запрос: {filter.sequentialMs} мс");
        Console.WriteLine($"  Параллельный запрос:     {filter.parallelMs} мс");
        Console.WriteLine($"  Ускорение: {filter.speedup:F2}x");
        Console.WriteLine();

        var agg = BenchmarkAggregation(dataSize, iterations);
        Console.WriteLine("Агрегация данных:");
        Console.WriteLine($"  Последовательный запрос: {agg.sequentialMs} мс");
        Console.WriteLine($"  Параллельный запрос:     {agg.parallelMs} мс");
        Console.WriteLine($"  Ускорение: {agg.speedup:F2}x");
        Console.WriteLine();

        var transform = BenchmarkTransformation(dataSize, iterations);
        Console.WriteLine("Трансформация данных:");
        Console.WriteLine($"  Последовательный запрос: {transform.sequentialMs} мс");
        Console.WriteLine($"  Параллельный запрос:     {transform.parallelMs} мс");
        Console.WriteLine($"  Ускорение: {transform.speedup:F2}x");
        Console.WriteLine();

        var degrees = BenchmarkDifferentDegrees(dataSize);
        Console.WriteLine("Оптимальная степень параллелизма:");

        foreach (var kvp in degrees)
            Console.WriteLine($"  {kvp.Key} ядер(а): {kvp.Value} мс");
    }
}
