using System.Collections.Concurrent;


public class TaskItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Description { get; set; }
    public Action Action { get; set; }
    public DateTime CreatedAt { get; } = DateTime.Now;

    public TaskItem(string description, Action action)
    {
        Description = description;
        Action = action;
    }
}

public class TaskQueueManager : IDisposable
{
    private readonly BlockingCollection<TaskItem> _taskQueue;
    private readonly CancellationTokenSource _cts = new();

    public TaskQueueManager(int boundedCapacity)
    {
        _taskQueue = new BlockingCollection<TaskItem>(boundedCapacity);
    }

    public void AddTask(string description, Action taskAction)
    {
        _taskQueue.Add(new TaskItem(description, taskAction), _cts.Token);
    }

    public Task ProcessTasks(int workerCount)
    {
        var workers = new Task[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            int workerId = i;
            workers[i] = Task.Run(() =>
            {
                foreach (var taskItem in _taskQueue.GetConsumingEnumerable(_cts.Token))
                {
                    try
                    {
                        taskItem.Action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Worker {workerId}] Ошибка в задаче {taskItem.Id}: {ex.Message}");
                    }
                }
            }, _cts.Token);
        }

        return Task.WhenAll(workers);
    }

    public void CompleteAdding()
    {
        _taskQueue.CompleteAdding();
    }

    public int GetPendingTaskCount()
    {
        return _taskQueue.Count;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _taskQueue.Dispose();
    }
}