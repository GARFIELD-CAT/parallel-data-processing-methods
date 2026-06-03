using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace DataflowApp;

/// <summary>
/// Класс, демонстрирующий разные способы построения пайплайнов TPL Dataflow.
///
/// Каждый метод Build*** возвращает объект <see cref="PipelineHandle{T}"/>,
/// в котором лежит:
///   - Input:           блок, куда нужно отправлять сообщения (точка входа);
///   - Completion:      задача, по которой можно дождаться полного завершения;
///   - GetProcessedCount: функция, возвращающая количество обработанных сообщений.
///
/// Такая обёртка нужна, чтобы вызывающий код (Program / Benchmark)
/// не знал о внутреннем устройстве пайплайна — только об интерфейсе.
/// </summary>
public class DataflowPipeline
{
    /// <summary>
    /// Удобная обёртка для возврата пайплайна "наружу".
    /// T — это тип сообщения, которое принимает пайплайн на входе.
    /// </summary>
    public class PipelineHandle<T>
    {
        /// <summary>Точка входа: сюда отправляем сообщения через Post или SendAsync.</summary>
        public required ITargetBlock<T> Input { get; init; }

        /// <summary>Задача завершения всего пайплайна (когда обработаны все сообщения).</summary>
        public required Task Completion { get; init; }

        /// <summary>Возвращает количество обработанных сообщений (на момент вызова).</summary>
        public required Func<int> GetProcessedCount { get; init; }
    }

    // =============================================================
    // 1. ПРОСТОЙ ПАЙПЛАЙН: BufferBlock -> TransformBlock -> ActionBlock
    // =============================================================

    /// <summary>
    /// Простой линейный пайплайн из 3 блоков:
    /// 
    ///    [BufferBlock] --> [TransformBlock] --> [ActionBlock]
    ///       вход            обработка             приёмник
    /// 
    /// Каждое число умножается на 2, а ActionBlock считает обработанные сообщения.
    /// </summary>
    public PipelineHandle<int> BuildSimplePipeline()
    {
        // Счётчик обработанных сообщений. Локальная переменная, захваченная замыканием —
        // безопасно для Interlocked, потому что C# превращает её в поле скрытого класса.
        int processedCount = 0;

        // 1. Источник: входной буфер. Просто хранит элементы до передачи дальше.
        var bufferBlock = new BufferBlock<int>();

        // 2. Преобразование. Лямбда СИНХРОННАЯ — это требование задания
        //    (никаких async/await внутри блоков).
        var transformBlock = new TransformBlock<int, int>(item => item * 2);

        // 3. Приёмник: увеличивает счётчик.
        //    Interlocked.Increment делает инкремент атомарным —
        //    на случай, если блок настроен на параллельную обработку.
        var actionBlock = new ActionBlock<int>(item =>
        {
            Interlocked.Increment(ref processedCount);
        });

        // Опции связывания:
        //   PropagateCompletion = true означает, что когда блок-источник
        //   получит Complete() и обработает все сообщения, он автоматически
        //   вызовет Complete() на блоке-приёмнике. Это удобно для линейных пайплайнов.
        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

        // Связываем блоки в цепочку.
        bufferBlock.LinkTo(transformBlock, linkOptions);
        transformBlock.LinkTo(actionBlock, linkOptions);

        return new PipelineHandle<int>
        {
            Input = bufferBlock,
            // Когда actionBlock завершится — весь пайплайн завершён.
            Completion = actionBlock.Completion,
            GetProcessedCount = () => processedCount
        };
    }

    // =============================================================
    // 2. СЛОЖНЫЙ ПАЙПЛАЙН С ВЕТВЛЕНИЯМИ И ОБЪЕДИНЕНИЕМ
    // =============================================================

    /// <summary>
    /// Пайплайн с ветвлением по предикату:
    /// 
    ///                  [BufferBlock] (вход)
    ///                        |
    ///                  [Math.Abs]          (нормализация)
    ///                        |
    ///                 +------+------+
    ///                 |             |
    ///         (чётные)        (нечётные)
    ///             |                 |
    ///        [квадрат]         [умножить на 3]
    ///             |                 |
    ///             +------+----------+
    ///                    |
    ///               [ActionBlock]     (общий приёмник)
    /// 
    /// При ветвлении нельзя просто включить PropagateCompletion на каждой ветке —
    /// первая же завершившаяся ветка завершит приёмник, и вторая получит ошибку.
    /// Поэтому мы вручную ждём обе ветки и затем закрываем приёмник.
    /// </summary>
    public PipelineHandle<int> BuildComplexPipeline()
    {
        int processedCount = 0;

        var inputBuffer = new BufferBlock<int>();

        // Нормализация: берём абсолютное значение.
        // MaxDegreeOfParallelism > 1 позволяет блоку обрабатывать
        // несколько сообщений параллельно — Dataflow сам управляет потоками.
        var absBlock = new TransformBlock<int, int>(
            item => Math.Abs(item),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            });

