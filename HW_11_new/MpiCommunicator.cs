// Класс для координации обмена сообщениями
public class MpiCommunicator
{
    // Базовый порт
    public const int BasePort = 9000;
    public int NodeId { get; }
    public int TotalNodes { get; }
    // Ранг узла. В нашей реализации равен NodeId
    public int Rank { get { return NodeId; } }
    // Таймаут для всех операций приема
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    private readonly MpiNode _node;
    private readonly CollectiveOperations _сollectiveOperations = new CollectiveOperations();

    public MpiCommunicator(int nodeId, int totalNodes)
    {
        NodeId = nodeId;
        TotalNodes = totalNodes;
        _node = new MpiNode(nodeId, BasePort + nodeId);
    }

    // Запустить сетевой узел и начать слушать порт
    public void Start()
    {
        _node.Start();
    }

    // Остановить узел и закрыть соединения
    public void Stop()
    {
        _node.Stop();
    }

    // Подключиться ко всем остальным узлам кластера
    public async Task ConnectToPeers()
    {
        var tasks = new List<Task>();

        for (int rank = 0; rank < TotalNodes; rank++)
        {
            // К себе подключаться не нужно
            if (rank == NodeId) continue;
            tasks.Add(_node.ConnectToNode(rank, "127.0.0.1", BasePort + rank));
        }

        await Task.WhenAll(tasks);
    }

    // Внутренний метод для отправки сообщения узлу
    internal async Task SendRaw(int destination, object data, string type, int tag)
    {
        await _node.SendObject(destination, data, type, tag);
    }

    // Внутренний метод для приема сообщения от узла
    internal async Task<object> ReceiveRaw(int source, string type, int tag)
    {
        return await _node.ReceiveObject(source, type, tag, Timeout);
    }

    // Отправить сообщение узлу
    public async Task Send(int destination, object message)
    {
        await SendRaw(destination, message, MessageTypeNames.Send, MessageTags.Default);
    }

    // Прием сообщения от узла
    public async Task<object> Receive(int source)
    {
        return await ReceiveRaw(source, MessageTypeNames.Send, MessageTags.Default);
    }

    // Комбинированная операция отправки и приема
    public async Task<object> SendReceive(int destination, object sendMsg, int source)
    {
        var sendTask = Send(destination, sendMsg);
        var receiveTask = Receive(source);
        await Task.WhenAll(sendTask, receiveTask);

        return receiveTask.Result;
    }

    // Рассылка от корневого узла всем (по дереву)
    public async Task<object> Broadcast(object message, int root)
    {
        return await _сollectiveOperations.ImplementBroadcast(this, message, root);
    }

    // Сбор данных от всех узлов к корневому (по кольцу)
    public async Task<object[]> Gather(object localData, int root)
    {
        return await _сollectiveOperations.ImplementGather(this, localData, root);
    }

    // Раздача данных от корневого узла всем
    public async Task<object> Scatter(object[] data, int root)
    {
        return await _сollectiveOperations.ImplementScatter(this, data, root);
    }

    // Сбор данных от всех ко всем (Gather к корню 0 + Broadcast результата)
    public async Task<object[]> AllGather(object localData)
    {
        const int root = 0;
        object[] gathered = await Gather(localData, root);

        GatherAccumulator pack = null;

        if (Rank == root)
        {
            pack = new GatherAccumulator();

            for (int i = 0; i < TotalNodes; i++)
                pack.Add(i, gathered[i]);
        }

        var received = (GatherAccumulator)await Broadcast(pack, root);

        return received.ToOrderedArray(TotalNodes);
    }

    // Редукция данных к корневому узлу (по дереву)
    public async Task<object> Reduce(object localValue, Func<object, object, object> operation, int root)
    {
        return await _сollectiveOperations.ImplementReduce(this, localValue, operation, root);
    }

    // Редукция ко всем узлам (Reduce к корню 0 + Broadcast результата)
    public async Task<object> AllReduce(object localValue, Func<object, object, object> operation)
    {
        const int root = 0;
        object reduced = await Reduce(localValue, operation, root);
        return await Broadcast(reduced, root);
    }

    // Барьерная синхронизация всех узлов
    public async Task Barrier()
    {
        await _сollectiveOperations.ImplementBarrier(this);
    }

    // Счeтчики для бенчмарков
    public long SentMessages
    {
        get { return _node.SentCount; }
    }
    public void ResetCounters()
    {
        _node.ResetSentCount();
    }
}
