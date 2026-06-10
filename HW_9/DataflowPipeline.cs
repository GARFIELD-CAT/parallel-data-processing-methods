using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;


public class DataflowPipeline
{
    public const int HeavyWorkIterations = 1500;

    public static int HeavyTransform(int value)
    {
        int result = value;
        for (int i = 0; i < HeavyWorkIterations; i++)
            result = (result * 30 + 16) % 1_000_000;
        return result;
    }

    public class PipelineHandle<T>
    {
        public required ITargetBlock<T> Input { get; init; }

        public required Task Completion { get; init; }

        public required Func<int> GetProcessedCount { get; init; }
    }

    // Простой пайплайн из 3 блоков (источник → обработка → приемник)
    public PipelineHandle<int> BuildSimplePipeline()
    {
        int processedCount = 0;
        int errorCount = 0;

        var bufferBlock = new BufferBlock<int>();

        var transformBlock = new TransformBlock<int, int>(item =>
        {
            try
            {
                return HeavyTransform(item);
            }
            catch
            {
                Interlocked.Increment(ref errorCount);
                Console.WriteLine("Got error in BuildSimplePipeline");
                return 0; // нейтральное значение — сообщение не теряем
            }
        },
        new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            EnsureOrdered = false
        });

        var actionBlock = new ActionBlock<int>(_ =>
        {
            Interlocked.Increment(ref processedCount);
        });

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

        bufferBlock.LinkTo(transformBlock, linkOptions);
        transformBlock.LinkTo(actionBlock, linkOptions);

