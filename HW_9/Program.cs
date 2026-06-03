using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;

namespace DataflowApp;

/// <summary>
/// Точка входа.
/// 
/// Что делает программа:
///   1. Генерирует 100 000 котировок (с фиксированным seed = 42 для воспроизводимости).
///   2. Прогоняет простой, сложный и broadcast-пайплайн на числах.
///   3. Прогоняет пайплайн обработки котировок (фильтр -> SMA -> сигнал -> "БД").
///   4. Сравнивает производительность Dataflow и последовательной обработки.
///   5. Печатает сводную статистику.
/// </summary>
internal static class Program
{
    // private const int QuoteCount = 100_000;
    private const int QuoteCount = 1_000_000;
    private static readonly string[] Symbols = { "AAPL", "GOOGL", "MSFT", "AMZN", "TSLA" };

    private static void Main()
    {
        // Поддержка UTF-8 в консоли (для русских символов).
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("=== TPL Dataflow: тестирование ===");
        Console.WriteLine();

        // 1. Генерация тестовых данных.
        Console.Write("Генерация тестовых котировок... ");
        var quotes = GenerateTestQuotes(QuoteCount);
        Console.WriteLine($"готово ({quotes.Count:N0} котировок).");
        Console.WriteLine();

        // 2. Тестирование простого пайплайна.
        var pipeline = new DataflowPipeline();
        var simpleHandle = pipeline.BuildSimplePipeline();
        var simpleSw = Stopwatch.StartNew();
        for (int i = 0; i < QuoteCount; i++) simpleHandle.Input.Post(i);
        simpleHandle.Input.Complete();
        simpleHandle.Completion.Wait();
        simpleSw.Stop();

        // 3. Тестирование сложного пайплайна.
        var complexHandle = pipeline.BuildComplexPipeline();
        var complexSw = Stopwatch.StartNew();
        for (int i = 0; i < QuoteCount; i++) complexHandle.Input.Post(i);
        complexHandle.Input.Complete();
        complexHandle.Completion.Wait();
        complexSw.Stop();

        // 4. Тестирование broadcast-пайплайна.
        var broadcastHandle = pipeline.BuildBroadcastPipeline();
        for (int i = 0; i < 1000; i++) broadcastHandle.Input.Post(i);
        broadcastHandle.Input.Complete();
        broadcastHandle.Completion.Wait();

        // 5. Тестирование пайплайна обработки рыночных данных.
        var marketProcessor = new MarketDataProcessor(movingAveragePeriod: 5);
        var tradingHandle = marketProcessor.BuildTradingPipeline();

        var tradingSw = Stopwatch.StartNew();
        foreach (var quote in quotes)
        {
            tradingHandle.Input.Post(quote);
        }
        tradingHandle.Input.Complete();
        tradingHandle.Completion.Wait();
        tradingSw.Stop();

        int signalsGenerated = tradingHandle.GetSignalsCount();
        bool calcCorrect = CheckMovingAverageCorrectness(marketProcessor);

        // 6. Сводная статистика.
        Console.WriteLine("=== Результаты тестирования TPL Dataflow ===");
        Console.WriteLine();
        Console.WriteLine("Простой пайплайн:");
        Console.WriteLine($"  Количество сообщений:    {QuoteCount:N0}");
        Console.WriteLine($"  Время выполнения:        {simpleSw.ElapsedMilliseconds} мс");
        Console.WriteLine($"  Пропускная способность:  {QuoteCount / Math.Max(simpleSw.Elapsed.TotalSeconds, 0.0001):N0} сообщений/сек");
        Console.WriteLine($"  Обработано сообщений:    {simpleHandle.GetProcessedCount():N0}");
        Console.WriteLine();

        Console.WriteLine("Сложный пайплайн:");
        Console.WriteLine($"  Количество сообщений:    {QuoteCount:N0}");
        Console.WriteLine($"  Время выполнения:        {complexSw.ElapsedMilliseconds} мс");
        Console.WriteLine($"  Пропускная способность:  {QuoteCount / Math.Max(complexSw.Elapsed.TotalSeconds, 0.0001):N0} сообщений/сек");
        Console.WriteLine($"  Обработано сообщений:    {complexHandle.GetProcessedCount():N0}");
        Console.WriteLine();

        Console.WriteLine("Broadcast пайплайн:");
        Console.WriteLine($"  Получателей: 3, сообщений на каждого: 1000");
        Console.WriteLine($"  Всего записей обработано: {broadcastHandle.GetProcessedCount():N0}");
        Console.WriteLine();

        Console.WriteLine("Пайплайн обработки торговых данных:");
        Console.WriteLine($"  Количество котировок:    {QuoteCount:N0}");
        Console.WriteLine($"  Время выполнения:        {tradingSw.ElapsedMilliseconds} мс");
        Console.WriteLine($"  Сгенерировано сигналов:  {signalsGenerated:N0}");
        Console.WriteLine($"  Корректность расчётов:   {(calcCorrect ? "Да" : "Нет")}");
        Console.WriteLine();

        // 7. Полное сравнение производительности.
        var benchmark = new DataflowBenchmark();
        benchmark.CompareAllApproaches(QuoteCount);

        // 8. Проверка корректности (без проблем = все обработаны).
        Console.WriteLine();
        bool allOk =
            simpleHandle.GetProcessedCount() == QuoteCount &&
            complexHandle.GetProcessedCount() == QuoteCount &&
            signalsGenerated == quotes.Count; // фильтрация валидна для всех сгенерированных
        Console.WriteLine(allOk
            ? "Проверка: все сообщения обработаны корректно. Проблем не обнаружено."
            : "Проверка: обнаружены расхождения между количеством отправленных и обработанных сообщений.");

        Console.WriteLine();
        Console.WriteLine("Готово.");
    }

    /// <summary>
    /// Сгенерировать список котировок с фиксированным seed (42) — для воспроизводимости.
    /// </summary>
    private static List<Quote> GenerateTestQuotes(int count)
    {
        var random = new Random(42);
        var quotes = new List<Quote>(count);
        var startTime = DateTime.UtcNow;

        for (int i = 0; i < count; i++)
        {
            quotes.Add(new Quote
            {
                Symbol = Symbols[random.Next(Symbols.Length)],
                Price = 50m + (decimal)(random.NextDouble() * 950.0),
                Volume = random.Next(100, 1_000_001),
                Timestamp = startTime.AddMilliseconds(i),
                QuoteType = random.Next(2) == 0 ? "BID" : "ASK"
            });
        }

        return quotes;
    }

    /// <summary>
    /// Простая проверка корректности расчёта SMA на синтетических данных.
    /// </summary>
    private static bool CheckMovingAverageCorrectness(MarketDataProcessor processor)
    {
        // Для цен [10, 20, 30, 40, 50] и периода 5 SMA = 30.
        var prices = new List<decimal> { 10m, 20m, 30m, 40m, 50m };
        decimal sma = processor.CalculateMovingAverage(prices, 5);
        return sma == 30m;
    }
}