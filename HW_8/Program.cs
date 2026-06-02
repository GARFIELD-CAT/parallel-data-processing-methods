using System.Globalization;


public class Program
{
    private struct SizeResult
    {
        public int Size;
        public long FilterSeqMs, FilterParMs;
        public bool FilterOk;
        public long AggSeqMs, AggParMs;
        public bool AggOk;
        public long TransSeqMs, TransParMs;
        public bool TransOk;
    }

    public static void Main(string[] args)
    {
        int[] sizes = { 10_000, 100_000, 1_000_000 };
        SizeResult? onMillion = null;

        foreach (var size in sizes)
        {
            var r = RunFullCheckForSize(size);
            if (size == 1_000_000)
                onMillion = r;
        }

        var benchmark = new PLINQBenchmark();

        benchmark.CompareAllApproaches();
        Console.WriteLine();

        var degrees = benchmark.BenchmarkDifferentDegrees(1_000_000);
        var best = degrees.OrderBy(p => p.Value).First();
        long baseline = degrees[1];
        double optimizationSpeedup = best.Value > 0 ? (double)baseline / best.Value : 0;

        if (onMillion.HasValue)
            PrintSummary(onMillion.Value, best.Key, optimizationSpeedup);

        DemoQueryOptimizerPresets();

        Console.WriteLine();
        Console.WriteLine("Готово.");
    }

    private static SizeResult RunFullCheckForSize(int size)
    {
        Console.WriteLine($"--- Размер данных: {size:N0} записей ---");
        Console.WriteLine();

        var analyzer = new DataAnalyzer();
        var data = DataGenerator.GenerateMarketData(size);
        var benchmark = new PLINQBenchmark();
        var result = new SizeResult { Size = size };

        // ============ Фильтрация ============
        var f = benchmark.BenchmarkFilter(size, iterations: 3);
        var fSeq = analyzer.FilterSequential(data, 100m, 500m).ToList();
        var fPar = analyzer.FilterParallel(data, 100m, 500m).ToList();
        bool filterOk = fSeq.Count == fPar.Count
                        && fSeq.Select(d => d.Id).OrderBy(x => x)
                           .SequenceEqual(fPar.Select(d => d.Id).OrderBy(x => x));

        result.FilterSeqMs = f.sequentialMs;
        result.FilterParMs = f.parallelMs;
        result.FilterOk = filterOk;
        PrintOperationBlock("Фильтрация данных:", f.sequentialMs, f.parallelMs, filterOk);

        // ============ Агрегация ============
        var a = benchmark.BenchmarkAggregation(size, iterations: 3);
        var aSeq = analyzer.AggregateSequential(data);
        var aPar = analyzer.AggregateParallel(data);
        bool aggOk = aSeq.Count == aPar.Count
                     && aSeq.Min == aPar.Min
                     && aSeq.Max == aPar.Max
                     && Math.Abs(aSeq.Sum - aPar.Sum) < 0.01m
                     && Math.Abs(aSeq.Average - aPar.Average) < 0.01m;

        result.AggSeqMs = a.sequentialMs;
        result.AggParMs = a.parallelMs;
        result.AggOk = aggOk;
        PrintOperationBlock("Агрегация данных:", a.sequentialMs, a.parallelMs, aggOk);

        // ============ Трансформация (SMA-10) ============
        var t = benchmark.BenchmarkTransformation(size, iterations: 3);
        var tSeq = analyzer.TransformSequential(data).ToList();
        var tPar = analyzer.TransformParallel(data).ToList();

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

        result.TransSeqMs = t.sequentialMs;
        result.TransParMs = t.parallelMs;
        result.TransOk = transformOk;
        PrintOperationBlock("Трансформация данных:", t.sequentialMs, t.parallelMs, transformOk);

        // ============ Соединение (Join) ============
        // Для Join берём сокращённые наборы. Полное соединение
        // миллионов записей даёт многомиллиардные результаты.
        int joinSize = size / 100;
        var data1 = data.Take(joinSize * 10).ToList();
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
        Console.WriteLine($"Соединение данных (Join, {joinSize * 10}×{joinSize}):");
        Console.WriteLine($"  Последовательный запрос: {jSeqMs} мс, результатов: {jSeq.Count:N0}");
        Console.WriteLine($"  Параллельный запрос: {jParMs} мс, результатов: {jPar.Count:N0}");
        Console.WriteLine($"  Результаты совпадают по количеству: {YesNo(joinOk)}");
        Console.WriteLine();

        return result;
    }

    private static void PrintOperationBlock(string header, long seqMs, long parMs, bool ok)
    {
        double speedup = parMs > 0 ? (double)seqMs / parMs : 0;
        Console.WriteLine(header);
        Console.WriteLine($"  Последовательный запрос: {seqMs} мс");
        Console.WriteLine($"  Параллельный запрос: {parMs} мс");
        Console.WriteLine($"  Ускорение: {speedup:F2}x");
        Console.WriteLine($"  Результаты совпадают: {YesNo(ok)}");
        Console.WriteLine();
    }

    private static void PrintSummary(SizeResult r, int bestDegree, double optimizationSpeedup)
    {
        Console.WriteLine("=== Результаты тестирования PLINQ ===");

        PrintOperationBlock($"Фильтрация данных ({r.Size} записей):",
            r.FilterSeqMs, r.FilterParMs, r.FilterOk);
        PrintOperationBlock($"Агрегация данных ({r.Size} записей):",
            r.AggSeqMs, r.AggParMs, r.AggOk);
        PrintOperationBlock($"Трансформация данных ({r.Size} записей):",
            r.TransSeqMs, r.TransParMs, r.TransOk);

        Console.WriteLine("Оптимизация запросов:");
        Console.WriteLine($"  Оптимальная степень параллелизма: {bestDegree} {CoresWord(bestDegree)}");
        Console.WriteLine($"  Ускорение с оптимизацией: {optimizationSpeedup:F2}x");
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
        Console.WriteLine($"  OptimizeForLargeData: найдено {largeCount:N0}, {sw.ElapsedMilliseconds} мс");

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
        Console.WriteLine($"  OptimizeForLowLatency: найдено {lowLatencyCount:N0}, {sw.ElapsedMilliseconds} мс");
    }

    private static string CoresWord(int n)
    {
        int mod100 = n % 100;
        int mod10 = n % 10;
        if (mod100 >= 11 && mod100 <= 14) return "ядер";
        if (mod10 == 1) return "ядро";
        if (mod10 >= 2 && mod10 <= 4) return "ядра";
        return "ядер";
    }

    private static string YesNo(bool ok) => ok ? "Да" : "Нет";
}