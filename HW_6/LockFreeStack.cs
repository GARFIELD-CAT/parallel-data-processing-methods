using System.Threading;

namespace AtomicOperationsDemo
{
    /// <summary>
    /// Потокобезопасный стек без использования блокировок (lock-free).
    /// Реализован на основе односвязного списка и CAS-операции CompareExchange.
    /// </summary>
    /// <typeparam name="T">Тип элементов стека.</typeparam>
    public class LockFreeStack<T>
    {
        /// <summary>
        /// Узел стека — хранит значение и ссылку на следующий узел.
        /// </summary>
        private class Node
        {
            public T Value;            // Значение элемента
            public Node Next;          // Ссылка на следующий узел (ниже в стеке)

            public Node(T value, Node next = null)
            {
                Value = value;
                Next = next;
            }
        }

        // Голова стека (верхний узел). Все изменения выполняются через CAS.
        private Node _head;

        /// <summary>
        /// Добавляет элемент на вершину стека (атомарно, без блокировок).
        /// </summary>
        /// <param name="item">Добавляемый элемент.</param>
        public void Push(T item)
        {
            Node newNode = new Node(item);
            Node currentHead;
            do
            {
                // Запоминаем текущую голову стека
                currentHead = _head;
                // Новый узел должен указывать на неё
                newNode.Next = currentHead;
            }
            // Пытаемся установить новый узел головой стека, если за время подготовки
            // голова не изменилась. Если изменилась — повторяем.
            while (Interlocked.CompareExchange(ref _head, newNode, currentHead) != currentHead);
        }

        /// <summary>
        /// Пытается извлечь элемент с вершины стека.
        /// Возвращает true и элемент через out при успехе, иначе false.
        /// </summary>
        public bool TryPop(out T item)
        {
            Node currentHead;
            do
            {
                currentHead = _head;
                if (currentHead == null)
                {
                    item = default(T);
                    return false; // Стек пуст
                }
            }
            // Пытаемся сдвинуть голову на следующий узел.
            // Если за время подготовки голова изменилась, повторяем.
            while (Interlocked.CompareExchange(ref _head, currentHead.Next, currentHead) != currentHead);

            item = currentHead.Value;
            return true;
        }

        /// <summary>
        /// Пытается прочитать верхний элемент без его извлечения.
        /// Использует атомарное чтение через CompareExchange, чтобы избежать
        /// гонок при чтении ссылки.
        /// </summary>
        public bool TryPeek(out T item)
        {
            // Атомарно читаем текущую голову, передавая одинаковые значения для сравнения и замены.
            Node currentHead = Interlocked.CompareExchange(ref _head, null, null);
            if (currentHead == null)
            {
                item = default(T);
                return false;
            }

            item = currentHead.Value;
            return true;
        }

        /// <summary>
        /// Проверяет, пуст ли стек (моментальный неблокирующий снимок).
        /// </summary>
        public bool IsEmpty()
        {
            // Атомарно читаем голову
            return Interlocked.CompareExchange(ref _head, null, null) == null;
        }

        /// <summary>
        /// Очищает стек (атомарно устанавливает голову в null).
        /// </summary>
        public void Clear()
        {
            Interlocked.Exchange(ref _head, null);
        }

        /// <summary>
        /// Возвращает количество элементов в стеке (проход по цепочке).
        /// Этот метод не является потокобезопасным при конкурентных изменениях,
        /// его следует вызывать только после завершения всех операций.
        /// </summary>
        public int UnsafeCount()
        {
            int count = 0;
            Node current = _head; // Читаем напрямую, т.к. вызов после тестов
            while (current != null)
            {
                count++;
                current = current.Next;
            }
            return count;
        }
    }
}