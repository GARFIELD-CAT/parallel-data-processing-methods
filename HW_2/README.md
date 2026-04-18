# Практическое задание 2 — Пул потоков и асинхронные операции

Цель: модернизировать систему обработки данных с использованием ThreadPool и асинхронных шаблонов (TAP и APM).

Файлы:

- Program.cs — точка входа, генерация данных, замеры и вывод результатов.
- TaskProcessor.cs — содержит:
  - ProcessDataWithThreadPool(decimal[] data)
  - ProcessDataAsync(decimal[] data) — TAP (Task-based)
  - BeginProcessData / EndProcessData — APM (IAsyncResult)
- AsyncLogger.cs — логирование:
  - LogAsync(string) — асинхронно через FileStream (FileOptions.Asynchronous)
  - LogWithCallback(string, Action) — APM-версия с callback

Ключевые решения и соответствие требованиям

- Разделение работы:
  - Количество частей вычисляется как min(8, Environment.ProcessorCount) — учитывает требование "на 8 частей" и одновременно избегает жёсткого кодирования потоков при малом числе CPU.
- ThreadPool:
  - Для синхронизации используется CountdownEvent (в TaskProcessor.ProcessDataWithThreadPool).
  - Каждому рабочему назначается неперекрывающийся сегмент результирующего массива, поэтому запись в результат не требует блокировок.
- TAP:
  - Используется Task.Run для фоновых задач и Task.WhenAll + ContinueWith для возврата Task<decimal[]>.
  - Для создания пользовательских задач можно было бы применять TaskCompletionSource; здесь достаточно Task.Run для ясности.
- APM:
  - Собственный класс ProcessAsyncResult реализует IAsyncResult.
  - BeginProcessData ставит работу в ThreadPool и возвращает IAsyncResult; EndProcessData ждёт завершения и возвращает результат.
  - В реализации APM не используется async/await (соответствует запрету).
- Логирование:
  - LogAsync использует FileStream с FileOptions.Asynchronous и WriteAsync.
  - LogWithCallback использует BeginWrite/EndWrite и вызывает callback при завершении.

Запреты и как они соблюдены

- Нельзя создавать Thread напрямую — используется только ThreadPool / Task / FileStream APM.
- Не используется Parallel.For или PLINQ.
- В APM методах нет async/await.
- Количество рабочих частей не жёстко захардкожено (используется Math.Min(8, Environment.ProcessorCount)).
- Разделяемые данные записываются в непересекающиеся сегменты — это безопасно; синхронизация для ожидания завершения достигается через CountdownEvent / ManualResetEvent.
- Нет глобальных переменных для передачи данных между потоками.

Как запускать и тестировать

1. Поместите все файлы в один проект .NET 10 (Console App).
2. dotnet build
3. dotnet run — программа сгенерирует 10,000,000 элементов, выполнит 4 вида обработки, выведет время и запишет лог в results.log.
4. Для тестирования с меньшими объёмами замените в Program.cs константу n на 1000 / 10000 / 100000.

Пояснения (коротко)

- ThreadPool обработка: делим работу на части, каждая часть обрабатывается рабочим из пула, CountdownEvent ждёт завершения всех.
- TAP: каждая часть — Task.Run, WaitAll слияние, возвращается Task<decimal[]> чтобы можно было await (в вызывающем коде).
- APM: класс IAsyncResult хранит состояние и ManualResetEvent; Begin ставит работу в очередь, End ждёт завершения и возвращает результат.
- Логирование демонстрирует оба подхода: TAP-лог и APM-лог.

Отладочная подсказка

- Если результаты не совпадают: проверьте детерминированность функции ProcessItem и границы сегментов. Для отладки запускайте на меньших массивах и сравнивайте значения на краях сегментов.
