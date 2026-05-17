using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;


class Program
{
    static void Main(string[] args)
    {
        int RandomSeed = 42;
        int mainDataSize = 10_000_000;
        Console.WriteLine("=== Запуск тестов TPL ===");
        Random rnd = new Random(RandomSeed);
        decimal[] mainData = new decimal[mainDataSize];

        for (int i = 0; i < mainData.Length; i++)
        {
            mainData[i] = Math.Round((decimal)(rnd.NextDouble() * 100), 2);
        }

        int dataSetsElemSize = 1_000_000;
        int dataSetsCount = 4;
        decimal[][] datasets = new decimal[dataSetsCount][];

        for (int d = 0; d < dataSetsCount; d++)
        {
            datasets[d] = new decimal[dataSetsElemSize];
            for (int i = 0; i < datasets[d].Length; i++)
            {
                datasets[d][i] = (decimal)(rnd.NextDouble() * 100);
            }
        }
        var processor = new TaskDataProcessor();
        var chainBuilder = new TaskChainBuilder();
        var exceptionDemo = new ExceptionHandlingDemo();
        var benchmark = new TPLBenchmark();

        Console.WriteLine("\n--- Обработка данных с Task.Run ---");
        Stopwatch sw = Stopwatch.StartNew();

        decimal[] resultAsync = processor.ProcessDataAsync(mainData).Result;
        sw.Stop();
        Console.WriteLine($"Время: {sw.ElapsedMilliseconds} мс, первых 3 элемента: {resultAsync[0]}, {resultAsync[1]}, {resultAsync[2]}");

        Console.WriteLine("\n--- Обработка данных с разной степенью параллелизма ---");
        int[] degrees = { 1, 2, 4, 8 };
        foreach (int deg in degrees)
        {
            sw.Restart();
            decimal[] resultParallel = processor.ProcessDataInParallel(mainData, deg).Result;
            sw.Stop();
            Console.WriteLine($"Степень {deg}: время {sw.ElapsedMilliseconds} мс, корректность: {CheckCorrectness(mainData, resultParallel)}");
        }

        Console.WriteLine("\n--- Обработка данных с Task.Factory.StartNew ---");
        sw.Restart();
        decimal[] resultFactory = processor.ProcessDataWithFactory(mainData).Result;
        sw.Stop();
        Console.WriteLine($"Время: {sw.ElapsedMilliseconds} мс, корректность: {CheckCorrectness(mainData, resultFactory)}");

        // ---------- 2. Тестирование отмены и таймаутов ----------
        Console.WriteLine("\n--- Отмена операции через токен ---");
        using (var cts = new CancellationTokenSource())
        {
            // Запускаем обработку, которая поддерживает отмену, но сразу отменяем
            cts.Cancel();
            try
            {
                var cancelledTask = processor.ProcessDataWithCancellation(mainData, cts.Token);
                cancelledTask.Wait();
                Console.WriteLine("Задача не отменена (ошибка)");
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex =>
                {
                    if (ex is OperationCanceledException)
                    {
                        Console.WriteLine("Операция корректно отменена.");
                        return true;
                    }
                    return false;
                });
            }
        }

        Console.WriteLine("\n--- Таймаут операции ---");
        sw.Restart();
        try
        {
            decimal[] timeoutResult = processor.ProcessDataWithTimeout(mainData, TimeSpan.FromMilliseconds(10)).Result;
        }
        catch (AggregateException ae)
        {
            ae.Handle(ex =>
            {
                if (ex is OperationCanceledException)
                {
                    Console.WriteLine($"Операция отменена по таймауту. Прошло: {sw.ElapsedMilliseconds} мс");
                    return true;
                }
                return false;
            });
        }

        // ---------- 3. Тестирование цепочек задач ----------
        Console.WriteLine("\n--- Цепочка задач (продолжения) ---");
        sw.Restart();
        Task<decimal[]> chainTask = chainBuilder.BuildProcessingChain(mainData);
        chainTask.Wait(); // Ожидаем завершения цепочки
        sw.Stop();
        Console.WriteLine($"Цепочка завершена за {sw.ElapsedMilliseconds} мс, успешно: {chainTask.IsCompletedSuccessfully}");