        return new PipelineHandle<int>
        {
            Input = bufferBlock,
            Completion = actionBlock.Completion,
            GetProcessedCount = () => processedCount
        };
    }

    // Cложный пайплайн с ветвлениями и объединениями.
    // При ветвлении нельзя включать PropagateCompletion на обеих ветках
    // первая же завершившаяся ветка закроет приёмник и вторая упадёт.
    // Поэтому завершением управляем вручную
    public PipelineHandle<int> BuildComplexPipeline()
    {
        int processedCount = 0;
        var inputBuffer = new BufferBlock<int>();

        var absBlock = new TransformBlock<int, int>(
            item =>
            {
                try
                {
                    return Math.Abs(item);
                }
                catch
                {
                    Console.WriteLine("Got error in BuildComplexPipeline: absBlock");
                    return 0;
                }
            },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            });

        var evenBranch = new TransformBlock<int, int>(
            item =>
            {
                try
                {
                    return item * item;
                }
                catch
                {
                    Console.WriteLine("Got error in BuildComplexPipeline: evenBranch");
                    return 0;
                }
            }
        );

        var oddBranch = new TransformBlock<int, int>(
            item =>
            {
                try
                {
                    return item * 3;
                }
                catch
                {
                    Console.WriteLine("Got error in BuildComplexPipeline: oddBranch");
                    return 0;
                }
            }
        );

        var sinkBlock = new ActionBlock<int>(_ => Interlocked.Increment(ref processedCount));
        var propagate = new DataflowLinkOptions { PropagateCompletion = true };

        inputBuffer.LinkTo(absBlock, propagate);

        absBlock.LinkTo(evenBranch, item => item % 2 == 0);
        absBlock.LinkTo(oddBranch, item => item % 2 != 0);

        // Заглушка, если ни один предикат не сработает
        absBlock.LinkTo(DataflowBlock.NullTarget<int>());

        evenBranch.LinkTo(sinkBlock);
        oddBranch.LinkTo(sinkBlock);

        async Task CompleteWhenDone()
        {
            await absBlock.Completion;
            evenBranch.Complete();
            oddBranch.Complete();
            await Task.WhenAll(evenBranch.Completion, oddBranch.Completion);
            sinkBlock.Complete();
            await sinkBlock.Completion;
        }

        return new PipelineHandle<int>
        {
            Input = inputBuffer,
            Completion = CompleteWhenDone(),
            GetProcessedCount = () => processedCount
        };
    }

    // Пайплайн с рассылкой данных нескольким получателям
    public PipelineHandle<int> BuildBroadcastPipeline()
    {
        int processedCount = 0;
        var inputBuffer = new BufferBlock<int>();

        var broadcastBlock = new BroadcastBlock<int>(value => value);

        var receiver1 = new ActionBlock<int>(_ => Interlocked.Increment(ref processedCount));
        var receiver2 = new ActionBlock<int>(_ => Interlocked.Increment(ref processedCount));
        var receiver3 = new ActionBlock<int>(_ => Interlocked.Increment(ref processedCount));

        var propagate = new DataflowLinkOptions { PropagateCompletion = true };

        inputBuffer.LinkTo(broadcastBlock, propagate);
        broadcastBlock.LinkTo(receiver1, propagate);
        broadcastBlock.LinkTo(receiver2, propagate);
        broadcastBlock.LinkTo(receiver3, propagate);

        // Пайплайн завершён, когда закончили все три получателя.
        var pipelineCompletion = Task.WhenAll(
            receiver1.Completion,
            receiver2.Completion,
            receiver3.Completion);

        return new PipelineHandle<int>
        {
            Input = inputBuffer,
            Completion = pipelineCompletion,
            GetProcessedCount = () => processedCount
        };
    }

    // Пайплайн с ограничением количества сообщений
    public PipelineHandle<int> BuildThrottledPipeline(int maxMessages)
    {
        if (maxMessages <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessages));

        int processedCount = 0;

        var blockOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = maxMessages,
            EnsureOrdered = true // сохраняем порядок сообщений
        };

        var inputBuffer = new BufferBlock<int>(new DataflowBlockOptions
        {
            BoundedCapacity = maxMessages
        });

        // Немного работы, чтобы буфер реально заполнялся и сбрасывался.
        var transformBlock = new TransformBlock<int, int>(item =>
        {
            try
            {
                int result = item;
                for (int i = 0; i < 100; i++)
                    result = (result * 30 + 16) % 1_000_000;

                return result;
            }
            catch
            {
                Console.WriteLine("Got error in BuildThrottledPipeline");
                return 0;
            }
        }, blockOptions);

        var actionBlock = new ActionBlock<int>(_ =>
        {
            Interlocked.Increment(ref processedCount);
        }, blockOptions);

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

        inputBuffer.LinkTo(transformBlock, linkOptions);
        transformBlock.LinkTo(actionBlock, linkOptions);

        return new PipelineHandle<int>
        {
            Input = inputBuffer,
            Completion = actionBlock.Completion,
            GetProcessedCount = () => processedCount
        };
    }

    // Пайплайн с приоритезацией сообщений
    public PrioritizedPipelineHandle BuildPrioritizedPipeline()
    {
        int highProcessed = 0;
        int lowProcessed = 0;

        var highPriorityBuffer = new BufferBlock<string>();
        var lowPriorityBuffer = new BufferBlock<string>();

        var sinkBlock = new ActionBlock<string>(message =>
        {
            try
            {
                if (message.StartsWith("HIGH:"))
                    Interlocked.Increment(ref highProcessed);
                else
                    Interlocked.Increment(ref lowProcessed);
            }
            catch
            {
                Console.WriteLine($"Got error in BuildPrioritizedPipeline: {message}");
            }
        });

        highPriorityBuffer.LinkTo(sinkBlock);
        lowPriorityBuffer.LinkTo(sinkBlock);

        async Task CompleteWhenDone()
        {
            await Task.WhenAll(highPriorityBuffer.Completion, lowPriorityBuffer.Completion);
            sinkBlock.Complete();
            await sinkBlock.Completion;
        }

        return new PrioritizedPipelineHandle
        {
            HighPriorityInput = highPriorityBuffer,
            LowPriorityInput = lowPriorityBuffer,
            Completion = CompleteWhenDone(),
            GetHighProcessed = () => highProcessed,
            GetLowProcessed = () => lowProcessed
        };
    }

    // Результат для приоритезированного пайплайна
    public class PrioritizedPipelineHandle
    {
        public required ITargetBlock<string> HighPriorityInput { get; init; }
        public required ITargetBlock<string> LowPriorityInput { get; init; }
        public required Task Completion { get; init; }
        public required Func<int> GetHighProcessed { get; init; }
        public required Func<int> GetLowProcessed { get; init; }
    }
}
