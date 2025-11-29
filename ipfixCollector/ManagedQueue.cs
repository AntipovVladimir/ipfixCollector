
namespace ipfixCollector;

public sealed class ManagedQueue<T>
{
    public int Capacity { get; }
    private readonly Queue<T> m_queue;
    private readonly SemaphoreSlim m_restrictor;

    public ManagedQueue(int capacity = 400, bool fillQueue = false, Queue<T> queue = null)
    {
        Capacity = capacity;
        m_queue = queue ?? new Queue<T>(capacity);
        m_restrictor = new SemaphoreSlim(fillQueue ? Capacity : 0, Capacity);
        if (fillQueue)
        {
            for (int i = 0; i < Capacity; i++)
                Insert(default);
        }
    }

    public int Last => m_queue.Count;

    public T TakeNext()
    {
        if (m_queue is null) throw new InvalidOperationException("The queue cannot be null");
        m_restrictor.Wait();
        lock (m_queue)
        {
            if (m_queue.Count > 0) return m_queue.Dequeue();
            throw new Exception("There has been a Semaphore/queue offset");
        }
    }

    public void Insert(T item)
    {
        if (m_queue is null) throw new InvalidOperationException("The queue cannot be null");
        if (item is null) throw new ArgumentException("The item cannot be null");
        lock (m_queue)
        {
            m_queue.Enqueue(item);
            m_restrictor.Release();
        }
    }
}