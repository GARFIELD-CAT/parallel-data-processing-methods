using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TplAssignment
{
    /// <summary>
    /// Класс для построения цепочек задач с использованием продолжений (ContinueWith).
    /// Демонстрирует различные варианты TaskContinuationOptions.
    /// </summary>
    public class TaskChainBuilder
    {
        /// <summary>
        /// Построение цепочки из 3-4 задач с продолжениями.
        /// Использует OnlyOnRanToCompletion, OnlyOnFaulted, OnlyOnCanceled, ExecuteSynchronously.
        /// Возвращает задачу, которая после успешного выполнения вернёт обработанный массив.
        /// </summary>
        /// <param name="data">Исходные данные</param>
        public Task<decimal[]> BuildProcessingChain(decimal[] data)
        {
            // Шаг 1: начальная задача – просто копирует данные во избежание изменения исходного массива
            Task<decimal[]> initialTask = Task.Run(() =>
            {
                // Для демонстрации отмены можно было бы использовать CancellationToken,
                // но здесь мы гарантируем успешное завершение.
                decimal[] copy = new decimal[data.Length];
                Array.Copy(data, copy, data.Length);
                return copy;
            });

            // Продолжение, выполняемое ТОЛЬКО при успешном завершении предыдущей задачи
            Task<decimal[]> step2 = initialTask.ContinueWith(prevTask =>
            {
                // prevTask.Result гарантированно не вызовет исключения
                decimal[] input = prevTask.Result;
                // Увеличиваем все элементы на 10%
                for (int i = 0; i < input.Length; i++)
                {
                    input[i] = Math.Round(input[i] * 1.1m, 2);
                }
                return input;
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            // Продолжение только при ошибке: логируем и возвращаем исходные данные (для демонстрации)
            Task<decimal[]> faultedHandler = initialTask.ContinueWith(prevTask =>
            {
                Console.WriteLine("Обработчик ошибки: задача упала с исключением. Возвращаем исходные данные.");
                // prevTask.Exception содержит информацию об ошибке
                return data;
            }, TaskContinuationOptions.OnlyOnFaulted);

            // Продолжение только при отмене: также логируем
            Task<decimal[]> canceledHandler = initialTask.ContinueWith(prevTask =>
            {
                Console.WriteLine("Обработчик отмены: задача была отменена.");
                return Array.Empty<decimal>();
            }, TaskContinuationOptions.OnlyOnCanceled);

            // Шаг 3: после успешного выполнения второго шага, например, вычисляем среднее (но мы вернём массив)
            Task<decimal[]> step3 = step2.ContinueWith(prevTask =>
            {
                // Здесь можно добавить дополнительную обработку, например, вычислить сумму
                decimal sum = prevTask.Result.Sum();
                Console.WriteLine($"Цепочка: сумма всех элементов после обработки = {sum}");
                return prevTask.Result;
            }, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously);

            // Возвращаем последнее звено, представляющее результат цепочки при успехе.
            // Если начальная задача упала или отменена, вернётся задача faultedHandler или canceledHandler,
            // но для клиента важно только успешное завершение, поэтому возвращаем step3.
            // При сбоях step3 не выполнится и задача останется в состоянии Faulted/Canceled.
            return step3;
        }

        /// <summary>
        /// Сложная цепочка с ветвлением: запускает две конкурирующие обработки и продолжает с результатом первой завершившейся.
        /// </summary>
        public Task<decimal[]> BuildComplexChain(decimal[] data)
        {
            // Две параллельные задачи: одна обрабатывает быстрее, другая медленнее (для демонстрации)
            Task<decimal[]> fastTask = Task.Run(() =>
            {
                // Быстрая обработка: просто копируем и немного меняем
                decimal[] result = new decimal[data.Length];
                Array.Copy(data, result, data.Length);
                for (int i = 0; i < result.Length; i++)
                    result[i] = Math.Round(result[i] * 0.9m, 2); // уменьшаем на 10%
                return result;
            });

            Task<decimal[]> slowTask = Task.Run(() =>
            {
                // Имитация длительной обработки (например, другая логика)
                Thread.Sleep(50); // Задержка для гарантии, что fastTask завершится первой
                decimal[] result = new decimal[data.Length];
                Array.Copy(data, result, data.Length);
                return result;
            });

            // Используем Task.WhenAny, чтобы получить результат первой завершившейся
            Task<Task<decimal[]>> whenAny = Task.WhenAny(fastTask, slowTask);

            // После получения победителя выполняем окончательную обработку
            Task<decimal[]> finalTask = whenAny.ContinueWith(winnerTask =>
            {
                Task<decimal[]> firstCompleted = winnerTask.Result; // первая завершённая задача
                decimal[] intermediate = firstCompleted.Result;
                // Дополнительная операция
                for (int i = 0; i < intermediate.Length; i++)
                    intermediate[i] = Math.Round(intermediate[i] + 1.0m, 2);
                return intermediate;
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            return finalTask;
        }

        /// <summary>
        /// Параллельная обработка нескольких наборов данных.
        /// Создаёт для каждого набора отдельную цепочку и выполняет их параллельно.
        /// </summary>
        /// <param name="datasets">Массив массивов данных</param>
        /// <returns>Задача, возвращающая массив обработанных массивов</returns>
        public Task<decimal[][]> BuildParallelChain(decimal[][] datasets)
        {
            var chainTasks = new Task<decimal[]>[datasets.Length];
            for (int i = 0; i < datasets.Length; i++)
            {
                // Запускаем отдельную цепочку для каждого набора данных
                chainTasks[i] = BuildProcessingChain(datasets[i]);
            }
            // Ожидаем завершения всех цепочек
            return Task.WhenAll(chainTasks).ContinueWith(allTask =>
            {
                return allTask.Result; // Task<decimal[][]>
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>
        /// Цепочка с повторными попытками при ошибках.
        /// Если задача завершается с ошибкой, запускает её заново, пока не будет успеха или не исчерпаются попытки.
        /// </summary>
        /// <param name="data">Входные данные</param>
        /// <param name="maxRetries">Максимальное количество повторов</param>
        public Task<decimal[]> BuildRetryChain(decimal[] data, int maxRetries)
        {
            // Начальная задача – обработка данных, которая может упасть
            Task<decimal[]> currentTask = CreateFaultProneTask(data);

            // Рекурсивное продолжение: если упало и есть попытки, создаём новую задачу
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                int currentAttempt = attempt; // захват для замыкания
                // Если задача упала, запускаем новую (только на время этой попытки)
                currentTask = currentTask.ContinueWith(prevTask =>
                {
                    if (prevTask.IsFaulted)
                    {
                        Console.WriteLine($"Повторная попытка #{currentAttempt} после ошибки");
                        // Создаём новую задачу, которая повторяет опасную операцию
                        return CreateFaultProneTask(data).Result; // синхронно дожидаемся в продолжении (т.к. мы уже в задаче)
                    }
                    // Если задача не упала (успех или отмена), возвращаем её результат
                    return prevTask.Result;
                }, TaskContinuationOptions.ExecuteSynchronously);
            }

            return currentTask;
        }

        // Вспомогательный метод: создаёт задачу, которая может упасть (например, деление на ноль)
        private Task<decimal[]> CreateFaultProneTask(decimal[] data)
        {
            return Task.Run(() =>
            {
                decimal[] result = new decimal[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    // Искусственная ошибка: если элемент равен 0, выбрасываем исключение
                    if (data[i] == 0)
                        throw new InvalidOperationException("Обнаружен нулевой элемент");
                    result[i] = 100 / data[i]; // деление, которое может упасть
                }
                return result;
            });
        }
    }
}