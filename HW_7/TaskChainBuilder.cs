using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class TaskChainBuilder
{
    private decimal ProcessItem(decimal value)
    {
        return (decimal)Math.Round(Math.Sqrt((double)value), 2); ;
    }

    public Task<decimal[]> BuildProcessingChain(decimal[] data)
    {
        Task<decimal[]> initialTask = Task.Run(() =>
        {
            decimal[] copy = new decimal[data.Length];
            Array.Copy(data, copy, data.Length);

            return copy;
        });

        Task<decimal[]> step2 = initialTask.ContinueWith(prevTask =>
        {
            decimal[] input = prevTask.Result;

            for (int i = 0; i < input.Length; i++)
            {
                input[i] = ProcessItem(input[i]);
            }
            return input;
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        Task<decimal[]> faultedHandler = initialTask.ContinueWith(prevTask =>
        {
            Console.WriteLine($"Обработчик ошибки: задача упала с исключением {prevTask.Exception?.InnerException?.Message}. Возвращаем исходные данные.");
            return data;
        }, TaskContinuationOptions.OnlyOnFaulted);

        Task<decimal[]> canceledHandler = initialTask.ContinueWith(prevTask =>
        {
            Console.WriteLine("Обработчик отмены: задача была отменена.");
            return Array.Empty<decimal>();
        }, TaskContinuationOptions.OnlyOnCanceled);

        Task<decimal[]> step3 = step2.ContinueWith(prevTask =>
        {
            decimal sum = prevTask.Result.Sum();
            Console.WriteLine($"Цепочка: сумма всех элементов после обработки = {sum}");
            return prevTask.Result;
        }, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously);

        return step3;
    }

    public Task<decimal[]> BuildComplexChain(decimal[] data)
    {
        Task<decimal[]> fastTask = Task.Run(() =>
        {
            decimal[] result = new decimal[data.Length];
            Array.Copy(data, result, data.Length);

            for (int i = 0; i < result.Length; i++)
                result[i] = ProcessItem(result[i]);
            return result;
        });

        Task<decimal[]> slowTask = Task.Run(() =>
        {
            Thread.Sleep(50);
            decimal[] result = new decimal[data.Length];
            Array.Copy(data, result, data.Length);

            return result;
        });

        Task<Task<decimal[]>> whenAny = Task.WhenAny(fastTask, slowTask);

        Task<decimal[]> finalTask = whenAny.ContinueWith(winnerTask =>
        {
            Task<decimal[]> firstCompleted = winnerTask.Result;
            decimal[] intermediate = firstCompleted.Result;

            for (int i = 0; i < intermediate.Length; i++)
                intermediate[i] = ProcessItem(intermediate[i]);
            return intermediate;
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        return finalTask;
    }

    public Task<decimal[][]> BuildParallelChain(decimal[][] datasets)
    {
        var chainTasks = new Task<decimal[]>[datasets.Length];
        for (int i = 0; i < datasets.Length; i++)
        {
            chainTasks[i] = BuildProcessingChain(datasets[i]);
        }

        return Task.WhenAll(chainTasks).ContinueWith(allTask =>
        {
            return allTask.Result;
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    public Task<decimal[]> BuildRetryChain(decimal[] data, int maxRetries)
    {
        Task<decimal[]> currentTask = CreateFaultProneTask(data);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            int currentAttempt = attempt;
            currentTask = currentTask.ContinueWith(prevTask =>
            {
                if (prevTask.IsFaulted)
                {
                    Console.WriteLine($"Повторная попытка #{currentAttempt} после ошибки");

                    return CreateFaultProneTask(data).Result;
                }

                return prevTask.Result;
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        return currentTask;
    }

    // Вспомогательный метод: создаёт задачу, которая может упасть
    private Task<decimal[]> CreateFaultProneTask(decimal[] data)
    {
        return Task.Run(() =>
        {
            decimal[] result = new decimal[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == 0)
                    throw new InvalidOperationException("Обнаружен нулевой элемент");
                result[i] = 100 / data[i];
            }
            return result;
        });
    }
}
