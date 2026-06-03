using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace DataflowApp;

/// <summary>
/// FilterBlock — блок, пропускающий ТОЛЬКО те сообщения, для которых
/// Predicate вернул true. Остальные просто отбрасываются.
/// 
/// Внутри это TransformManyBlock: возвращает массив с одним элементом,
/// если предикат истинен, или пустой массив, если нет.
/// </summary>
public class FilterBlock<T>
{
    private readonly TransformManyBlock<T, T> _innerBlock;

    /// <summary>Функция-предикат: true — пропустить, false — отбросить.</summary>
    public Func<T, bool> Predicate { get; }

    /// <summary>Задача завершения блока.</summary>
    public Task Completion => _innerBlock.Completion;

    public FilterBlock(Func<T, bool> predicate)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));

        // Если предикат истинен — возвращаем массив из одного элемента,
        // иначе — пустой массив (элемент будет проигнорирован дальше по цепочке).
        _innerBlock = new TransformManyBlock<T, T>(item =>
        {
            try
            {
                return Predicate(item) ? new[] { item } : Array.Empty<T>();
            }
            catch
            {
                // Если в предикате выкинуто исключение — просто отбрасываем сообщение.
                // (Альтернатива — пробрасывать ошибку; но фильтр не должен ломать пайплайн.)
                return Array.Empty<T>();
            }
        });
    }

    /// <summary>Отправка элемента в блок.</summary>
    public bool Post(T item) => _innerBlock.Post(item);

    /// <summary>Связать блок-фильтр с приёмником.</summary>
    public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
        => _innerBlock.LinkTo(target, linkOptions);

    /// <summary>Завершить блок (больше сообщений не будет).</summary>
    public void Complete() => _innerBlock.Complete();
}

/// <summary>
/// AggregatorBlock — копит элементы в текущем "окне" и по команде CompleteWindow()
/// отправляет всё накопленное в виде списка вниз по пайплайну.
/// 
/// Можно использовать для группировки данных по интервалам времени:
/// внешний код вызывает CompleteWindow() каждые N секунд.
/// </summary>
public class AggregatorBlock<T>
{
    // Сюда копим элементы текущего окна. Доступ под локом для потокобезопасности.
    private readonly object _lock = new();
    private List<T> _currentWindow = new();

    // Выходной буфер: сюда мы пушим готовые окна.
    private readonly BufferBlock<List<T>> _outputBuffer = new();

    /// <summary>Длительность временного окна (носит информативный характер,
    /// само окно закрывается вызовом CompleteWindow()).</summary>
    public TimeSpan WindowDuration { get; set; }

    /// <summary>Источник, к которому можно подключить следующего получателя.</summary>
    public ISourceBlock<List<T>> Source => _outputBuffer;

    public AggregatorBlock(TimeSpan windowDuration)
    {
        WindowDuration = windowDuration;
    }

    /// <summary>Добавить элемент в текущее окно.</summary>
    public void Add(T item)
    {
        lock (_lock)
        {
            _currentWindow.Add(item);
        }
    }

    /// <summary>Закрыть текущее окно и отправить накопленное вниз по пайплайну.</summary>
    public void CompleteWindow()
    {
        List<T> windowData;
        lock (_lock)
        {
            // Берём текущее окно, а на его место кладём пустое.
            windowData = _currentWindow;
            _currentWindow = new List<T>();
        }

        if (windowData.Count > 0)
            _outputBuffer.Post(windowData);
    }

    /// <summary>Завершить блок — отправляет последнее окно и закрывает выход.</summary>
    public void Complete()
    {
        CompleteWindow();
        _outputBuffer.Complete();
    }

    public Task Completion => _outputBuffer.Completion;
}

/// <summary>
/// CustomBatchBlock — обёртка над встроенным System.Threading.Tasks.Dataflow.BatchBlock.
/// Собирает сообщения в пачки указанного размера и отправляет их дальше как массив.
/// </summary>
public class CustomBatchBlock<T>
{
    // Полное имя, чтобы не путать с нашим классом.
    private readonly System.Threading.Tasks.Dataflow.BatchBlock<T> _innerBlock;

    /// <summary>Размер пачки.</summary>
    public int BatchSize { get; }

    public Task Completion => _innerBlock.Completion;

    public CustomBatchBlock(int batchSize)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Размер пачки должен быть положительным.");

        BatchSize = batchSize;
        _innerBlock = new System.Threading.Tasks.Dataflow.BatchBlock<T>(batchSize);
    }

    /// <summary>Положить элемент в блок (пачка будет отправлена при заполнении).</summary>
    public bool Post(T item) => _innerBlock.Post(item);

    /// <summary>Принудительно отправить текущую (неполную) пачку дальше.</summary>
    public void TriggerBatch() => _innerBlock.TriggerBatch();

    public IDisposable LinkTo(ITargetBlock<T[]> target, DataflowLinkOptions linkOptions)
        => _innerBlock.LinkTo(target, linkOptions);

    public void Complete() => _innerBlock.Complete();
}

/// <summary>
/// ErrorHandlingBlock — блок, который обрабатывает исключения внутри пользовательской
/// операции и не падает целиком, а пропускает "сломанные" сообщения.
/// Также умеет повторять отправку при ошибках (PostWithRetry).
/// </summary>
public class ErrorHandlingBlock<T>
{
    private readonly ActionBlock<T> _innerBlock;

    /// <summary>Обработчик ошибок (вызывается при исключении внутри workItem).</summary>
    public Action<Exception> ErrorHandler { get; }

    public Task Completion => _innerBlock.Completion;

    /// <param name="workItem">Действие, которое нужно выполнить с каждым элементом.</param>
    /// <param name="errorHandler">Что делать, если внутри workItem выкинуто исключение.</param>
    public ErrorHandlingBlock(Action<T> workItem, Action<Exception> errorHandler)
    {
        ErrorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));

        _innerBlock = new ActionBlock<T>(item =>
        {
            try
            {
                workItem(item);
            }
            catch (Exception ex)
            {
                // Любая ошибка ловится и передаётся пользовательскому обработчику.
                // Так пайплайн продолжает работу, а не падает целиком.
                ErrorHandler(ex);
            }
        });
    }

    /// <summary>Простая отправка элемента в блок.</summary>
    public bool Post(T item) => _innerBlock.Post(item);

    /// <summary>
    /// Отправка с повторными попытками. Если Post вернул false
    /// (например, блок переполнен или завершается), пробуем ещё раз.
    /// </summary>
    public bool PostWithRetry(T item, int maxRetries)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (_innerBlock.Post(item))
                return true;

            // Небольшая пауза перед следующей попыткой.
            // Thread.Sleep здесь оправдан — это не "параллельная обработка",
            // а ретрай-цикл; альтернатива — SpinWait.
            Thread.Sleep(10);
        }

        // Если все попытки исчерпаны — отдаём ошибку через ErrorHandler.
        ErrorHandler(new InvalidOperationException(
            $"Не удалось отправить сообщение после {maxRetries} попыток."));
        return false;
    }

    public void Complete() => _innerBlock.Complete();
}