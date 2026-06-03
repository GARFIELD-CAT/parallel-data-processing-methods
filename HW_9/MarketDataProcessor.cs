using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace DataflowApp;

public class Quote
{
    public string Symbol { get; set; } = "";

    public decimal Price { get; set; }

    public long Volume { get; set; }

    public DateTime Timestamp { get; set; }

    public string QuoteType { get; set; } = "";

    public override string ToString()
        => $"{Symbol} {QuoteType} {Price:F2} (vol={Volume}) @ {Timestamp:HH:mm:ss}";
}

public class TradingSignal
{
    public string Symbol { get; set; } = "";
    public string Action { get; set; } = "";  // "BUY" / "SELL" / "HOLD"
    public decimal Price { get; set; }
    public decimal MovingAverage { get; set; }
    public DateTime Timestamp { get; set; }

    public override string ToString()
        => $"[{Action}] {Symbol} price={Price:F2} ma={MovingAverage:F2} @ {Timestamp:HH:mm:ss}";
}

/// <summary>
/// Процессор рыночных данных: строит пайплайн обработки котировок.
/// 
/// Пайплайн:
///   1. Фильтр валидных котировок (цена > 0, объём > 0).
///   2. Расчёт скользящей средней по символу.
///   3. Генерация сигнала BUY/SELL/HOLD.
///   4. "Сохранение" сигналов (имитация записи в БД).
/// </summary>
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
        public required Func<List<TradingSignal>> GetAllSignals { get; init; }
    }

    public TradingPipelineHandle BuildTradingPipeline()
    {
        // Список сгенерированных сигналов — потокобезопасное добавление.
        var signals = new List<TradingSignal>();
        var signalsLock = new object();

        // ---------- Блок 1: фильтрация валидных котировок ----------
        // Используем TransformManyBlock как фильтр: возвращаем 0 или 1 элемент.
        var filterBlock = new TransformManyBlock<Quote, Quote>(quote =>
        {
            // Простая валидация: цена и объём должны быть положительными,
            // символ не пустой, тип — BID или ASK.
            bool isValid =
                !string.IsNullOrWhiteSpace(quote.Symbol)
                && quote.Price > 0
                && quote.Volume > 0
                && (quote.QuoteType == "BID" || quote.QuoteType == "ASK");

            return isValid ? new[] { quote } : Array.Empty<Quote>();
        });

        // ---------- Блок 2: расчёт скользящей средней ----------
        // На входе — котировка, на выходе — пара (котировка, SMA).
        var movingAverageBlock = new TransformBlock<Quote, (Quote Quote, decimal Ma)>(quote =>
        {
            // Обновляем историю цен по символу.
            List<decimal> snapshot;
            lock (_historyLock)
            {
                if (!_priceHistory.TryGetValue(quote.Symbol, out var queue))
                {
                    queue = new Queue<decimal>();
                    _priceHistory[quote.Symbol] = queue;
                }

                queue.Enqueue(quote.Price);
                // Держим окно размера MovingAveragePeriod.
                while (queue.Count > MovingAveragePeriod)
                    queue.Dequeue();

                snapshot = queue.ToList();
            }

            decimal ma = CalculateMovingAverage(snapshot, MovingAveragePeriod);
            return (quote, ma);
        });

        // ---------- Блок 3: генерация торгового сигнала ----------
        var signalBlock = new TransformBlock<(Quote Quote, decimal Ma), TradingSignal>(input =>
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
        });

        // ---------- Блок 4: "сохранение" результата ----------
        // Имитируем сохранение в БД — просто кладём сигнал в список.
        var saveBlock = new ActionBlock<TradingSignal>(signal =>
        {
            lock (signalsLock)
            {
                signals.Add(signal);
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
            GetAllSignals = () =>
            {
                lock (signalsLock) return new List<TradingSignal>(signals);
            }
        };
    }

    /// <summary>
    /// Рассчитать простую скользящую среднюю (SMA).
    /// Если данных меньше period — берём столько, сколько есть.
    /// Возвращает 0, если данных нет вообще.
    /// </summary>
    public decimal CalculateMovingAverage(List<decimal> prices, int period)
    {
        if (prices == null || prices.Count == 0)
            return 0m;

        int take = Math.Min(period, prices.Count);
        // Берём последние take цен (они в Queue идут от старых к новым).
        decimal sum = 0m;
        for (int i = prices.Count - take; i < prices.Count; i++)
            sum += prices[i];

        return sum / take;
    }

    /// <summary>
    /// Сгенерировать торговый сигнал.
    /// Простая стратегия:
    ///   - цена выше средней на >0.5% -> SELL (актив "перегрет");
    ///   - цена ниже средней на >0.5% -> BUY  (актив "недооценён");
    ///   - иначе -> HOLD.
    /// </summary>
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