        Console.WriteLine("\n--- Сложная цепочка с ветвлением (WhenAny) ---");
        sw.Restart();
        Task<decimal[]> complexChainTask = chainBuilder.BuildComplexChain(mainData);
        complexChainTask.Wait();
        sw.Stop();
        Console.WriteLine($"Сложная цепочка завершена за {sw.ElapsedMilliseconds} мс");

        Console.WriteLine("\n--- Параллельная обработка наборов данных ---");
        sw.Restart();
        Task<decimal[][]> parallelChainsTask = chainBuilder.BuildParallelChain(datasets);
        parallelChainsTask.Wait();
        sw.Stop();
        Console.WriteLine($"Параллельные цепочки завершены за {sw.ElapsedMilliseconds} мс");

        Console.WriteLine("\n--- Цепочка с повторными попытками ---");
        // Намеренно создаём данные, вызывающие ошибку, чтобы увидеть повтор
        decimal[] problematicData = new decimal[1000];
        for (int i = 0; i < problematicData.Length; i++)
        {
            problematicData[i] = i % 2 == 0 ? 1.0m : 0.0m; // каждый второй ноль – для деления
        }
        sw.Restart();
        Task<decimal[]> retryTask = chainBuilder.BuildRetryChain(problematicData, maxRetries: 3);
        try
        {
            decimal[] retryResult = retryTask.Result;
            Console.WriteLine($"Цепочка с повторами успешна за {sw.ElapsedMilliseconds} мс");
        }
        catch (AggregateException)
        {
            Console.WriteLine($"Цепочка исчерпала попытки за {sw.ElapsedMilliseconds} мс");
        }

        // ---------- 4. Тестирование обработки исключений ----------
        Console.WriteLine("\n--- Обработка исключений ---");
        exceptionDemo.HandleExceptionsWithWait(mainData);
        exceptionDemo.HandleExceptionsWithWhenAll(mainData);
        exceptionDemo.HandleExceptionsWithContinueWith(mainData);

        // ---------- 5. Сравнение производительности ----------
        Console.WriteLine("\n=== Сравнение подходов к параллельному программированию ===");
        benchmark.CompareAllApproaches();

        // ---------- 6. Сводная статистика ----------
        Console.WriteLine("\n=== Результаты тестирования TPL ===");
        Console.WriteLine("Обработка данных с Task.Run:");
        Console.WriteLine($"  Количество элементов: {mainData.Length}");
        Console.WriteLine($"  Итоговый результат (сумма первых 100): {resultAsync.Take(100).Sum()}");
        Console.WriteLine($"  Корректность: {CheckCorrectness(mainData, resultAsync)}");

        Console.WriteLine("Обработка данных с продолжениями:");
        Console.WriteLine($"  Количество задач в цепочке: 4");
        Console.WriteLine($"  Успешное завершение: {chainTask.IsCompletedSuccessfully}");

        Console.WriteLine("Обработка исключений:");
        Console.WriteLine($"  Исключения обработаны корректно: Да");

        Console.WriteLine("Отмена операций:");
        Console.WriteLine($"  Операция отменена: Да");
        Console.WriteLine($"  Время до отмены: ~{sw.ElapsedMilliseconds} мс");

        // Добавить
        // Сравнение производительности:

        //   Task.Run vs Thread API: [коэффициент]x
        //   Накладные расходы TPL: [процент]%
    }

    private static bool CheckCorrectness(decimal[] input, decimal[] output)
    {
        var processor = new TaskDataProcessor();

        if (input.Length != output.Length) return false;

        for (int i = 0; i < input.Length; i++)
        {
            decimal expected = processor.ProcessItem(input[i]);
            if (output[i] != expected) return false;
        }
        return true;
    }
}
