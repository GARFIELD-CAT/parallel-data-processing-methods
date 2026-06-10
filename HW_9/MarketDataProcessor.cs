using System.Threading.Tasks.Dataflow;


public class Quote
{
    public string Symbol { get; set; } = "";

    public decimal Price { get; set; }

    public long Volume { get; set; }

    public DateTime Timestamp { get; set; }

    public string QuoteType { get; set; } = "";

    public override string ToString()
    {
        return $"{Symbol} {QuoteType} {Price:F2} (vol={Volume}) @ {Timestamp:HH:mm:ss}";
    }
}

public class TradingSignal
{
    public string Symbol { get; set; } = "";
    public string Action { get; set; } = "";  // "BUY" / "SELL" / "HOLD"
    public decimal Price { get; set; }
    public decimal MovingAverage { get; set; }
    public DateTime Timestamp { get; set; }

    public override string ToString()
    {
        return $"[{Action}] {Symbol} price={Price:F2} ma={MovingAverage:F2} @ {Timestamp:HH:mm:ss}";
    }
}

public class MarketDataProcessor
{
    public int MovingAveragePeriod { get; }
    private readonly Dictionary<string, Queue<decimal>> _priceHistory = new();
    private readonly object _historyLock = new();

    public MarketDataProcessor(int movingAveragePeriod = 5)
    {
        if (movingAveragePeriod <= 0)
            throw new ArgumentOutOfRangeException(nameof(movingAveragePeriod));

        MovingAveragePeriod = movingAveragePeriod;
    }

    public class TradingPipelineHandle
    {
        public required ITargetBlock<Quote> Input { get; init; }
        public required Task Completion { get; init; }
        public required Func<int> GetSignalsCount { get; init; }
        public required Func<int> GetErrorCount { get; init; }
        public required Func<List<TradingSignal>> GetAllSignals { get; init; }
    }

    /// Построение пайплайна обработки торговых данных:
    /// Пайплайн:
    ///   1. Фильтр валидных котировок.
    ///   2. Расчет скользящей средней по символу.
    ///   3. Генерация сигнала BUY/SELL/HOLD.
    ///   4. "Сохранение" сигналов (имитация записи в БД).
    public TradingPipelineHandle BuildTradingPipeline()
    {
        var signals = new List<TradingSignal>();
        var signalsLock = new object();
        int errorCount = 0;

        // фильтрация валидных котировок
        var filterBlock = new TransformManyBlock<Quote, Quote>(quote =>
        {
            try
            {
                bool isValid =
                    !string.IsNullOrWhiteSpace(quote.Symbol)
                    && quote.Price > 0
                    && quote.Volume > 0
                    && (quote.QuoteType == "BID" || quote.QuoteType == "ASK");

                return isValid ? new[] { quote } : Array.Empty<Quote>();
            }
            catch
            {
                Console.WriteLine($"Got error in BuildTradingPipeline: filterBlock");
                Interlocked.Increment(ref errorCount);
                return Array.Empty<Quote>();
            }
        });

        // Расчет скользящей средней
        var movingAverageBlock = new TransformBlock<Quote, (Quote Quote, decimal Ma)>(quote =>
        {
            try
            {
                List<decimal> snapshot;
                lock (_historyLock)
                {
                    if (!_priceHistory.TryGetValue(quote.Symbol, out var queue))
                    {
                        queue = new Queue<decimal>();
                        _priceHistory[quote.Symbol] = queue;
                    }

                    queue.Enqueue(quote.Price);
                    while (queue.Count > MovingAveragePeriod)
                        queue.Dequeue();

                    snapshot = queue.ToList();
                }

                decimal ma = CalculateMovingAverage(snapshot, MovingAveragePeriod);
                return (quote, ma);
            }
            catch
            {
                Console.WriteLine($"Got error in BuildTradingPipeline: movingAverageBlock");
                Interlocked.Increment(ref errorCount);
                return (quote, 0m);
            }
        });

        // Генерация торгового сигнала
        var signalBlock = new TransformBlock<(Quote Quote, decimal Ma), TradingSignal>(input =>
        {
            try
            {
                string action = GenerateTradingSignal(input.Quote.Price, input.Ma);
                return new TradingSignal
                {
                    Symbol = input.Quote.Symbol,
                    Action = action,
                    Price = input.Quote.Price,
                    MovingAverage = input.Ma,
                    Timestamp = input.Quote.Timestamp
                };
            }
            catch
            {
                Console.WriteLine($"Got error in BuildTradingPipeline: signalBlock");
                Interlocked.Increment(ref errorCount);
                return new TradingSignal
                {
                    Symbol = input.Quote.Symbol,
                    Action = "HOLD",
                    Price = input.Quote.Price,
                    MovingAverage = input.Ma,
                    Timestamp = input.Quote.Timestamp
                };
            }
        });

        // Сохранение результата
        var saveBlock = new ActionBlock<TradingSignal>(signal =>
        {
            try
            {
                lock (signalsLock)
                {
                    signals.Add(signal);
                }
            }
            catch
            {
                Console.WriteLine($"Got error in BuildTradingPipeline: saveBlock");
                Interlocked.Increment(ref errorCount);
            }
        });

        // Связываем блоки последовательно с автозавершением.
        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        filterBlock.LinkTo(movingAverageBlock, linkOptions);
        movingAverageBlock.LinkTo(signalBlock, linkOptions);
        signalBlock.LinkTo(saveBlock, linkOptions);

        return new TradingPipelineHandle
        {
            Input = filterBlock,
            Completion = saveBlock.Completion,
            GetSignalsCount = () =>
            {
                lock (signalsLock) return signals.Count;
            },
            GetErrorCount = () => errorCount,
            GetAllSignals = () =>
            {
                lock (signalsLock) return new List<TradingSignal>(signals);
            }
        };
    }

    // Рассчитать простую скользящую среднюю
    public decimal CalculateMovingAverage(List<decimal> prices, int period)
    {
        if (prices == null || prices.Count == 0)
            return 0m;

        int take = Math.Min(period, prices.Count);
        // Берём последние take цен.
        decimal sum = 0m;
        for (int i = prices.Count - take; i < prices.Count; i++)
            sum += prices[i];

        return sum / take;
    }

    // Сгенерировать торговый сигнал
    public string GenerateTradingSignal(decimal currentPrice, decimal movingAverage)
    {
        if (movingAverage <= 0m)
            return "HOLD";

        decimal deviation = (currentPrice - movingAverage) / movingAverage;

        if (deviation > 0.005m) return "SELL";
        if (deviation < -0.005m) return "BUY";
        return "HOLD";
    }
}
