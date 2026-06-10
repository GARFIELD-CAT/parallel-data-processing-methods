using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;


public class DataflowBenchmark
{
    private readonly DataflowPipeline _pipelineFactory = new();

    // Бенчмарк простого пайплайна Dataflow.
    public (long ElapsedMs, int Processed, double Throughput) BenchmarkSimplePipeline(int messageCount)
    {
        var pipeline = _pipelineFactory.BuildSimplePipeline();

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < messageCount; i++)
            pipeline.Input.Post(i);

        pipeline.Input.Complete();
        pipeline.Completion.Wait();

        sw.Stop();

        int processed = pipeline.GetProcessedCount();
        double throughput = processed / Math.Max(sw.Elapsed.TotalSeconds, 0.0001);

        return (sw.ElapsedMilliseconds, processed, throughput);
    }

    // Бенчмарк сложного пайплайна с ветвлениями.
    public (long ElapsedMs, int Processed) BenchmarkComplexPipeline(int messageCount)
    {
        var pipeline = _pipelineFactory.BuildComplexPipeline();

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < messageCount; i++)
            pipeline.Input.Post(i);

        pipeline.Input.Complete();
        pipeline.Completion.Wait();

        sw.Stop();

        return (sw.ElapsedMilliseconds, pipeline.GetProcessedCount());
    }

    // Бенчмарк последовательной обработки — для сравнения.
    public (long ElapsedMs, int Processed) BenchmarkSequentialProcessing(int messageCount)
    {
        int processed = 0;
        long checksum = 0;

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < messageCount; i++)
        {
            checksum += DataflowPipeline.HeavyTransform(i);
            processed++;
        }

        sw.Stop();

        return (sw.ElapsedMilliseconds, processed);
    }

    public void CompareAllApproaches(int messageCount = 100_000)
    {
        Console.WriteLine("=== Сравнение подходов к обработке потоковых данных ===");
        Console.WriteLine();

        var simple = BenchmarkSimplePipeline(messageCount);
        Console.WriteLine("Простой Dataflow пайплайн:");
        Console.WriteLine($"  Время выполнения: {simple.ElapsedMs} мс");
        Console.WriteLine($"  Обработанные сообщения: {simple.Processed:N0}");
        Console.WriteLine($"  Пропускная способность: {simple.Throughput:N0} сообщений/сек");
        Console.WriteLine();

        var complex = BenchmarkComplexPipeline(messageCount);
        double complexThroughput = complex.Processed / Math.Max(complex.ElapsedMs / 1000.0, 0.0001);
        Console.WriteLine("Сложный Dataflow пайплайн:");
        Console.WriteLine($"  Время выполнения: {complex.ElapsedMs} мс");
        Console.WriteLine($"  Обработанные сообщения: {complex.Processed:N0}");
        Console.WriteLine($"  Пропускная способность: {complexThroughput:N0} сообщений/сек");
        Console.WriteLine();

        var sequential = BenchmarkSequentialProcessing(messageCount);
        double seqThroughput = sequential.Processed / Math.Max(sequential.ElapsedMs / 1000.0, 0.0001);
        Console.WriteLine("Последовательная обработка:");
        Console.WriteLine($"  Время выполнения: {sequential.ElapsedMs} мс");
        Console.WriteLine($"  Обработанные сообщения: {sequential.Processed:N0}");
        Console.WriteLine($"  Пропускная способность: {seqThroughput:N0} сообщений/сек");
        Console.WriteLine();

        double speedup = sequential.ElapsedMs / Math.Max((double)simple.ElapsedMs, 0.0001);
        Console.WriteLine($"Ускорение Dataflow vs Последовательная: {speedup:F2}x");
    }
}
