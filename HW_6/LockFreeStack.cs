using System.Threading;


public class LockFreeStack<T>
{
    private class Node
    {
        public T Value;
        public Node? Next;

        public Node(T value, Node? next = null)
        {
            Value = value;
            Next = next;
        }
    }

    private Node? _head = null;

    public void Push(T item)
    {
        Node newNode = new Node(item);
        Node? currentHead;
        do
        {
            currentHead = _head;
            newNode.Next = currentHead;
        }
        while (Interlocked.CompareExchange(ref _head, newNode, currentHead) != currentHead);
    }

    public bool TryPop(out T? item)
    {
        Node? currentHead;
        do
        {
            currentHead = _head;

            // Стек пуст
            if (currentHead == null)
            {
                item = default;
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref _head, currentHead.Next, currentHead) != currentHead);

        item = currentHead.Value;
        return true;
    }

    public bool TryPeek(out T? item)
    {
        Node? currentHead = Interlocked.CompareExchange(ref _head, null, null);

        if (currentHead == null)
        {
            item = default;
            return false;
        }

        item = currentHead.Value;
        return true;
    }

    public bool IsEmpty()
    {
        return Interlocked.CompareExchange(ref _head, null, null) == null;
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _head, null);
    }

    // Читаем напрямую, т.к. вызов после тестов
    public int UnsafeCount()
    {
        int count = 0;
        Node? current = _head;

        while (current != null)
        {
            count++;
            current = current.Next;
        }
        return count;
    }
}
