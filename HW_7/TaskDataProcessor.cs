using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TplAssignment
{
    /// <summary>
    /// Класс для обработки массивов decimal с использованием TPL.
    /// Все методы возвращают Task<decimal[]> и не используют async/await.
    /// </summary>
    public class TaskDataProcessor
    {
        // Количество частей, на которое разбивается работа по умолчанию
        private const int DefaultPartitions = 4;

        /// <summary>
        /// Асинхронная обработка данных с использованием Task.Run.
        /// Разбивает массив на части и обрабатывает каждую в отдельной задаче.
        /// </summary>
        /// <param name="data">Исходный массив</param>
        /// <returns>Task, возвращающий обработанный массив</returns>
        public Task<decimal[]> ProcessDataAsync(decimal[] data)
        {
            // Разбиваем на фиксированное количество частей
            int partitionCount = DefaultPartitions;
            return ProcessDataInParallel(data, partitionCount);
        }

        /// <summary>
        /// Параллельная обработка с заданной степенью параллелизма.
        /// Создаёт degreeOfParallelism задач, каждая обрабатывает свой сегмент данных.
        /// </summary>
        /// <param name="data">Входные данные</param>
        /// <param name="degreeOfParallelism">Количество одновременно работающих задач</param>
        /// <returns>Task с результатом обработки</returns>
        public Task<decimal[]> ProcessDataInParallel(decimal[] data, int degreeOfParallelism)
        {
            if (degreeOfParallelism <= 0)
                throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism));

            // Вычисляем размер одного сегмента (округляем вверх для последнего)
            int chunkSize = (data.Length + degreeOfParallelism - 1) / degreeOfParallelism;
            var tasks = new Task<decimal[]>[degreeOfParallelism];

            for (int i = 0; i < degreeOfParallelism; i++)
            {
                int start = i * chunkSize;
                int end = Math.Min(start + chunkSize, data.Length);
                // Если сегмент пуст, завершаем задачу с пустым массивом
                if (start >= data.Length)
                {
                    tasks[i] = Task.FromResult(Array.Empty<decimal>());
                    continue;
                }

                // Каждая задача обрабатывает свой фрагмент данных через Task.Run
                tasks[i] = Task.Run(() =>
                {
                    decimal[] chunk = new decimal[end - start];
                    for (int j = start; j < end; j++)
                    {
                        // Применяем простую операцию: умножаем на 1.1 и округляем до 2 знаков
                        chunk[j - start] = Math.Round(data[j] * 1.1m, 2);
                    }
                    return chunk;
                });
            }

            // Ожидаем все задачи и объединяем результаты
            Task<decimal[]> allTask = Task.WhenAll(tasks).ContinueWith(completedAll =>
            {
                // Объединяем все фрагменты в один результирующий массив
                List<decimal> result = new List<decimal>(data.Length);
                foreach (var chunk in completedAll.Result)
                {
                    result.AddRange(chunk);
                }
                return result.ToArray();
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            return allTask;
        }

        /// <summary>
        /// Обработка данных с использованием Task.Factory.StartNew.
        /// Аналог ProcessDataAsync, но демонстрирует фабрику задач.
        /// </summary>
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

                // Используем Task.Factory.StartNew с параметрами по умолчанию
                tasks[i] = Task.Factory.StartNew(() =>
                {
                    decimal[] chunk = new decimal[end - start];
                    for (int j = start; j < end; j++)
                    {
                        chunk[j - start] = Math.Round(data[j] * 1.1m, 2);
                    }
                    return chunk;
                }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            }

            return Task.WhenAll(tasks).ContinueWith(completedAll =>
            {
                List<decimal> result = new List<decimal>(data.Length);
                foreach (var chunk in completedAll.Result)
                    result.AddRange(chunk);
                return result.ToArray();
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>
        /// Обработка данных с поддержкой отмены через CancellationToken.
        /// Периодически проверяет токен и выбрасывает OperationCanceledException при запросе отмены.
        /// </summary>
        public Task<decimal[]> ProcessDataWithCancellation(decimal[] data, CancellationToken cancellationToken)
        {
            // Разбиваем на 4 части, но в каждой задаче в цикле проверяем токен
            int partitionCount = 4;
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

                // Передаём токен в задачу, чтобы при старте задачи он проверялся
                tasks[i] = Task.Factory.StartNew(() =>
                {
                    decimal[] chunk = new decimal[end - start];
                    for (int j = start; j < end; j++)
                    {
                        // Проверка отмены на каждой итерации (можно реже, но для примера наглядно)
                        cancellationToken.ThrowIfCancellationRequested();
                        chunk[j - start] = Math.Round(data[j] * 1.1m, 2);
                    }
                    return chunk;
                }, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default);
            }

            return Task.WhenAll(tasks).ContinueWith(completedAll =>
            {
                // Объединяем только если все задачи успешны
                List<decimal> result = new List<decimal>(data.Length);
                foreach (var chunk in completedAll.Result)
                    result.AddRange(chunk);
                return result.ToArray();
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>
        /// Обработка данных с ограничением по времени.
        /// Создаёт CancellationTokenSource с заданным таймаутом и делегирует основному методу с отменой.
        /// </summary>
        public Task<decimal[]> ProcessDataWithTimeout(decimal[] data, TimeSpan timeout)
        {
            // Создаём CTS, который отменится через timeout
            var cts = new CancellationTokenSource(timeout);
            // Передаём токен и обязательно освобождаем CTS после завершения задачи
            Task<decimal[]> task = ProcessDataWithCancellation(data, cts.Token);
            // После завершения задачи (любым способом) освобождаем ресурсы CTS
            task.ContinueWith(_ => cts.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
            return task;
        }
    }
}