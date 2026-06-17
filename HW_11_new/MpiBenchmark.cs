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

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();

            // Узел 0 шлет -> узел 1 принимает и шлет обратно -> узел 0 принимает.
            var t0 = Task.Run(async () =>
            {
                await _comms[0].Send(1, payload);
                await _comms[0].Receive(1);
            });
            var t1 = Task.Run(async () =>
            {
                object received = await _comms[1].Receive(0);
                await _comms[1].Send(0, received);
            });
            await Task.WhenAll(t0, t1);

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

            await RunAll(async comm =>
            {
                await comm.Broadcast(comm.Rank == 0 ? (object)payload : null, 0);
            });
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

            await RunAll(async comm =>
            {
                // Каждый узел отдает массив int длиной dataSize.
                var local = new int[dataSize];
                for (int k = 0; k < dataSize; k++)
                {
                    local[k] = comm.Rank;
                }
                await comm.Gather(local, 0);
            });
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        // Объем: число узлов * элементов * размер int (4 байта).
        long volume = (long)_comms.Length * dataSize * sizeof(int);
        return (times.Average(), volume);
    }

    // Измеряет время редукции данных (сумма, максимум, минимум)
    public async Task<(double TimeMs, long Result)> BenchmarkReduce(int iterations)
    {
        var times = new List<double>();
        long lastResult = 0;

        for (int i = 0; i < iterations; i++)
        {
            object rootResult = null;
            var sw = Stopwatch.StartNew();

            await RunAll(async comm =>
            {
                object r = await comm.Reduce(comm.Rank, (a, b) => (int)a + (int)b, 0);

                if (comm.Rank == 0)
                {
                    // итог только у корня
                    rootResult = r;
                }
            });
            sw.Stop();

            times.Add(sw.Elapsed.TotalMilliseconds);
            if (rootResult != null)
            {
                lastResult = (int)rootResult;
            }
        }

        return (times.Average(), lastResult);
    }
}
