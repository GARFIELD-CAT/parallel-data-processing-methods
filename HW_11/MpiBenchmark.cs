using System.Diagnostics;


public class MpiBenchmark
{
    private readonly MpiCommunicator[] _comms;

    public MpiBenchmark(MpiCommunicator[] comms)
    {
        _comms = comms;
    }

    // Вспомогательный метод: запускает по одному действию на каждый узел
    // одновременно (модель SPMD) и ждет завершения всех.
    private async Task RunAll(Func<MpiCommunicator, Task> perNode)
    {
        var tasks = new Task[_comms.Length];

        for (int i = 0; i < _comms.Length; i++)
        {
            // локальная копия для замыкания
            MpiCommunicator comm = _comms[i];
            tasks[i] = perNode(comm);
        }
        await Task.WhenAll(tasks);
    }

    // Измеряет время отправки и приема сообщений между двумя узлами
    public async Task<(double Avg, double Min, double Max)> BenchmarkPointToPoint(int iterations, int messageSize)
    {
        var payload = new byte[messageSize];
        new Random(16).NextBytes(payload);
        var times = new List<double>();

        // Узел 0
        async Task Ping()
        {
            await _comms[0].Send(1, payload);
            await _comms[0].Receive(1);
        }
        // Узел 1
        async Task Pong()
        {
            object received = await _comms[1].Receive(0);
            await _comms[1].Send(0, received);
        }

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await Task.WhenAll(Ping(), Pong());
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        return (times.Average(), times.Min(), times.Max());
    }

    // Измеряет время рассылки сообщения от корневого узла всем узлам
    public async Task<(double TimeMs, long Messages)> BenchmarkBroadcast(int iterations, int messageSize)
    {
        var payload = new byte[messageSize];
        new Random(16).NextBytes(payload);

        var times = new List<double>();
        long totalMessages = 0;

        for (int i = 0; i < iterations; i++)
        {
            foreach (var c in _comms)
            {
                c.ResetCounters();
            }

            var sw = Stopwatch.StartNew();
            await RunAll(comm => comm.Broadcast(comm.Rank == 0 ? (object)payload : null, 0));
            sw.Stop();

            times.Add(sw.Elapsed.TotalMilliseconds);

            foreach (var c in _comms)
            {
                totalMessages += c.SentMessages;
            }
        }

        return (times.Average(), totalMessages / iterations);
    }

    // Измеряет время сбора данных от всех узлов к корневому
    public async Task<(double TimeMs, long DataVolumeBytes)> BenchmarkGather(int iterations, int dataSize)
    {
        var times = new List<double>();

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();

            await RunAll(comm =>
            {
                // Каждый узел отдает массив int длиной dataSize.
                var local = new int[dataSize];
                Array.Fill(local, comm.Rank);

                return comm.Gather(local, 0);
            });

            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        // Объем: число узлов * элементов * размер int (4 байта).
        long volume = (long)_comms.Length * dataSize * sizeof(int);
        return (times.Average(), volume);
    }

    // Измеряет время редукции данных (сумма)
    public async Task<(double TimeMs, long Result)> BenchmarkReduce(int iterations)
    {
        var times = new List<double>();
        long lastResult = 0;

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            // Все узлы одновременно сворачивают свой ранг суммой к корню 0
            object[] results = await Task.WhenAll(
                _comms.Select(comm => comm.Reduce(comm.Rank, (a, b) => (int)a + (int)b, 0)));
            sw.Stop();

            times.Add(sw.Elapsed.TotalMilliseconds);
            // итог только у корня (ранг 0)
            lastResult = (int)results[0];
        }

        return (times.Average(), lastResult);
    }
}
