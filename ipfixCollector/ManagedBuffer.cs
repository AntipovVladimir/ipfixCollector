namespace ipfixCollector;

public sealed class ManagedBuffer:IDisposable
{
    private readonly int Capacity;
    private byte[] m_buffer;
    private int current;
    private readonly int _sliceSize;
    public ManagedBuffer(int capacity = 1024, int sliceSize = 52)
    {
        Capacity = capacity;
        _sliceSize = sliceSize;
        m_buffer = new byte[capacity * sliceSize];
    }
    public bool IsFull => current == Capacity;
    public bool IsEmpty => current == 0;

    public ReadOnlyMemory<byte> GetBufferArray()
    {
        return IsFull ? m_buffer : m_buffer.AsSpan().Slice(0, _sliceSize * current).ToArray();
    }

    public ReadOnlySpan<byte> GetBuffer() => m_buffer.AsSpan().Slice(0, _sliceSize*current);

    public void Flush()
    {
        current = 0;
        Array.Clear(m_buffer);
    }
    
    public void Insert(ReadOnlySpan<byte> span)
    {
        if (current < Capacity && span.Length == _sliceSize)
        {
            var bspan = m_buffer.AsSpan().Slice(current * _sliceSize, _sliceSize);
            span.CopyTo(bspan);
            current++;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private bool isDisposed;
    private void Dispose(bool disposing)
    {
        if (isDisposed) return;
        if (disposing)
        {
            Array.Clear(m_buffer);
        }

        m_buffer = null;
        isDisposed = true;
    }
}