using System.Collections.Concurrent;

namespace LibrarySystem;


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

public class TaskQueueManager
{
    private readonly BlockingCollection<TaskItem> _taskQueue;

    public TaskQueueManager(int boundedCapacity)
    {
        _taskQueue = new BlockingCollection<TaskItem>(boundedCapacity);
    }

    public void AddTask(string description, Action taskAction)
    {
        _taskQueue.TryAdd(new TaskItem(description, taskAction));
    }

    public void ProcessTasks(int workerCount)
    {
        using var cts = new CancellationTokenSource();

        var workers = new Task[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            int workerId = i;
            workers[i] = Task.Run(() =>
            {
                foreach (var taskItem in _taskQueue.GetConsumingEnumerable(cts.Token))
                {
                    try
                    {
                        Console.WriteLine($"[Worker {workerId}] Выполняется: {taskItem.Description}");
                        taskItem.Action();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Worker {workerId}] Ошибка в задаче {taskItem.Id}: {ex.Message}");
                    }
                }
            }, cts.Token);
        }

        Task.WaitAll(workers);
    }

    public void CompleteAdding()
    {
        _taskQueue.CompleteAdding();
    }

    public int GetPendingTaskCount()
    {
        return _taskQueue.Count;
    }
}