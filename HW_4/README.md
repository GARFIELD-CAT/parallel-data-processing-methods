# Примитивы синхронизации: Mutex, SemaphoreSlim, ReaderWriterLockSlim

Кратко

- Проект демонстрирует использование ReaderWriterLockSlim для каталога библиотечных записей, SemaphoreSlim для пула ограниченных ресурсов и именованного Mutex для межпроцессной синхронизации.
- Реализованы таймауты, корректное освобождение примитивов в finally и замеры производительности через Stopwatch.
- Тесты воспроизводимы: Random с seed = 42.

Файлы проекта

- `Program.cs` — точка входа, генерация данных и запуск тестов.
- `LibraryCatalog.cs` — каталог с методами чтения/записи, таймаутные методы `TryAddBook` и `TrySearchBooks`. Использует `ReaderWriterLockSlim`.
- `ResourcePool.cs` — реализация пула ресурсов на `SemaphoreSlim`. Методы `AcquireResource`, `TryAcquireResource`, `ReleaseResource`.
- `CrossProcessSync.cs` — именованный `Mutex` для межпроцессной синхронизации: `ExecuteWithGlobalLock`, `TryExecuteWithGlobalLock`.
- `SynchronizationBenchmark.cs` — бенчмарки для каждого примитива и сравнение.
