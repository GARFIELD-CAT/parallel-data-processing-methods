using System;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        const int initialBooks = 1000;

        // Создаём каталог и заполняем начальными книгами
        var catalog = new LibraryCatalog();

        for (int i = 0; i < initialBooks; i++)
        {
            catalog.AddBook($"Title-{i}", $"Author-{i}");
        }
        Console.WriteLine($"Сгенерировано книг: {initialBooks}");

        // Тестирование ReaderWriterLockSlim
        Console.WriteLine("\n=== Тест ReaderWriterLockSlim ===");
        int readerCount = 100;
        int writerCount = 10;
        var rwResult = SynchronizationBenchmark.BenchmarkReaderWriterLock(catalog, readerCount, writerCount);
        Console.WriteLine($"Чтение (суммарно): {rwResult.readMs} мс, Запись: {rwResult.writeMs} мс, Общее: {rwResult.totalMs} мс");

        // Проверка целостности — ожидаем минимум initialBooks
        var all = catalog.GetAllBooks();
        Console.WriteLine($"Всего книг после конкурентного доступа: {all.Count}");
        Console.WriteLine($"Целостность данных: {(all.Count == initialBooks + writerCount ? "Да" : "Нет")}");

        // Тест SemaphoreSlim
        Console.WriteLine("\n=== Тест SemaphoreSlim ===");
        // Пул ресурсов (10 ед.)
        using var pool = new ResourcePool(10);
        int requestCount = 100;
        int timeoutMs = 200;

        var semResult = SynchronizationBenchmark.BenchmarkSemaphore(pool, requestCount, timeoutMs);
        Console.WriteLine($"Время: {semResult.totalMs} мс, Успешные: {semResult.success}, Неудачные: {semResult.fail}, Доступно сейчас: {pool.AvailableCount}");

        // Тест Mutex (межпроцессный) — запускаем локально несколько операций
        Console.WriteLine("\n=== Тест Mutex (именованный) ===");
        // Имя для глобального мьютекса (непустое и уникальное для приложения)
        string mutexName = "Global.LibraryCatalog.Mutex";
        int count = 20;
        int BenchmarkMutexTimeoutMs = 500;
        var muResult = SynchronizationBenchmark.BenchmarkMutex(mutexName, count, BenchmarkMutexTimeoutMs);
        Console.WriteLine($"Операций: {count}, Успешных: {muResult.success}, Время: {muResult.totalMs} мс");

        // Демонстрация таймаутов: TryAddBook и TrySearchBooks
        Console.WriteLine("\n=== Тест таймаутов ReaderWriterLockSlim ===");
        int TryAddBookTimeoutMs = 50;
        bool addOk = catalog.TryAddBook("TimeoutTitle", "TimeoutAuthor", TryAddBookTimeoutMs);
        Console.WriteLine($"TryAddBook ({TryAddBookTimeoutMs}ms): {(addOk ? "Успех" : "Таймаут/Неудача")}");

        int TrySearchBooksTimeoutMs = 50;
        var (searchOk, results) = catalog.TrySearchBooks("Title-1", TrySearchBooksTimeoutMs);
        Console.WriteLine($"TrySearchBooks ({TrySearchBooksTimeoutMs}ms): {(searchOk ? $"Успех, найдено {results.Count}" : "Таймаут/Неудача")}");

        // Сравнение всех
        SynchronizationBenchmark.CompareAllPrimitives(catalog, pool, mutexName);
        catalog.Dispose();
    }
}
