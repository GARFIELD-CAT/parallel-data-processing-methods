using System.Threading;

public class StatisticsTracker
{
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;
    private long _totalProcessingTime;

    public long TotalRequests
    {
        get { return Interlocked.Read(ref _totalRequests); }
    }

    public long SuccessfulRequests
    {
        get { return Interlocked.Read(ref _successfulRequests); }
    }

    public long FailedRequests
    {
        get { return Interlocked.Read(ref _failedRequests); }
    }

    public long TotalProcessingTime
    {
        get { return Interlocked.Read(ref _totalProcessingTime); }
    }

    public void RecordRequest(bool success, long processingTime)
    {
        Interlocked.Increment(ref _totalRequests);

        if (success)
            Interlocked.Increment(ref _successfulRequests);
        else
            Interlocked.Increment(ref _failedRequests);

        // Прибавляем время обработки
        Interlocked.Add(ref _totalProcessingTime, processingTime);
    }

    public double GetSuccessRate()
    {
        long total = TotalRequests;

        if (total == 0)
        {
            return 0.0;
        }

        long success = SuccessfulRequests;

        return (double)success / total * 100.0;
    }

    public double GetAverageProcessingTime()
    {
        long total = TotalRequests;

        if (total == 0)
        {
            return 0.0;
        }

        long totalTime = TotalProcessingTime;

        return (double)totalTime / total;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _successfulRequests, 0);
        Interlocked.Exchange(ref _failedRequests, 0);
        Interlocked.Exchange(ref _totalProcessingTime, 0);
    }
}
