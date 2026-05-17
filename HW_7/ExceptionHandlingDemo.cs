using System;
using System.Threading.Tasks;


public class ExceptionHandlingDemo
{
    public void HandleExceptionsWithWait(decimal[] data)
    {
        Console.WriteLine("--- HandleExceptionsWithWait ---");
        Task goodTask = Task.Run(() =>
        {
            return new decimal[] { data[0] * 2 };
        });
        Task badTask = Task.Run(() =>
        {
            throw new ArgumentException("Ошибка в задаче");
        });

        try
        {
            // Блокирует поток до получения всех результатов
            Task.WaitAll(goodTask, badTask);
        }
        catch (AggregateException)
        {
            HandleAggregatedExceptions(Task.WhenAll(goodTask, badTask));
        }
    }

    public void HandleExceptionsWithWhenAll(decimal[] data)
    {
        Console.WriteLine("--- HandleExceptionsWithWhenAll ---");
        Task t1 = Task.Run(() =>
        {
            throw new InvalidOperationException("Ошибка в первой задаче");
        });
        Task t2 = Task.Run(() =>
        {
            throw new ArgumentNullException("Ошибка во второй задаче");
        });

        // Не блокирует поток, а возвращает Task.
        Task allTasks = Task.WhenAll(t1, t2);
        try
        {
            allTasks.Wait();
        }
        catch (AggregateException)
        {
            HandleAggregatedExceptions(allTasks);
        }
    }

    public void HandleExceptionsWithContinueWith(decimal[] data)
    {
        Console.WriteLine("--- HandleExceptionsWithContinueWith ---");
        Task faultTask = Task.Run(() =>
        {
            throw new DivideByZeroException("Деление на ноль");
        });

        Task continuationTask = faultTask.ContinueWith(prevTask =>
        {
            Console.WriteLine($"Перехвачено исключение в ContinueWith: {prevTask.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);

        try
        {
            continuationTask.Wait();
        }
        catch (AggregateException)
        {
            Console.WriteLine("Не должно появиться");
            HandleAggregatedExceptions(continuationTask);
        }
    }

    public void HandleAggregatedExceptions(Task task)
    {
        if (task.Exception == null) return;

        // Flatten() превращает дерево AggregateException в плоский список
        var flatExceptions = task.Exception.Flatten();
        flatExceptions.Handle(innerEx =>
        {
            // Здесь мы решаем, какие исключения мы можем обработать
            if (innerEx is ArgumentException || innerEx is InvalidOperationException ||
                innerEx is ArgumentNullException || innerEx is DivideByZeroException)
            {
                Console.WriteLine($"Обработано исключение: {innerEx.GetType().Name}: {innerEx.Message}");
                return true; // исключение обработано
            }
            // Для непредвиденных исключений возвращаем false, что вызовет проброс AggregateException
            return false;
        });
    }
}
