// Набор алгоритмов коллективных операций. Методы получают коммуникатор и через
// его внутренние SendRaw/ReceiveRaw общаются с другими узлами.
public class CollectiveOperations
{
    //  BROADCAST (рассылка по дереву)
    //  Корень -> всем. Чтобы не слать N-1 сообщений последовательно, используем
    //  двоичное дерево: каждый узел получает данные от родителя и пересылает
    //  максимум двум детям. Глубина дерева ~ log2(N).
    public async Task<object> ImplementBroadcast(MpiCommunicator comm, object message, int root)
    {
        int n = comm.TotalNodes;
        int me = comm.Rank;

        // Виртуальный ранг: сдвигаем нумерацию так, чтобы корень стал нулем.
        // Тогда дерево всегда строится одинаково: у узла v дети 2v+1 и 2v+2.
        int v = (me - root + n) % n;

        object data;
        if (v == 0)
        {
            // у корня данные уже есть
            data = message;
        }
        else
        {
            // Родитель в дереве по виртуальному рангу
            int parentV = (v - 1) / 2;
            int parentReal = (parentV + root) % n;
            data = await comm.ReceiveRaw(parentReal, MessageTypeNames.Broadcast, MessageTags.Broadcast);
        }

        // Пересылаем данные детям
        foreach (int childV in new[] { 2 * v + 1, 2 * v + 2 })
        {
            if (childV < n)
            {
                int childReal = (childV + root) % n;
                await comm.SendRaw(childReal, data, MessageTypeNames.Broadcast, MessageTags.Broadcast);
            }
        }

        return data;
    }

    //  GATHER (сбор по кольцу)
    //  Все -> корню. Накопитель (GatherAccumulator) обходит кольцо
    //  (root+1) -> (root+2) -> ... -> root, по пути каждый узел добавляет
    //  свое значение. В конце корень собирает упорядоченный массив.
    public async Task<object[]> ImplementGather(MpiCommunicator comm, object localData, int root)
    {
        int n = comm.TotalNodes;
        int me = comm.Rank;

        if (n == 1)
        {
            // Один узел — сразу готовый результат.
            var single = new GatherAccumulator();
            single.Add(root, localData);
            return single.ToOrderedArray(n);
        }

        int next = (me + 1) % n;           // следующий по кольцу
        int prev = (me - 1 + n) % n;       // предыдущий по кольцу
        int starter = (root + 1) % n;      // узел, который начинает цепочку

        if (me == starter)
        {
            // Стартовый узел создает накопитель и пускает его по кольцу
            var acc = new GatherAccumulator();
            acc.Add(me, localData);
            await comm.SendRaw(next, acc, MessageTypeNames.Gather, MessageTags.Gather);

            // не корень -> результата нет
            return null;
        }
        else if (me == root)
        {
            // Корень получает накопитель от предыдущего и добавляет себя последним
            var acc = (GatherAccumulator)await comm.ReceiveRaw(prev, MessageTypeNames.Gather, MessageTags.Gather);
            acc.Add(me, localData);

            return acc.ToOrderedArray(n);
        }
        else
        {
            // Промежуточный узел получил -> добавил себя -> отдал дальше
            var acc = (GatherAccumulator)await comm.ReceiveRaw(prev, MessageTypeNames.Gather, MessageTags.Gather);
            acc.Add(me, localData);
            await comm.SendRaw(next, acc, MessageTypeNames.Gather, MessageTags.Gather);
            return null;
        }
    }

    //  SCATTER (раздача)
    //  Корень -> каждому свой кусок. Корень рассылает data[i] узлу i.
    public async Task<object> ImplementScatter(MpiCommunicator comm, object[] data, int root)
    {
        int n = comm.TotalNodes;
        int me = comm.Rank;

        if (me == root)
        {
            object myPiece = null;
            for (int i = 0; i < n; i++)
            {
                if (i == root)
                    // свой кусок оставляем себе
                    myPiece = data[i];
                else
                    await comm.SendRaw(i, data[i], MessageTypeNames.Scatter, MessageTags.Scatter);
            }
            return myPiece;
        }
        else
        {
            return await comm.ReceiveRaw(root, MessageTypeNames.Scatter, MessageTags.Scatter);
        }
    }

    //  REDUCE (редукция по дереву)
    //  Все -> корню с применением функции op (например, сумма/макс/мин).
    //  Дерево то же, что в Broadcast, но данные текут СНИЗУ ВВЕРХ:
    //  листья шлют значения родителям, родители сворачивают и шлют выше.
    public async Task<object> ImplementReduce(MpiCommunicator comm, object localValue,
                                  Func<object, object, object> op, int root)
    {
        int n = comm.TotalNodes;
        int me = comm.Rank;
        int v = (me - root + n) % n;

        object accumulated = localValue;

        // Сначала получаем значения от детей и сворачиваем их функцией op.
        foreach (int childV in new[] { 2 * v + 1, 2 * v + 2 })
        {
            if (childV < n)
            {
                int childReal = (childV + root) % n;
                object childValue = await comm.ReceiveRaw(childReal, MessageTypeNames.Reduce, MessageTags.Reduce);
                accumulated = op(accumulated, childValue);
            }
        }

        if (v == 0)
        {
            // корень получил итоговый результат
            return accumulated;
        }
        else
        {
            // Не корень. Отправляем свернутое значение родителю.
            int parentReal = ((v - 1) / 2 + root) % n;
            await comm.SendRaw(parentReal, accumulated, MessageTypeNames.Reduce, MessageTags.Reduce);
            return null;
        }
    }

    //  BARRIER (барьер)
    //  Никто не идет дальше, пока все не дойдут до барьера.
    //  Схема сбор + разрешение: все шлют корню, корень дожидается всех
    //  и шлет всем "можно идти".
    public async Task ImplementBarrier(MpiCommunicator comm)
    {
        int n = comm.TotalNodes;
        int me = comm.Rank;
        // координатор барьера
        const int root = 0;

        if (me == root)
        {
            // Дожидаемся "прибытия" от всех остальных
            for (int i = 0; i < n; i++)
                if (i != root)
                    await comm.ReceiveRaw(i, MessageTypeNames.Barrier, MessageTags.BarrierArrive);

            // Разрешаем всем продолжать
            for (int i = 0; i < n; i++)
                if (i != root)
                    await comm.SendRaw(i, "go", MessageTypeNames.Barrier, MessageTags.BarrierRelease);
        }
        else
        {
            // сообщаем корню, что дошли, и ждем разрешения
            await comm.SendRaw(root, "here", MessageTypeNames.Barrier, MessageTags.BarrierArrive);
            await comm.ReceiveRaw(root, MessageTypeNames.Barrier, MessageTags.BarrierRelease);
        }
    }
}
