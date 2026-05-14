using System;
using System.Threading.Tasks;

namespace TplAssignment
{
    /// <summary>
    /// Демонстрация обработки исключений в задачах.
    /// </summary>
    public class ExceptionHandlingDemo
    {
        /// <summary>
        /// Обработка исключений через ожидание Task.Wait().
        /// </summary>
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
                Task.WaitAll(goodTask, badTask);
            }
            catch (AggregateException)
            {
                // Передаем задачу, объединяющую обе, в универсальный обработчик
                HandleAggregatedExceptions(Task.WhenAll(goodTask, badTask));
            }
        }

        /// <summary>
        /// Обработка исключений через Task.WhenAll.
        /// </summary>
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

            // Task.WhenAll для задач типа Task<decimal[]> возвращает Task<decimal[][]>
            // Сохраняем как базовый Task для передачи в обработчик
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

        /// <summary>
        /// Обработка исключений с помощью ContinueWith (без выбрасывания исключений наружу).
        /// </summary>
        public void HandleExceptionsWithContinueWith(decimal[] data)
        {
            Console.WriteLine("--- HandleExceptionsWithContinueWith ---");
            Task faultTask = Task.Run(() =>
            {
                throw new DivideByZeroException("Деление на ноль");
            });

            // Прикрепляем продолжение только на случай ошибки
            // ContinueWith возвращает Task, но нам не нужно сохранять его
            faultTask.ContinueWith(t =>
            {
                // t.Exception содержит AggregateException
                Console.WriteLine($"Перехвачено исключение в ContinueWith: {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);

            // Ожидаем завершения основной задачи; исключение не будет проброшено повторно,
            // так как оно обработано в продолжении
            try
            {
                faultTask.Wait();
            }
            catch (AggregateException)
            {
                // Сюда не попадём, потому что продолжение "проглотило" ошибку
                Console.WriteLine("Не должно появиться");
            }
        }

        /// <summary>
        /// Универсальный метод обработки AggregateException: разворачивает вложенные исключения
        /// и обрабатывает известные типы.
        /// </summary>
        /// <param name="task">Задача, в которой могут быть исключения</param>
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
}