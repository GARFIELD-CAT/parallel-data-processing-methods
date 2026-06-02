using System.Collections.Generic;
using System.Linq;
using System.Threading;


public class QueryOptimizer
{
    public ParallelQuery<MarketData> WithDegreeOfParallelism(
        IEnumerable<MarketData> data, int degree)
    {
        return data.AsParallel().WithDegreeOfParallelism(degree);
    }

    public ParallelQuery<MarketData> WithExecutionMode(
        IEnumerable<MarketData> data, ParallelExecutionMode mode)
    {
        return data.AsParallel().WithExecutionMode(mode);
    }

    public ParallelQuery<MarketData> WithMergeOptions(
        IEnumerable<MarketData> data, ParallelMergeOptions options)
    {
        return data.AsParallel().WithMergeOptions(options);
    }

    public ParallelQuery<MarketData> WithCancellation(
        IEnumerable<MarketData> data, CancellationToken cancellationToken)
    {
        return data.AsParallel().WithCancellation(cancellationToken);
    }

    public ParallelQuery<MarketData> OptimizeQueryForLargeData(
        IEnumerable<MarketData> data)
    {
        return data
            .AsParallel()
            .WithDegreeOfParallelism(System.Environment.ProcessorCount)
            .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
            .WithMergeOptions(ParallelMergeOptions.FullyBuffered);
    }

    public ParallelQuery<MarketData> OptimizeQueryForOrderDependent(
        IEnumerable<MarketData> data)
    {
        return data
            .AsParallel()
            .AsOrdered()
            .WithDegreeOfParallelism(System.Environment.ProcessorCount)
            .WithMergeOptions(ParallelMergeOptions.AutoBuffered);
    }

    public ParallelQuery<MarketData> OptimizeQueryForLowLatency(
        IEnumerable<MarketData> data)
    {
        int degree = System.Math.Min(4, System.Environment.ProcessorCount);

        return data
            .AsParallel()
            .WithDegreeOfParallelism(degree)
            .WithMergeOptions(ParallelMergeOptions.NotBuffered);
    }
}
