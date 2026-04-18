using System;
using System.Diagnostics;


public static class PerformanceMeter
{
    public static (long elapsedMs, decimal[] result) MeasureExecutionTime(Func<decimal[]> action, string operationName)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        var sw = Stopwatch.StartNew();
        decimal[] result = action();
        sw.Stop();
        long ms = sw.ElapsedMilliseconds;
        Console.WriteLine("{0}: {1} мс", operationName, ms);

        return (ms, result);
    }

    public static bool CompareResults(decimal[] result1, decimal[] result2, decimal tolerance = 0.0001m)
    {
        if (result1 == null || result2 == null) return false;
        if (result1.Length != result2.Length) return false;

        for (int i = 0; i < result1.Length; i++)
        {
            decimal a = result1[i];
            decimal b = result2[i];
            decimal diff = a - b;
            // Вместо Math.Abs
            if (diff < 0) diff = -diff;
            if (diff > tolerance) return false;
        }

        return true;
    }
}
