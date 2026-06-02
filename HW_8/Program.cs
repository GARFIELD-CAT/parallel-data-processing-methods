using System;
using System.Collections.Generic;
using System.Linq;


public class Program
{
    public static void Main(string[] args)
    {
        int[] sizes = { 10_000, 100_000, 1_000_000 };
        foreach (var size in sizes)
            RunFullCheckForSize(size);

        // 2) Подбор оптимальной степени параллелизма
        Console.WriteLine("=== Поиск оптимальной степени параллелизма (1 000 000 записей) ===");
        var benchmark = new PLINQBenchmark();
        var degrees = benchmark.BenchmarkDifferentDegrees(1_000_000);
        foreach (var kvp in degrees)
            Console.WriteLine($"  {kvp.Key,2} ядер(а): {kvp.Value} мс");

        // Найдём самую быструю настройку
        var best = degrees.OrderBy(p => p.Value).First();
        // Сравним её с однопоточным выполнением — это и будет «ускорение с оптимизацией».
        long baseline = degrees[1];
        double speedupWithOptimization = best.Value > 0
            ? (double)baseline / best.Value
            : 0;

        Console.WriteLine();
        Console.WriteLine("=== Итог ===");
        Console.WriteLine($"  Оптимальная степень параллелизма: {best.Key} ядер");
        Console.WriteLine($"  Ускорение с оптимизацией: {speedupWithOptimization:F2}x");
        Console.WriteLine();

        // 3) Пример использования пресетов QueryOptimizer
        DemoQueryOptimizerPresets();

        Console.WriteLine();
        Console.WriteLine("Готово.");

        benchmark.CompareAllApproaches();
    }

    private static void RunFullCheckForSize(int size)
    {
        Console.WriteLine($"--- Размер данных: {size:N0} записей ---");

        var analyzer = new DataAnalyzer();
        var data = DataGenerator.GenerateMarketData(size);

        // ============ Фильтрация ============
        var benchmark = new PLINQBenchmark();
        var f = benchmark.BenchmarkFilter(size, iterations: 3);

        var fSeq = analyzer.FilterSequential(data, 100m, 500m).ToList();
        var fPar = analyzer.FilterParallel(data, 100m, 500m).ToList();
        // Параллельный фильтр может вернуть элементы в другом порядке,
        // поэтому сравниваем как множества по Id.
        bool filterOk = fSeq.Count == fPar.Count
                        && fSeq.Select(d => d.Id).OrderBy(x => x)
                           .SequenceEqual(fPar.Select(d => d.Id).OrderBy(x => x));

        Console.WriteLine("Фильтрация:");
        Console.WriteLine($"  Последовательный: {f.sequentialMs} мс");
        Console.WriteLine($"  Параллельный:     {f.parallelMs} мс");
        Console.WriteLine($"  Ускорение: {f.speedup:F2}x");
        Console.WriteLine($"  Результаты совпадают: {(filterOk ? "Да" : "Нет")}");
        Console.WriteLine();

        // ============ Агрегация ============
        var a = benchmark.BenchmarkAggregation(size, iterations: 3);
        var aSeq = analyzer.AggregateSequential(data);
        var aPar = analyzer.AggregateParallel(data);
        // Сравниваем агрегаты (count/min/max — точно; sum/avg — с разумным эпсилон).
        bool aggOk = aSeq.Count == aPar.Count
                     && aSeq.Min == aPar.Min
                     && aSeq.Max == aPar.Max
                     && Math.Abs(aSeq.Sum - aPar.Sum) < 0.01m
                     && Math.Abs(aSeq.Average - aPar.Average) < 0.01m;

        Console.WriteLine("Агрегация:");
        Console.WriteLine($"  Последовательный: {a.sequentialMs} мс");
        Console.WriteLine($"  Параллельный:     {a.parallelMs} мс");
        Console.WriteLine($"  Ускорение: {a.speedup:F2}x");
        Console.WriteLine($"  Результаты совпадают: {(aggOk ? "Да" : "Нет")}");
        Console.WriteLine($"  (последовательно) {aSeq}");
        Console.WriteLine();

        // ============ Трансформация ============
        var t = benchmark.BenchmarkTransformation(size, iterations: 3);
        var tSeq = analyzer.TransformSequential(data).ToList();
        var tPar = analyzer.TransformParallel(data).ToList();
        // Здесь порядок важен — благодаря AsOrdered() он сохранён.
        // Сравниваем поэлементно с эпсилон.
        bool transformOk = tSeq.Count == tPar.Count;
        if (transformOk)
        {
            for (int i = 0; i < tSeq.Count; i++)
            {
                if (Math.Abs(tSeq[i] - tPar[i]) > 0.0001m)
                {
                    transformOk = false;
                    break;
                }
            }
        }

        Console.WriteLine("Трансформация (SMA-10):");
        Console.WriteLine($"  Последовательный: {t.sequentialMs} мс");
        Console.WriteLine($"  Параллельный:     {t.parallelMs} мс");
        Console.WriteLine($"  Ускорение: {t.speedup:F2}x");
        Console.WriteLine($"  Результаты совпадают: {(transformOk ? "Да" : "Нет")}");
        Console.WriteLine();

        // ============ Соединение (Join) ============
        // Для Join используем сокращённые наборы (1000 × 1000), потому что
        // полное соединение миллионов записей даёт многомиллиардные результаты.
        int joinSize = Math.Min(1000, size);
        var data1 = data.Take(joinSize).ToList();
        var data2 = DataGenerator.GenerateMarketData(size).Skip(size / 2).Take(joinSize).ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var jSeq = analyzer.JoinSequential(data1, data2).ToList();
        sw.Stop();
        long jSeqMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var jPar = analyzer.JoinParallel(data1, data2).ToList();
        sw.Stop();
        long jParMs = sw.ElapsedMilliseconds;

        bool joinOk = jSeq.Count == jPar.Count;
        Console.WriteLine($"Соединение (Join, {joinSize}×{joinSize}):");
        Console.WriteLine($"  Последовательный: {jSeqMs} мс, результатов: {jSeq.Count}");
        Console.WriteLine($"  Параллельный:     {jParMs} мс, результатов: {jPar.Count}");
        Console.WriteLine($"  Результаты совпадают по количеству: {(joinOk ? "Да" : "Нет")}");
        Console.WriteLine();
    }

    private static void DemoQueryOptimizerPresets()
    {
        Console.WriteLine("=== Демонстрация пресетов QueryOptimizer ===");

        var optimizer = new QueryOptimizer();
        var data = DataGenerator.GenerateMarketData(200_000);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int largeCount = optimizer.OptimizeQueryForLargeData(data)
                                  .Count(d => d.Price > 500m);
        sw.Stop();
        Console.WriteLine($"  OptimizeForLargeData: найдено {largeCount}, {sw.ElapsedMilliseconds} мс");

        sw.Restart();
        var orderedFirst = optimizer.OptimizeQueryForOrderDependent(data)
                                    .Select(d => d.Price)
                                    .Take(5)
                                    .ToList();
        sw.Stop();
        Console.WriteLine($"  OptimizeForOrderDependent: первые 5 цен (по порядку): " +
                          $"[{string.Join(", ", orderedFirst.Select(p => p.ToString("F2")))}], " +
                          $"{sw.ElapsedMilliseconds} мс");

        sw.Restart();
        int lowLatencyCount = optimizer.OptimizeQueryForLowLatency(data)
                                       .Count(d => d.Volume > 500_000);
        sw.Stop();
        Console.WriteLine($"  OptimizeForLowLatency: найдено {lowLatencyCount}, {sw.ElapsedMilliseconds} мс");
    }
}
