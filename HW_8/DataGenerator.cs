using System;
using System.Collections.Generic;

public class MarketData
{
    public int Id { get; set; }                  // уникальный идентификатор записи
    public string Symbol { get; set; } = "";     // тикер актива, например "AAPL"
    public decimal Price { get; set; }           // цена актива
    public long Volume { get; set; }             // объём торгов
    public DateTime Timestamp { get; set; }      // временная метка
    public decimal ChangePercent { get; set; }   // процент изменения цены
}


public static class DataGenerator
{
    private static readonly string[] Symbols =
    {
        "AAPL", "GOOGL", "MSFT", "AMZN", "TSLA",
        "META", "NFLX", "NVDA", "INTC", "AMD"
    };

    public static List<MarketData> GenerateMarketData(int count)
    {
        var random = new Random(42);

        var data = new List<MarketData>(count);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0);

        for (int i = 0; i < count; i++)
        {
            data.Add(new MarketData
            {
                Id = i,
                Symbol = Symbols[random.Next(Symbols.Length)],
                Price = (decimal)(random.NextDouble() * 1000 + 1),
                Volume = random.NextInt64(1_000, 1_000_000),
                Timestamp = baseTime.AddMinutes(i),
                // Изменение от -5% до +5%
                ChangePercent = (decimal)((random.NextDouble() - 0.5) * 10)
            });
        }

        return data;
    }
}
