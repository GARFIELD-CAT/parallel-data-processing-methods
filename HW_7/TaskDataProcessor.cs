using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class TaskDataProcessor
{
    private const int DefaultPartitions = 4;

    public decimal ProcessItem(decimal value)
    {
        return (decimal)Math.Round(Math.Sqrt((double)value), 2);
    }

    private decimal[] ProcessDataPart(decimal[] data, int start, int end)
    {
        decimal[] result = new decimal[end - start];
        int resultIndex = 0;

        for (int i = start; i < end; i++)
        {
            result[resultIndex] = ProcessItem(data[i]);
            resultIndex++;
        }

        return result;
    }

    private decimal[] JoinProcessedData(Task<decimal[][]> task, int finalLength)
    {
        List<decimal> result = new List<decimal>(finalLength);

        foreach (var chunk in task.Result)
        {
            result.AddRange(chunk);
        }

        return result.ToArray();
    }

    public Task<decimal[]> ProcessDataAsync(decimal[] data)
    {
        Task<decimal[]> task = Task.Run(() =>
        {
            return ProcessDataPart(data, 0, data.Length);
        });

        return task;
    }

    public Task<decimal[]> ProcessDataInParallel(decimal[] data, int degreeOfParallelism)
    {
        if (degreeOfParallelism <= 0)
            throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism));

        int chunkSize = (data.Length + degreeOfParallelism - 1) / degreeOfParallelism;
        var tasks = new Task<decimal[]>[degreeOfParallelism];

        for (int i = 0; i < degreeOfParallelism; i++)
        {
            int start = i * chunkSize;
            int end = Math.Min(start + chunkSize, data.Length);

            if (start >= data.Length)
            {
                tasks[i] = Task.FromResult(Array.Empty<decimal>());
                continue;
            }

            tasks[i] = Task.Run(() =>
            {
                decimal[] chunk = ProcessDataPart(data, start, end);

                return chunk;
            });
        }

        return Task.WhenAll(tasks).ContinueWith(completedAll =>
        {
            return JoinProcessedData(completedAll, data.Length);
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    public Task<decimal[]> ProcessDataWithFactory(decimal[] data)
    {
        int partitionCount = DefaultPartitions;
        int chunkSize = (data.Length + partitionCount - 1) / partitionCount;
        var tasks = new Task<decimal[]>[partitionCount];

        for (int i = 0; i < partitionCount; i++)
        {
            int start = i * chunkSize;
            int end = Math.Min(start + chunkSize, data.Length);
            if (start >= data.Length)
            {
                tasks[i] = Task.FromResult(Array.Empty<decimal>());
                continue;
            }

            tasks[i] = Task.Factory.StartNew(() =>
            {
                decimal[] chunk = ProcessDataPart(data, start, end);

                return chunk;
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        }

        return Task.WhenAll(tasks).ContinueWith(completedAll =>
        {
            return JoinProcessedData(completedAll, data.Length);
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    public Task<decimal[]> ProcessDataWithCancellation(decimal[] data, CancellationToken cancellationToken)
    {
        int partitionCount = DefaultPartitions;
        int chunkSize = (data.Length + partitionCount - 1) / partitionCount;
        var tasks = new Task<decimal[]>[partitionCount];

        for (int i = 0; i < partitionCount; i++)
        {
            int start = i * chunkSize;
            int end = Math.Min(start + chunkSize, data.Length);
            if (start >= data.Length)
            {
                tasks[i] = Task.FromResult(Array.Empty<decimal>());
                continue;
            }

            tasks[i] = Task.Factory.StartNew(() =>
            {
                decimal[] chunk = new decimal[end - start];

                for (int j = start; j < end; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    chunk[j - start] = ProcessItem(data[j]);
                }
                return chunk;

            }, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default);
        }

        return Task.WhenAll(tasks).ContinueWith(completedAll =>
        {
            return JoinProcessedData(completedAll, data.Length);
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    public Task<decimal[]> ProcessDataWithTimeout(decimal[] data, TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        Task<decimal[]> task = ProcessDataWithCancellation(data, cts.Token);
        task.ContinueWith(_ => cts.Dispose(), TaskContinuationOptions.ExecuteSynchronously);

        return task;
    }
}
