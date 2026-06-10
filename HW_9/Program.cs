using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;


internal static class Program
{
    private const int QuoteCount = 100_000;
    private static readonly string[] Symbols = { "AAPL", "GOOGL", "MSFT", "AMZN", "TSLA" };

    private static void Main()
    {
        try
        {
            Console.Write("Генерация тестовых котировок... ");
            var quotes = GenerateTestQuotes(QuoteCount);
            Console.WriteLine($"готово ({quotes.Count:N0} котировок).");
            Console.WriteLine();

            var pipeline = new DataflowPipeline();
            var benchmark = new DataflowBenchmark();

            // Простой пайплайн
            var simpleHandle = pipeline.BuildSimplePipeline();
            var simpleSw = Stopwatch.StartNew();

            for (int i = 0; i < QuoteCount; i++)
            {
                simpleHandle.Input.Post(i);
            }

            simpleHandle.Input.Complete();
            simpleHandle.Completion.Wait();
            simpleSw.Stop();
            double simpleThroughput = QuoteCount / Math.Max(simpleSw.Elapsed.TotalSeconds, 0.0001);

            // Последовательная обработка той же нагрузки — для сравнения в сводке.
            var sequential = benchmark.BenchmarkSequentialProcessing(QuoteCount);
            double dataflowVsSequential =
                sequential.ElapsedMs / Math.Max((double)simpleSw.ElapsedMilliseconds, 0.0001);

            // Сложный пайплайн с ветвлениями
            var complexHandle = pipeline.BuildComplexPipeline();
            var complexSw = Stopwatch.StartNew();

            for (int i = 0; i < QuoteCount; i++)
            {
                complexHandle.Input.Post(i);
            }

            complexHandle.Input.Complete();
            complexHandle.Completion.Wait();
            complexSw.Stop();

            // Пайплайн с рассылкой данных
            var broadcastHandle = pipeline.BuildBroadcastPipeline();

            for (int i = 0; i < QuoteCount; i++)
            {
                broadcastHandle.Input.Post(i);
            }
            broadcastHandle.Input.Complete();
            broadcastHandle.Completion.Wait();

            // Пайплайн обработки торговых данных
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
            int tradingErrors = tradingHandle.GetErrorCount();
            bool calcCorrect = CheckMovingAverageCorrectness(marketProcessor);

            Console.WriteLine("=== Результаты тестирования TPL Dataflow ===");
            Console.WriteLine();

            Console.WriteLine("Простой пайплайн:");
            Console.WriteLine($"  Количество сообщений: {QuoteCount:N0}");
            Console.WriteLine($"  Время выполнения: {simpleSw.ElapsedMilliseconds} мс");
            Console.WriteLine($"  Пропускная способность: {simpleThroughput:N0} сообщений/сек");
            Console.WriteLine($"  Обработанные сообщения: {simpleHandle.GetProcessedCount():N0}");
            Console.WriteLine();

            Console.WriteLine("Сложный пайплайн:");
            Console.WriteLine($"  Количество сообщений: {QuoteCount:N0}");
            Console.WriteLine($"  Время выполнения: {complexSw.ElapsedMilliseconds} мс");
            Console.WriteLine($"  Пропускная способность: {QuoteCount / Math.Max(complexSw.Elapsed.TotalSeconds, 0.0001):N0} сообщений/сек");
            Console.WriteLine($"  Обработанные сообщения: {complexHandle.GetProcessedCount():N0}");
            Console.WriteLine();

            Console.WriteLine("Broadcast пайплайн:");
            Console.WriteLine($"  Получателей: 3, сообщений на каждого: {QuoteCount}");
            Console.WriteLine($"  Всего записей обработано: {broadcastHandle.GetProcessedCount():N0}");
            Console.WriteLine();

            Console.WriteLine("Пайплайн обработки торговых данных:");
            Console.WriteLine($"  Количество котировок: {QuoteCount:N0}");
            Console.WriteLine($"  Время выполнения: {tradingSw.ElapsedMilliseconds} мс");
            Console.WriteLine($"  Сгенерировано сигналов: {signalsGenerated:N0}");
            Console.WriteLine($"  Корректность расчетов: {(calcCorrect ? "Да" : "Нет")}");
            Console.WriteLine();

            Console.WriteLine("Сравнение производительности:");
            Console.WriteLine($"  Dataflow vs Последовательная: {dataflowVsSequential:F2}x");
            Console.WriteLine($"  Пропускная способность: {simpleThroughput:N0} сообщений/сек");
            Console.WriteLine();

            // Cравнение всех подходов
            benchmark.CompareAllApproaches(QuoteCount);
            Console.WriteLine();

            // Демонстрация throttled / prioritized пайплайнов
            DemonstrateAdvancedPipelines();

            // Демонстрация пользовательских блоков
            DemonstrateCustomBlocks();

            // Проверка корректности.
            bool allOk =
                simpleHandle.GetProcessedCount() == QuoteCount &&
                complexHandle.GetProcessedCount() == QuoteCount &&
                signalsGenerated == quotes.Count &&   // все сгенерированные котировки валидны
                tradingErrors == 0 &&
                calcCorrect;

            Console.WriteLine(allOk
                ? "Проверка: все сообщения обработаны корректно. Проблем не обнаружено."
                : "Проверка: обнаружены расхождения (см. счётчики выше).");

            Console.WriteLine();
            Console.WriteLine("Готово.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка во время выполнения: {ex.Message}");
        }
    }

