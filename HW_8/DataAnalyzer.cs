using System;
using System.Collections.Generic;
using System.Linq;


public class AggregationResult
{
    public decimal Sum { get; set; }
    public decimal Average { get; set; }
    public decimal Max { get; set; }
    public decimal Min { get; set; }
    public int Count { get; set; }

    public override string ToString()
        => $"Count={Count}, Sum={Sum:F2}, Avg={Average:F2}, Min={Min:F2}, Max={Max:F2}";
}

public class DataAnalyzer
{
    public const int MovingAverageWindow = 10;

    public IEnumerable<MarketData> FilterSequential(
        IEnumerable<MarketData> data, decimal minPrice, decimal maxPrice)
    {
        return data
            .Where(d => d.Price >= minPrice && d.Price <= maxPrice);
    }

    public AggregationResult AggregateSequential(IEnumerable<MarketData> data)
    {
        return data.Aggregate(
                (Sum: 0m, Max: decimal.MinValue, Min: decimal.MaxValue, Count: 0),
                (acc, d) => (
                    acc.Sum + d.Price,
                    d.Price > acc.Max ? d.Price : acc.Max,
                    d.Price < acc.Min ? d.Price : acc.Min,
                    acc.Count + 1
                ),
                acc => new AggregationResult
                {
                    Count = acc.Count,
                    Sum = acc.Sum,
                    Average = acc.Count > 0 ? acc.Sum / acc.Count : 0,
                    Max = acc.Count > 0 ? acc.Max : 0,
                    Min = acc.Count > 0 ? acc.Min : 0,
                });
    }

    public IEnumerable<decimal> TransformSequential(IEnumerable<MarketData> data)
    {
        var prices = data
            .OrderBy(d => d.Timestamp)
            .Select(d => d.Price)
            .ToList();

        return prices
            .Select((_, i) => prices
                .Skip(Math.Max(0, i - MovingAverageWindow + 1))
                .Take(Math.Min(i + 1, MovingAverageWindow))
                .Average());
    }

    public IEnumerable<(string Symbol, decimal TotalPrice)> JoinSequential(
        IEnumerable<MarketData> data1, IEnumerable<MarketData> data2)
    {
        return data1.Join(
            data2,
            d1 => d1.Symbol,
            d2 => d2.Symbol,
            (d1, d2) => (
                d1.Symbol,
                d1.Price + d2.Price
            ));
    }

    public IEnumerable<MarketData> FilterParallel(
        IEnumerable<MarketData> data, decimal minPrice, decimal maxPrice)
    {
        return data
            .AsParallel()
            .Where(d => d.Price >= minPrice && d.Price <= maxPrice);
    }

    public AggregationResult AggregateParallel(IEnumerable<MarketData> data)
    {
        var seedFactory = () => (Sum: 0m, Max: decimal.MinValue, Min: decimal.MaxValue, Count: 0);

        return data
            .AsParallel()
            .Aggregate(
                seedFactory: seedFactory,
                updateAccumulatorFunc: (acc, d) => (
                    acc.Sum + d.Price,
                    d.Price > acc.Max ? d.Price : acc.Max,
                    d.Price < acc.Min ? d.Price : acc.Min,
                    acc.Count + 1),
                combineAccumulatorsFunc: (a, b) => (
                    a.Sum + b.Sum,
                    a.Max > b.Max ? a.Max : b.Max,
                    a.Min < b.Min ? a.Min : b.Min,
                    a.Count + b.Count),
                resultSelector: acc => new AggregationResult
                {
                    Count = acc.Count,
                    Sum = acc.Sum,
                    Average = acc.Count > 0 ? acc.Sum / acc.Count : 0,
                    Max = acc.Count > 0 ? acc.Max : 0,
                    Min = acc.Count > 0 ? acc.Min : 0,
                });

    }

    public IEnumerable<decimal> TransformParallel(IEnumerable<MarketData> data)
    {
        var prices = data
            .OrderBy(d => d.Timestamp)
            .Select(d => d.Price)
            .ToList();

        return prices
            .AsParallel()
            .AsOrdered()
            .Select((_, i) => prices
                .Skip(Math.Max(0, i - MovingAverageWindow + 1))
                .Take(Math.Min(i + 1, MovingAverageWindow))
                .Average());
    }

    public IEnumerable<(string Symbol, decimal TotalPrice)> JoinParallel(
        IEnumerable<MarketData> data1, IEnumerable<MarketData> data2)
    {
        return data1
            .AsParallel()
            .Join(
                data2
                .AsParallel(),
                d1 => d1.Symbol,
                d2 => d2.Symbol,
                (d1, d2) => (
                    d1.Symbol,
                    d1.Price + d2.Price
                ));
    }
}
