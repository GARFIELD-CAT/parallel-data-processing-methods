using System;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        const int initialBooks = 1000;
        var rand = new Random(42);

        // Создаём каталог и заполняем начальными книгами
        var catalog = new LibraryCatalog();
        for (int i = 0; i < initialBooks; i++)
        {
            catalog.AddBook($"Title-{i}", $"Author-{rand.Next(0, 200)}");
        }
        Console.WriteLine($"Сгенерировано книг: {initialBooks}");

        // Пул ресурсов (10 ед.)
        using var pool = new ResourcePool(10);

        // Имя для глобального мьютекса (непустое и уникальное для приложения)
        string mutexName = "Global.LibraryCatalog.Mutex.Example";

        // Тестирование ReaderWriterLockSlim
        Console.WriteLine("\n=== Тест ReaderWriterLockSlim ===");
        var rwResult = SynchronizationBenchmark.BenchmarkReaderWriterLock(catalog, 50, 10);
        Console.WriteLine($"Чтение (суммарно): {rwResult.readMs} мс, Запись: {rwResult.writeMs} мс, Общее: {rwResult.totalMs} мс");

        // Проверка целостности — ожидаем минимум initialBooks (писатели могут добавлять)
        var all = catalog.GetAllBooks();
        Console.WriteLine($"Всего книг после конкурентного доступа: {all.Count}");
        Console.WriteLine($"Целостность данных: {(all.Count >= initialBooks ? "Да" : "Нет")}");

        // Тест SemaphoreSlim
        Console.WriteLine("\n=== Тест SemaphoreSlim ===");
        var semResult = SynchronizationBenchmark.BenchmarkSemaphore(pool, 100, 200);
        Console.WriteLine($"Время: {semResult.totalMs} мс, Успешные: {semResult.success}, Неудачные: {semResult.fail}, Доступно сейчас: {pool.AvailableCount}");

        // Тест Mutex (межпроцессный) — запускаем локально несколько операций
        Console.WriteLine("\n=== Тест Mutex (именованный) ===");
        var muResult = SynchronizationBenchmark.BenchmarkMutex(mutexName, 20, 500);
        Console.WriteLine($"Операций: 20, Успешных: {muResult.success}, Время: {muResult.totalMs} мс");

        // Демонстрация таймаутов: TryAddBook и TrySearchBooks
        Console.WriteLine("\n=== Тест таймаутов ReaderWriterLockSlim ===");
        bool addOk = catalog.TryAddBook("TimeoutTitle", "TimeoutAuthor", 50);
        Console.WriteLine($"TryAddBook (50ms): {(addOk ? "Успех" : "Таймаут/Неудача")}");

        var (searchOk, results) = catalog.TrySearchBooks("Title-1", 50);
        Console.WriteLine($"TrySearchBooks (50ms): {(searchOk ? $"Успех, найдено {results.Count}" : "Таймаут/Неудача")}");

        // Сравнение всех
        Console.WriteLine("\n=== Сравнение всех примитивов ===");
        SynchronizationBenchmark.CompareAllPrimitives(catalog, pool, mutexName);

        // Очистка
        catalog.Dispose();
    }
}
