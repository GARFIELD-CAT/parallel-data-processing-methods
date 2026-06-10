using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;


// Блок фильтрации данных по предикату
public class FilterBlock<T>
{
    private readonly TransformManyBlock<T, T> _innerBlock;

    // Функция-предикат: true — пропустить, false — отбросить
    public Func<T, bool> Predicate { get; }

    public Task Completion
    {
        get { return _innerBlock.Completion; }
    }


    public FilterBlock(Func<T, bool> predicate)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));

        _innerBlock = new TransformManyBlock<T, T>(item =>
        {
            try
            {
                return Predicate(item) ? new[] { item } : Array.Empty<T>();
            }
            catch
            {
                Console.WriteLine("Got error in FilterBlock");
                return Array.Empty<T>();
            }
        });
    }

    public bool Post(T item)
    {
        return _innerBlock.Post(item);
    }

    public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
    {
        return _innerBlock.LinkTo(target, linkOptions);
    }

    public void Complete()
    {
        _innerBlock.Complete();
    }
}

// Блок агрегации данных по временным окнам
public class AggregatorBlock<T>
{
    private readonly object _lock = new();
    private List<T> _currentWindow = new();

    private readonly BufferBlock<List<T>> _outputBuffer = new();

    public TimeSpan WindowDuration { get; set; }

    public ISourceBlock<List<T>> Source
    {
        get { return _outputBuffer; }
    }

    public AggregatorBlock(TimeSpan windowDuration)
    {
        WindowDuration = windowDuration;
    }

    // Добавить элемент в текущее окно
    public void Add(T item)
    {
        lock (_lock)
        {
            _currentWindow.Add(item);
        }
    }

    // Закрыть текущее окно и отправить накопленное вниз по пайплайну
    public void CompleteWindow()
    {
        List<T> windowData;
        lock (_lock)
        {
            windowData = _currentWindow;
            _currentWindow = new List<T>();
        }

        if (windowData.Count > 0)
            _outputBuffer.Post(windowData);
    }

    // Завершить блок. Отправляет последнее окно и закрывает выход
    public void Complete()
    {
        CompleteWindow();
        _outputBuffer.Complete();
    }

    public Task Completion
    {
        get { return _outputBuffer.Completion; }
    }
}

// Блок группировки данных в пакеты
public class BatchBlock<T>
{
    private readonly System.Threading.Tasks.Dataflow.BatchBlock<T> _innerBlock;

    public int BatchSize { get; }

    public Task Completion
    {
        get { return _innerBlock.Completion; }
    }

    public BatchBlock(int batchSize)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Размер пачки должен быть положительным.");

        BatchSize = batchSize;
        _innerBlock = new System.Threading.Tasks.Dataflow.BatchBlock<T>(batchSize);
    }

    // Положить элемент в блок
    public bool Post(T item)
    {
        return _innerBlock.Post(item);
    }

    // Принудительно отправка пакета дальше
    public void TriggerBatch()
    {
        _innerBlock.TriggerBatch();
    }

    public IDisposable LinkTo(ITargetBlock<T[]> target, DataflowLinkOptions linkOptions)
    {
        return _innerBlock.LinkTo(target, linkOptions);
    }

    public void Complete()
    {
        _innerBlock.Complete();
    }
}

// Блок с обработкой исключений
public class ErrorHandlingBlock<T>
{
    private readonly ActionBlock<T> _innerBlock;

    // Обработчик ошибок
    public Action<Exception> ErrorHandler { get; }

    public Task Completion
    {
        get { return _innerBlock.Completion; }
    }

    // workItem" - Действие, которое нужно выполнить с каждым элементом
    // errorHandler - Что делать, если внутри workItem выкинуто исключение
    public ErrorHandlingBlock(Action<T> workItem, Action<Exception> errorHandler)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ErrorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));

        _innerBlock = new ActionBlock<T>(item =>
        {
            try
            {
                workItem(item);
            }
            catch (Exception ex)
            {
                // Любая ошибка ловится и передаётся пользовательскому обработчику
                ErrorHandler(ex);
            }
        });
    }

    // Простая отправка элемента в блок
    public bool Post(T item)
    {
        return _innerBlock.Post(item);
    }

    // Отправка с повторными попытками. Если Post вернул false
    public bool PostWithRetry(T item, int maxRetries)
    {
        var spinner = new SpinWait();

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (_innerBlock.Post(item))
                return true;

            // Короткая пауза без блокировки потока
            spinner.SpinOnce();
        }

        // Все попытки исчерпаны — отдаём ошибку через ErrorHandler.
        ErrorHandler(new InvalidOperationException(
            $"Не удалось отправить сообщение после {maxRetries} попыток.")
        );

        return false;
    }

    public void Complete()
    {
        _innerBlock.Complete();
    }
}