        // Ветка для чётных чисел: возводит в квадрат.
        var evenBranch = new TransformBlock<int, int>(item => item * item);

        // Ветка для нечётных чисел: умножает на 3.
        var oddBranch = new TransformBlock<int, int>(item => item * 3);

        // Общий приёмник: считает обработанные сообщения.
        var sinkBlock = new ActionBlock<int>(item =>
        {
            Interlocked.Increment(ref processedCount);
        });

        var linkOptionsPropagate = new DataflowLinkOptions { PropagateCompletion = true };

        // inputBuffer -> absBlock: с автозавершением.
        inputBuffer.LinkTo(absBlock, linkOptionsPropagate);

        // Из absBlock — два LinkTo с разными ПРЕДИКАТАМИ.
        // Каждое сообщение пойдёт только в ту ветку, чей предикат вернул true.
        absBlock.LinkTo(evenBranch, item => item % 2 == 0);
        absBlock.LinkTo(oddBranch, item => item % 2 != 0);

        // Хорошая практика: добавить "заглушку" NullTarget на случай,
        // если ни один предикат не сработает (для int таких случаев нет,
        // но в общем случае это спасает от подвисания сообщений в буфере).
        absBlock.LinkTo(DataflowBlock.NullTarget<int>());

        // Из веток — в общий приёмник. БЕЗ PropagateCompletion!
        // Иначе первая завершённая ветка закроет sinkBlock, и вторая упадёт.
        evenBranch.LinkTo(sinkBlock);
        oddBranch.LinkTo(sinkBlock);

        // Ручное управление завершением сложного пайплайна:
        // когда absBlock завершён -> завершаем обе ветки ->
        // когда обе ветки завершены -> завершаем приёмник.
        // Использование ContinueWith здесь — это синхронизация завершения,
        // а не запуск параллельной обработки (что запрещено).
        var pipelineCompletion = absBlock.Completion.ContinueWith(_ =>
        {
            evenBranch.Complete();
            oddBranch.Complete();
            // Ждём обе ветки и закрываем приёмник.
            Task.WhenAll(evenBranch.Completion, oddBranch.Completion).Wait();
            sinkBlock.Complete();
            sinkBlock.Completion.Wait();
        });

