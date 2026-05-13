using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TaskItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Description { get; set; }
    public Action Action { get; set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public TaskItem(string description, Action action)
    {
        Description = description;
        Action = action;
    }
}

public class TaskQueueManager : IDisposable
{
    public BlockingCollection<TaskItem> TaskQueue { get; }

    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly List<Task> _workers = new List<Task>();

    public TaskQueueManager(int boundedCapacity = 1000)
    {
        TaskQueue = new BlockingCollection<TaskItem>(boundedCapacity);
    }

    public bool AddTask(string description, Action action)
    {
        var item = new TaskItem(description, action);

        try
        {
            return TaskQueue.TryAdd(item);
        }
        catch
        {
            return false;
        }
    }

    public Task ProcessTasks(int workerCount)
    {
        _workers.Clear();

        for (int i = 0; i < workerCount; i++)
        {
            var worker = Task.Run(() =>
            {
                foreach (var taskItem in TaskQueue.GetConsumingEnumerable(_cts.Token))
                {
                    try
                    {
                        taskItem.Action?.Invoke();
                    }
                    catch (OperationCanceledException)
                    {
                        // Корректный выход при отмене токена – ничего не делаем
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка обработки задачи: {ex.Message}");
                    }
                }
            }, _cts.Token);

            _workers.Add(worker);
        }

        return Task.WhenAll(_workers);
    }

    public void CompleteAdding()
    {
        TaskQueue.CompleteAdding();
    }


    public int GetPendingTaskCount()
    {
        return TaskQueue.Count;
    }


    public void StopProcessing()
    {
        _cts.Cancel();
    }

    public void Dispose()
    {
        StopProcessing();
        TaskQueue.Dispose();
        _cts.Dispose();
    }
}
