namespace ipfixCollector;

public class ManagedBuffer
{
    private readonly int Capacity;
    private readonly byte[] m_buffer;
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
    public ReadOnlyMemory<byte> GetBufferArray() => m_buffer.AsSpan().Slice(0, _sliceSize*current).ToArray();
    public ReadOnlySpan<byte> GetBuffer() => m_buffer.AsSpan().Slice(0, _sliceSize*current);
    
    public void Insert(ReadOnlySpan<byte> span)
    {
        if (current < Capacity && span.Length == _sliceSize)
        {
            var bspan = m_buffer.AsSpan().Slice(current * _sliceSize, _sliceSize);
            span.CopyTo(bspan);
            current++;
        }
    }

}