    // Сгенерировать список котировок
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

    // Проверка корректности расчёта на синтетических данных
    private static bool CheckMovingAverageCorrectness(MarketDataProcessor processor)
    {
        var prices = new List<decimal> { 10m, 20m, 30m, 40m, 50m };
        decimal sma = processor.CalculateMovingAverage(prices, 5);
        return sma == 30m;
    }

    // Демонстрация пайплайнов с ограничением буфера и приоритезацией
    private static void DemonstrateAdvancedPipelines()
    {
        Console.WriteLine("=== Демонстрация throttled и prioritized пайплайнов ===");
        var pipeline = new DataflowPipeline();

        // Throttled: ограниченный буфер. Подаём через SendAsync, чтобы при
        // переполнении НЕ терять сообщения (Post вернул бы false), а ждать места.
        const int throttledMessages = 5_000;
        var throttled = pipeline.BuildThrottledPipeline(maxMessages: 64);

        for (int i = 0; i < throttledMessages; i++)
            throttled.Input.SendAsync(i).Wait(); // ждём свободное место

        throttled.Input.Complete();
        throttled.Completion.Wait();
        Console.WriteLine($"  Throttled (буфер 64): отправлено {throttledMessages:N0}, " +
                          $"обработано {throttled.GetProcessedCount():N0}");

        // Prioritized: два входа. Кладём поровну; высокоприоритетный связан первым.
        var prioritized = pipeline.BuildPrioritizedPipeline();
        for (int i = 0; i < 1_000; i++)
        {
            prioritized.LowPriorityInput.Post($"LOW:{i}");
            prioritized.HighPriorityInput.Post($"HIGH:{i}");
        }
        prioritized.HighPriorityInput.Complete();
        prioritized.LowPriorityInput.Complete();
        prioritized.Completion.Wait();
        Console.WriteLine($"  Prioritized: высокий приоритет — {prioritized.GetHighProcessed():N0}, " +
                          $"низкий — {prioritized.GetLowProcessed():N0}");
        Console.WriteLine();
    }

    // Демонстрация пользовательских блоков из DataflowBlocks.cs.
    private static void DemonstrateCustomBlocks()
    {
        Console.WriteLine("=== Демонстрация пользовательских блоков ===");
        var propagate = new DataflowLinkOptions { PropagateCompletion = true };

        // FilterBlock: пропускаем только чётные числа.
        int evenCount = 0;
        var filter = new FilterBlock<int>(n => n % 2 == 0);
        var evenSink = new ActionBlock<int>(_ => Interlocked.Increment(ref evenCount));
        filter.LinkTo(evenSink, propagate);

        for (int i = 0; i < 10; i++)
        {
            filter.Post(i);
        }
        filter.Complete();
        evenSink.Completion.Wait();
        Console.WriteLine($"  FilterBlock: из 10 чисел прошло чётных — {evenCount}");

        // BatchBlock: группируем по 4; остаток выталкиваем TriggerBatch().
        int batchCount = 0;
        var batch = new BatchBlock<int>(4);
        var batchSink = new ActionBlock<int[]>(_ => Interlocked.Increment(ref batchCount));
        batch.LinkTo(batchSink, propagate);
        for (int i = 0; i < 10; i++)
        {
            batch.Post(i);
        }
        batch.TriggerBatch(); // вытолкнуть остаток (10 = 4 + 4 + 2)
        batch.Complete();
        batchSink.Completion.Wait();
        Console.WriteLine($"  BatchBlock: 10 элементов по 4 -> пачек: {batchCount}");

        // AggregatorBlock: копим во временное окно и закрываем его.
        int windowItems = 0;
        var aggregator = new AggregatorBlock<int>(TimeSpan.FromSeconds(1));
        var windowSink = new ActionBlock<List<int>>(w => Interlocked.Add(ref windowItems, w.Count));
        aggregator.Source.LinkTo(windowSink, propagate);
        for (int i = 0; i < 5; i++)
        {
            aggregator.Add(i);
        }
        aggregator.CompleteWindow();
        aggregator.Complete();
        windowSink.Completion.Wait();
        Console.WriteLine($"  AggregatorBlock: элементов в закрытом окне — {windowItems}");

        // ErrorHandlingBlock: намеренно бросаем исключение на одном элементе.
        int handledErrors = 0;
        var errorBlock = new ErrorHandlingBlock<int>(
            workItem: n => { if (n == 3) throw new InvalidOperationException("плохой элемент"); },
            errorHandler: _ => Interlocked.Increment(ref handledErrors));
        for (int i = 0; i < 5; i++)
        {
            errorBlock.Post(i);
        }
        errorBlock.Complete();
        errorBlock.Completion.Wait();
        Console.WriteLine($"  ErrorHandlingBlock: перехвачено ошибок — {handledErrors} (пайплайн не упал)");
        Console.WriteLine();
    }
}
