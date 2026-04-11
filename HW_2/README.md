# Практическое задание 2 — Пул потоков и асинхронные операции

Содержимое:

- Program.cs — точка входа, генерирует 10_000_000 элементов и запускает 4 способа обработки (последовательно, ThreadPool, TAP, APM).
- TaskProcessor.cs — реализация ProcessDataWithThreadPool, ProcessDataAsync (TAP), BeginProcessData/EndProcessData (APM).
- AsyncLogger.cs — LogAsync (FileStream с FileOptions.Asynchronous) и LogWithCallback (APM-style через ThreadPool и callback).

Коротко:

- ThreadPool реализация делит данные на 8 частей и использует CountdownEvent для синхронизации.
- TAP использует Task.Run и Task.WhenAll; возвращаем Task<decimal[]>.
- APM реализован через пользовательский IAsyncResult (ProcessDataAsyncResult), использует ThreadPool для выполнения и позволяет вызвать EndProcessData.
- Асинхронный логгер поддерживает TAP и APM-style интерфейс.

Тестирование:

- Параметр размера данных можно изменить в Program.cs (переменная N) для тестов 1k, 10k, 100k.
- Сравнение результатов выполняется с точностью 0.0001.

Замечания:

- Не используются Thread напрямую, Parallel или PLINQ.
- Для крупного N выполнение может занять значительное время и потребовать памяти (~160 MB для массива из 10M decimal).
