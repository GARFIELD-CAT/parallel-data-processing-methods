using System;
using System.Text;

// Асинхронный логгер
public static class AsyncLogger
{
    private const string LogFile = "results.log";

    // Асинхронная запись с использованием FileStream и FileOptions.Asynchronous
    public static async Task LogAsync(string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message + Environment.NewLine);

        try
        {
            using FileStream fs = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
            await fs.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await fs.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"LogAsync failed and error-file write failed: {ex}");
        }

    }

    // APM-версия логирования: использует BeginWrite/EndWrite и вызывает callback по завершении
    public static void LogWithCallback(string message, Action callback)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message + Environment.NewLine);
        // Открываем поток синхронно, но запись произведём через APM
        var fs = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.Read);
        try
        {
            // Перемещаемся в конец и начинаем APM-запись
            fs.Seek(0, SeekOrigin.End);
            fs.BeginWrite(bytes, 0, bytes.Length, iar =>
            {
                try
                {
                    fs.EndWrite(iar);
                    fs.Flush();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"LogWithCallback EndWrite failed: {ex}");
                }
                finally
                {
                    fs.Dispose();
                    try { callback?.Invoke(); }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"LogWithCallback callback threw: {ex}");
                    }
                }
            }, null);
        }
        catch
        {
            fs.Dispose();
            callback?.Invoke();
        }
    }
}
