using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


public static class AsyncLogger
{
    private static readonly string LogFilePath = "process.log";
    private static readonly object _fileLock = new object();

    // Асинхронное логирование через FileStream с FileOptions.Asynchronous
    public static async Task LogAsync(string message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        byte[] data = Encoding.UTF8.GetBytes($"{DateTime.Now:O} {message}{Environment.NewLine}");

        // Открываем файл в режиме append
        using (var fs = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous))
        {
            await fs.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            await fs.FlushAsync().ConfigureAwait(false);
        }
    }

    // Логирование с APM (Begin/End) и callback при завершении
    // Мы реализуем BeginWrite/EndWrite на FileStream (классический APM) через BeginWrite/EndWrite.
    // Для совместимости с .NET версии, где BeginWrite может быть отсутствующим, используем ThreadPool.
    public static IAsyncResult BeginLogWithApm(string message, AsyncCallback? callback, object? state)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        byte[] data = Encoding.UTF8.GetBytes($"{DateTime.Now:O} {message}{Environment.NewLine}");

        var tcs = new TaskCompletionSource<object?>(state);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                // Синхронная запись, выполняемая в пуле потоков (APM-стиль)
                lock (_fileLock)
                {
                    using (var fs = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        fs.Write(data, 0, data.Length);
                        fs.Flush();
                    }
                }

                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            finally
            {
                callback?.Invoke(tcs.Task); // Используем Task как IAsyncResult совместимый объект
            }
        });

        return tcs.Task;
    }

    public static void EndLogWithApm(IAsyncResult asyncResult)
    {
        if (asyncResult == null) throw new ArgumentNullException(nameof(asyncResult));
        var task = asyncResult as Task;
        if (task == null) throw new ArgumentException("Invalid IAsyncResult", nameof(asyncResult));
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch
        {
            throw;
        }
    }

    // Метод LogWithCallback: использует APM BeginLogWithApm и вызывает callback после завершения
    public static void LogWithCallback(string message, Action callback)
    {
        BeginLogWithApm(message, ar =>
        {
            try
            {
                EndLogWithApm(ar);
            }
            catch
            {
                // Игнорируем ошибки логирования здесь (можно расширить)
            }
            finally
            {
                callback?.Invoke();
            }
        }, null);
    }
}
