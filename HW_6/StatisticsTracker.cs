using System.Threading;

namespace AtomicOperationsDemo
{
    /// <summary>
    /// Сборщик статистики запросов, использующий только атомарные операции.
    /// Все счётчики обновляются без блокировок.
    /// </summary>
    public class StatisticsTracker
    {
        // Общее количество запросов
        private long _totalRequests;
        // Количество успешных запросов
        private long _successfulRequests;
        // Количество неудачных запросов
        private long _failedRequests;
        // Общее время обработки в миллисекундах
        private long _totalProcessingTime;

        /// <summary>
        /// Общее количество запросов (атомарное чтение).
        /// </summary>
        public long TotalRequests => Interlocked.Read(ref _totalRequests);

        /// <summary>
        /// Количество успешных запросов.
        /// </summary>
        public long SuccessfulRequests => Interlocked.Read(ref _successfulRequests);

        /// <summary>
        /// Количество неудачных запросов.
        /// </summary>
        public long FailedRequests => Interlocked.Read(ref _failedRequests);

        /// <summary>
        /// Общее время обработки всех запросов (мс).
        /// </summary>
        public long TotalProcessingTime => Interlocked.Read(ref _totalProcessingTime);

        /// <summary>
        /// Регистрирует один запрос с указанием успешности и времени обработки.
        /// </summary>
        /// <param name="success">Успешно ли обработан запрос.</param>
        /// <param name="processingTime">Время обработки в миллисекундах.</param>
        public void RecordRequest(bool success, long processingTime)
        {
            // Атомарно увеличиваем счётчики запросов
            Interlocked.Increment(ref _totalRequests);

            if (success)
                Interlocked.Increment(ref _successfulRequests);
            else
                Interlocked.Increment(ref _failedRequests);

            // Прибавляем время обработки
            Interlocked.Add(ref _totalProcessingTime, processingTime);
        }

        /// <summary>
        /// Возвращает долю успешных запросов в процентах (0.0 – 100.0).
        /// </summary>
        public double GetSuccessRate()
        {
            long total = Interlocked.Read(ref _totalRequests);
            if (total == 0) return 0.0;
            long success = Interlocked.Read(ref _successfulRequests);
            return (double)success / total * 100.0;
        }

        /// <summary>
        /// Возвращает среднее время обработки запроса в миллисекундах.
        /// </summary>
        public double GetAverageProcessingTime()
        {
            long total = Interlocked.Read(ref _totalRequests);
            if (total == 0) return 0.0;
            long totalTime = Interlocked.Read(ref _totalProcessingTime);
            return (double)totalTime / total;
        }

        /// <summary>
        /// Сбрасывает все счётчики в ноль.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _totalRequests, 0);
            Interlocked.Exchange(ref _successfulRequests, 0);
            Interlocked.Exchange(ref _failedRequests, 0);
            Interlocked.Exchange(ref _totalProcessingTime, 0);
        }
    }
}