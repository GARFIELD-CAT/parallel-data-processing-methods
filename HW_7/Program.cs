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
        int MainDataSize = 10_000_000;
        int ChainTaskCount = 4;
        int degreeOfParallelism = 4;
        Console.WriteLine("=== Запуск тестов TPL ===\n");

        Console.Write("Генерация тестовых данных... ");
        Stopwatch genSw = Stopwatch.StartNew();
        Random rnd = new Random(RandomSeed);
        decimal[] mainData = GenerateTestData(rnd, MainDataSize);

        int dataSetsCount = 3;
        int dataSetsElemSize = 1_000_000;
        decimal[][] datasets = new decimal[dataSetsCount][];
        for (int d = 0; d < dataSetsCount; d++)
        {
            datasets[d] = GenerateTestData(rnd, dataSetsElemSize);
        }
        genSw.Stop();
        Console.WriteLine($"готово за {genSw.ElapsedMilliseconds} мс\n");

        var processor = new TaskDataProcessor();
        var chainBuilder = new TaskChainBuilder();
        var exceptionDemo = new ExceptionHandlingDemo();
        var benchmark = new TPLBenchmark();

        Console.WriteLine("\n--- Обработка данных с Task.Run ---");
        long taskRunTime = 0;
        decimal taskRunResult = 0;
        bool taskRunCorrectness = false;
        Stopwatch sw = Stopwatch.StartNew();
        decimal[] resultAsync = processor.ProcessDataAsync(mainData).Result;
        sw.Stop();
        taskRunTime = sw.ElapsedMilliseconds;
        taskRunCorrectness = CheckCorrectness(mainData, resultAsync);
        taskRunResult = resultAsync.Sum();
        Console.WriteLine($"  Время: {sw.ElapsedMilliseconds} мс");
        Console.WriteLine($"  Итоговый результат: {taskRunResult}");
        Console.WriteLine($"  Корректность: {(taskRunCorrectness ? "Да" : "Нет")}");

        Console.WriteLine("\n--- Обработка данных с Task.Run и параллелизмом ---");
        decimal taskRunParallelResult = 0;
        sw.Restart();
        decimal[] resultParallel = processor.ProcessDataInParallel(mainData, degreeOfParallelism).Result;
        taskRunParallelResult = resultAsync.Sum();
        sw.Stop();
        Console.WriteLine($"  Степень параллелизма: {degreeOfParallelism}");
        Console.WriteLine($"  Время: {sw.ElapsedMilliseconds} мс");
        Console.WriteLine($"  Итоговый результат: {taskRunParallelResult}");
        Console.WriteLine($"  Корректность:  {(CheckCorrectness(mainData, resultParallel) ? "Да" : "Нет")}");

        Console.WriteLine("\n--- Task.Factory.StartNew ---");
        decimal taskResultFactoryResult = 0;
        sw.Restart();
        decimal[] resultFactory = processor.ProcessDataWithFactory(mainData).Result;
        sw.Stop();
        taskResultFactoryResult = resultFactory.Sum();
        Console.WriteLine($"  Время: {sw.ElapsedMilliseconds} мс");
        Console.WriteLine($"  Итоговый результат: {taskRunParallelResult}");
        Console.WriteLine($"  Корректность:  {(CheckCorrectness(mainData, resultFactory) ? "Да" : "Нет")}");


        Console.WriteLine("\n--- Отмена операций ---");
        bool operationCancelled = false;
        long cancelTime = 0;

        // Тест отмены через токен
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); // Сразу отменяем
            sw.Restart();
            try
            {
                var cancelledTask = processor.ProcessDataWithCancellation(mainData, cts.Token);
                cancelledTask.Wait();
                operationCancelled = false;
            }
            catch (AggregateException ae)
            {
                operationCancelled = ae.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException);
            }
            sw.Stop();
            cancelTime = sw.ElapsedMilliseconds;
        }

        Console.WriteLine($"  Отмена через токен: {(operationCancelled ? "Да" : "Нет")}");
        Console.WriteLine($"  Время до отмены: {cancelTime} мс");

        Console.WriteLine("  Тест отмены через таймаут...");
        sw.Restart();
        bool timeoutCancelled = false;
        try
        {
            decimal[] timeoutResult = processor.ProcessDataWithTimeout(mainData, TimeSpan.FromMilliseconds(10)).Result;
        }
        catch (AggregateException ae)
        {
            timeoutCancelled = ae.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException);
        }
        sw.Stop();
        Console.WriteLine($"  Таймаут: {(timeoutCancelled ? "Сработал" : "Не сработал")}, прошло: {sw.ElapsedMilliseconds} мс");

        Console.WriteLine("\n--- Цепочки задач ---");
        long chainTime = 0;
        bool chainSuccess = false;
        sw.Restart();
        Task<decimal[]> chainTask = chainBuilder.BuildProcessingChain(mainData);
        try
        {
            chainTask.Wait();
            chainSuccess = chainTask.IsCompletedSuccessfully;
        }
        catch (AggregateException)
        {
            chainSuccess = false;
        }
        sw.Stop();
        chainTime = sw.ElapsedMilliseconds;

        Console.WriteLine($"  Основная цепочка: {chainTime} мс, успешно: {(chainSuccess ? "Да" : "Нет")}");

        sw.Restart();
        Task<decimal[]> complexChainTask = chainBuilder.BuildComplexChain(mainData);
        complexChainTask.Wait();
        sw.Stop();
        Console.WriteLine($"  Сложная цепочка (WhenAny): {sw.ElapsedMilliseconds} мс");

        sw.Restart();
        Task<decimal[][]> parallelChainsTask = chainBuilder.BuildParallelChain(datasets);
        parallelChainsTask.Wait();
        sw.Stop();
        Console.WriteLine($"  Параллельные цепочки: {sw.ElapsedMilliseconds} мс");

        decimal[] problematicData = new decimal[1000];
        for (int i = 0; i < problematicData.Length; i++)
        {
            problematicData[i] = i % 2 == 0 ? 1.0m : 0.0m;
        }
        sw.Restart();
        Task<decimal[]> retryTask = chainBuilder.BuildRetryChain(problematicData, maxRetries: 3);
        try
        {
            decimal[] retryResult = retryTask.Result;
            Console.WriteLine($"  Цепочка с повторами: успех за {sw.ElapsedMilliseconds} мс");
        }
        catch (AggregateException)
        {
            Console.WriteLine($"  Цепочка с повторами: исчерпаны попытки за {sw.ElapsedMilliseconds} мс");
        }

        Console.WriteLine("\n--- Обработка исключений ---");
        int exceptionCount = 0;
        bool exceptionsHandledCorrectly = false;

        exceptionCount = 3; // У нас 3 метода, каждый генерирует исключения
        exceptionsHandledCorrectly = TestExceptionHandling(exceptionDemo, mainData);

        Console.WriteLine("\n=== Сравнение производительности ===");
        const int benchOperationCount = 1_000_000;
        const int benchThreadCount = 4;

        var (timeRun, successRun, _) = benchmark.BenchmarkTaskRun(benchOperationCount, benchThreadCount);
        var (timeThread, successThread) = benchmark.BenchmarkThreadAPI(benchOperationCount, benchThreadCount);

        double tplVsThreadSpeedup = (double)timeThread / timeRun;
        double tplOverheadPercent = (double)(timeRun - timeThread) / timeThread * 100;

        Console.WriteLine("\n=== Результаты тестирования TPL ===");

        Console.WriteLine("Обработка данных с Task.Run:");
        Console.WriteLine($"  Количество элементов: {MainDataSize:N0}");
        Console.WriteLine($"  Время выполнения: {taskRunTime} мс");
        Console.WriteLine($"  Итоговый результат: {taskRunResult:F2}");
        Console.WriteLine($"  Корректность: {(taskRunCorrectness ? "Да" : "Нет")}");

        Console.WriteLine("\nОбработка данных с продолжениями:");
        Console.WriteLine($"  Количество задач в цепочке: {ChainTaskCount}");
        Console.WriteLine($"  Время выполнения: {chainTime} мс");
        Console.WriteLine($"  Успешное завершение: {(chainSuccess ? "Да" : "Нет")}");

        Console.WriteLine("\nОбработка исключений:");
        Console.WriteLine($"  Количество исключений: {exceptionCount}");
        Console.WriteLine($"  Обработано корректно: {(exceptionsHandledCorrectly ? "Да" : "Нет")}");

        Console.WriteLine("\nОтмена операций:");
        Console.WriteLine($"  Операция отменена: {(operationCancelled ? "Да" : "Нет")}");
        Console.WriteLine($"  Время до отмены: {cancelTime} мс");

        Console.WriteLine("\nСравнение производительности:");
        Console.WriteLine($"  Task.Run vs Thread API: {tplVsThreadSpeedup:F2}x");
        Console.WriteLine($"  Накладные расходы TPL: {tplOverheadPercent:F1}%\n");

        benchmark.CompareAllApproaches();
    }

    private static decimal[] GenerateTestData(Random rnd, int size)
    {
        decimal[] data = new decimal[size];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = Math.Round((decimal)(rnd.NextDouble() * 100), 2);
        }
        return data;
    }

    private static bool CheckCorrectness(decimal[] input, decimal[] output)
    {
        if (input.Length != output.Length) return false;

        for (int i = 0; i < input.Length; i++)
        {
            decimal expected = ProcessItem(input[i]);
            if (output[i] != expected) return false;
        }
        return true;
    }

    public static decimal ProcessItem(decimal value)
    {
        return (decimal)Math.Round(Math.Sqrt((double)value), 2);
    }

    private static bool TestExceptionHandling(ExceptionHandlingDemo demo, decimal[] data)
    {
        try
        {
            Console.WriteLine("  Тест 1: HandleExceptionsWithWait");
            demo.HandleExceptionsWithWait(data);

            Console.WriteLine("  Тест 2: HandleExceptionsWithWhenAll");
            demo.HandleExceptionsWithWhenAll(data);

            Console.WriteLine("  Тест 3: HandleExceptionsWithContinueWith");
            demo.HandleExceptionsWithContinueWith(data);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