        return new PipelineHandle<int>
        {
            Input = inputBuffer,
            Completion = pipelineCompletion,
            GetProcessedCount = () => processedCount
        };
    }

    // =============================================================
    // 3. ПАЙПЛАЙН С РАССЫЛКОЙ (BROADCAST)
    // =============================================================

    /// <summary>
    /// Пайплайн с рассылкой одного и того же сообщения нескольким получателям:
    /// 
    ///   [BufferBlock] -> [BroadcastBlock] -> [ActionBlock #1]
    ///                                     -> [ActionBlock #2]
    ///                                     -> [ActionBlock #3]
    /// 
    /// BroadcastBlock хранит только ПОСЛЕДНЕЕ сообщение и копирует его
    /// всем подписчикам. Полезно, например, для рыночных котировок —
    /// одна цена идёт сразу в несколько обработчиков (логгер, сигналы, дашборд).
    /// </summary>
    public PipelineHandle<int> BuildBroadcastPipeline()
    {
        int processedCount = 0;

        var inputBuffer = new BufferBlock<int>();

        // BroadcastBlock: cloningFunction просто возвращает само значение
        // (для int копирование тривиально). Для классов сюда можно положить
        // настоящее клонирование, чтобы получатели не делили один объект.
        var broadcastBlock = new BroadcastBlock<int>(value => value);

        // Три "получателя": каждый делает что-то своё.
        // Здесь все они просто увеличивают общий счётчик,
        // чтобы можно было проверить, что все три получили сообщение.
        var receiver1 = new ActionBlock<int>(_ => Interlocked.Increment(ref processedCount));
        var receiver2 = new ActionBlock<int>(_ => Interlocked.Increment(ref processedCount));
        var receiver3 = new ActionBlock<int>(_ => Interlocked.Increment(ref processedCount));

        var linkOptionsPropagate = new DataflowLinkOptions { PropagateCompletion = true };

        inputBuffer.LinkTo(broadcastBlock, linkOptionsPropagate);
        broadcastBlock.LinkTo(receiver1, linkOptionsPropagate);
        broadcastBlock.LinkTo(receiver2, linkOptionsPropagate);
        broadcastBlock.LinkTo(receiver3, linkOptionsPropagate);

        // Пайплайн считается завершённым, когда все три получателя закончили.
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

    // =============================================================
    // 4. ПАЙПЛАЙН С ОГРАНИЧЕНИЕМ (THROTTLING)
    // =============================================================

    /// <summary>
    /// Пайплайн с ограниченным размером буфера.
    /// Если буфер переполнен, Post вернёт false, а SendAsync будет ждать
    /// свободного места. Так мы реализуем backpressure (обратное давление):
    /// быстрый продьюсер не "забивает" медленного консьюмера.
    /// </summary>
    public PipelineHandle<int> BuildThrottledPipeline(int maxMessages)
    {
        int processedCount = 0;

        // BoundedCapacity ограничивает количество сообщений ВНУТРИ блока.
        var blockOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = maxMessages,
            // EnsureOrdered = true (значение по умолчанию) — сохраняем порядок сообщений.
            EnsureOrdered = true
        };

        var inputBuffer = new BufferBlock<int>(new DataflowBlockOptions
        {
            BoundedCapacity = maxMessages
        });

        // Имитируем "тяжёлую" обработку — небольшие вычисления.
        // Они нужны, чтобы продемонстрировать, как буфер заполняется и сбрасывается.
        var transformBlock = new TransformBlock<int, int>(item =>
        {
            // Просто немного работы (умножение/деление), без Thread.Sleep,
            // потому что блокировать поток внутри блока — плохая практика.
            int result = item;
            for (int i = 0; i < 100; i++)
                result = (result * 31 + 17) % 1_000_000;
            return result;
        }, blockOptions);

        var actionBlock = new ActionBlock<int>(item =>
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

    // =============================================================
    // 5. ПАЙПЛАЙН С "ПРИОРИТЕЗАЦИЕЙ"
    // =============================================================

    /// <summary>
    /// Псевдо-приоритезированный пайплайн.
    /// 
    /// В TPL Dataflow нет встроенной приоритезации сообщений.
    /// Поэтому мы делаем "ручную" приоритезацию через два входных буфера:
    ///   - highPriorityBuffer (отправляйте сюда важные сообщения)
    ///   - lowPriorityBuffer  (отправляйте сюда обычные)
    /// 
    /// Оба буфера связаны с одним приёмником. Поскольку highPriorityBuffer
    /// связан ПЕРВЫМ, Dataflow при наличии сообщений в обоих буферах
    /// будет отдавать предпочтение ему.
    /// 
    /// Это не "жёсткий" приоритет (нет гарантии), но в большинстве случаев работает.
    /// </summary>
    public PrioritizedPipelineHandle BuildPrioritizedPipeline()
    {
        int highProcessed = 0;
        int lowProcessed = 0;

        var highPriorityBuffer = new BufferBlock<string>();
        var lowPriorityBuffer = new BufferBlock<string>();

        // Общий приёмник: помечает, откуда пришло сообщение.
        // (Здесь различаем по префиксу строки — для демонстрации.)
        var sinkBlock = new ActionBlock<string>(message =>
        {
            if (message.StartsWith("HIGH:"))
                Interlocked.Increment(ref highProcessed);
            else
                Interlocked.Increment(ref lowProcessed);
        });

        // Сначала связываем высокоприоритетный буфер — он "первый в очереди".
        highPriorityBuffer.LinkTo(sinkBlock);
        lowPriorityBuffer.LinkTo(sinkBlock);

        // Завершение: ждём оба буфера, потом закрываем приёмник.
        var completionTask = Task.WhenAll(
            highPriorityBuffer.Completion,
            lowPriorityBuffer.Completion).ContinueWith(_ =>
        {
            sinkBlock.Complete();
            sinkBlock.Completion.Wait();
        });

        return new PrioritizedPipelineHandle
        {
            HighPriorityInput = highPriorityBuffer,
            LowPriorityInput = lowPriorityBuffer,
            Completion = completionTask,
            GetHighProcessed = () => highProcessed,
            GetLowProcessed = () => lowProcessed
        };
    }

    /// <summary>Handle для приоритезированного пайплайна — два входа вместо одного.</summary>
    public class PrioritizedPipelineHandle
    {
        public required ITargetBlock<string> HighPriorityInput { get; init; }
        public required ITargetBlock<string> LowPriorityInput { get; init; }
        public required Task Completion { get; init; }
        public required Func<int> GetHighProcessed { get; init; }
        public required Func<int> GetLowProcessed { get; init; }
    }
}