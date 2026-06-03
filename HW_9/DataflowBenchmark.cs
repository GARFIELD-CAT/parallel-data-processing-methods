using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace DataflowApp;

/// <summary>
/// Класс для измерения и сравнения производительности разных подходов
/// к обработке потоковых данных.
/// </summary>
public class DataflowBenchmark
{
    private readonly DataflowPipeline _pipelineFactory = new();

    /// <summary>
    /// Бенчмарк простого пайплайна Dataflow.
    /// Возвращает (время мс, обработано сообщений, пропускная способность).
    /// </summary>
    public (long ElapsedMs, int Processed, double Throughput)
        BenchmarkSimplePipeline(int messageCount)
    {
        var pipeline = _pipelineFactory.BuildSimplePipeline();

        var sw = Stopwatch.StartNew();

        // Отправляем сообщения в пайплайн.
        for (int i = 0; i < messageCount; i++)
        {
            pipeline.Input.Post(i);
        }

        // Закрываем вход и ждём, пока всё обработается.
        pipeline.Input.Complete();
        pipeline.Completion.Wait();

        sw.Stop();

        int processed = pipeline.GetProcessedCount();
        double throughput = processed / Math.Max(sw.Elapsed.TotalSeconds, 0.0001);

        return (sw.ElapsedMilliseconds, processed, throughput);
    }

    /// <summary>
    /// Бенчмарк сложного пайплайна с ветвлениями.
    /// </summary>
    public (long ElapsedMs, int Processed)
        BenchmarkComplexPipeline(int messageCount)
    {
        var pipeline = _pipelineFactory.BuildComplexPipeline();

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < messageCount; i++)
        {
            pipeline.Input.Post(i);
        }

        pipeline.Input.Complete();
        pipeline.Completion.Wait();

        sw.Stop();
        return (sw.ElapsedMilliseconds, pipeline.GetProcessedCount());
    }

    /// <summary>
    /// Бенчмарк последовательной обработки — для сравнения.
    /// Делает ту же работу, что и BuildSimplePipeline (x * 2 + инкремент),
    /// но в одном потоке без Dataflow.
    /// </summary>
    public (long ElapsedMs, int Processed)
        BenchmarkSequentialProcessing(int messageCount)
    {
        int processed = 0;

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < messageCount; i++)
        {
            int transformed = i * 2;
            // Имитация "приёмника".
            _ = transformed; // используем результат, чтобы JIT не выкинул вычисление
            processed++;
        }

        sw.Stop();
        return (sw.ElapsedMilliseconds, processed);
    }

    /// <summary>
    /// Выполнить сравнение всех подходов и распечатать результаты.
    /// </summary>
    public void CompareAllApproaches(int messageCount = 100_000)
    {
        Console.WriteLine();
        Console.WriteLine("=== Сравнение подходов к обработке потоковых данных ===");
        Console.WriteLine($"Количество сообщений: {messageCount:N0}");
        Console.WriteLine();

        var simple = BenchmarkSimplePipeline(messageCount);
        Console.WriteLine("Простой Dataflow пайплайн:");
        Console.WriteLine($"  Время выполнения:    {simple.ElapsedMs} мс");
        Console.WriteLine($"  Обработано сообщений: {simple.Processed:N0}");
        Console.WriteLine($"  Пропускная способность: {simple.Throughput:N0} сообщений/сек");
        Console.WriteLine();

        var complex = BenchmarkComplexPipeline(messageCount);
        double complexThroughput = complex.Processed / Math.Max(complex.ElapsedMs / 1000.0, 0.0001);
        Console.WriteLine("Сложный Dataflow пайплайн:");
        Console.WriteLine($"  Время выполнения:    {complex.ElapsedMs} мс");
        Console.WriteLine($"  Обработано сообщений: {complex.Processed:N0}");
        Console.WriteLine($"  Пропускная способность: {complexThroughput:N0} сообщений/сек");
        Console.WriteLine();

        var sequential = BenchmarkSequentialProcessing(messageCount);
        double seqThroughput = sequential.Processed / Math.Max(sequential.ElapsedMs / 1000.0, 0.0001);
        Console.WriteLine("Последовательная обработка:");
        Console.WriteLine($"  Время выполнения:    {sequential.ElapsedMs} мс");
        Console.WriteLine($"  Обработано сообщений: {sequential.Processed:N0}");
        Console.WriteLine($"  Пропускная способность: {seqThroughput:N0} сообщений/сек");
        Console.WriteLine();

        // Учтите: при ОЧЕНЬ лёгкой обработке последовательный код часто БЫСТРЕЕ,
        // потому что Dataflow тратит время на оркестрацию (передачу между блоками).
        // Преимущество Dataflow проявляется на тяжёлой обработке (IO, CPU-bound вычисления).
        double speedup = sequential.ElapsedMs / Math.Max((double)simple.ElapsedMs, 0.0001);
        Console.WriteLine($"Отношение Sequential / Dataflow: {speedup:F2}x");
        Console.WriteLine("(>1 = Dataflow быстрее; <1 = последовательный быстрее на этой нагрузке)");
    }
}