namespace ipfixCollector;

public sealed class BufferWriter : IDisposable
{
    private const int simultaneousBuffers = 32;
    private const int bufferLength = 8000; // packets
    private const int limitReached = simultaneousBuffers / 2;
    private StreamWriter _streamWriter;
    private static readonly TaskFactory m_task_factory = new();
    public long LastAccessedTime { get; private set; }
    
    public BufferWriter(string path, string filename)
    {
        if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
            Directory.CreateDirectory(path);
        _streamWriter = new StreamWriter(filename, append: true);
        m_buffer = new ManagedBuffer(bufferLength);
        LastAccessedTime = Environment.TickCount64;
    }
    

    private ManagedBuffer m_buffer;
    private readonly Queue<ManagedBuffer> m_buffers = new (simultaneousBuffers);
    public bool IsDisposed => isDisposed;

    public void Write(ReadOnlySpan<byte> data)
    {
        if (isDisposed) return;
        if (m_buffer.IsFull)
        {
            if (m_buffers.Count == limitReached)
            {
                m_task_factory.StartNew(async () => await FlushBuffers());
            }

            m_buffers.Enqueue(m_buffer);
            m_buffer = new ManagedBuffer(bufferLength);
        }

        m_buffer.Insert(data);
        LastAccessedTime = Environment.TickCount64;
    }

    private async Task FlushBuffers(bool all = false)
    {
        while (m_buffers is not null && m_buffers.Count > 0)
        {
            var buf = m_buffers.Dequeue();
            if ((!all && buf.IsFull) || (all && !buf.IsEmpty))
            {
                await _streamWriter.BaseStream.WriteAsync(buf.GetBufferArray());
                buf.Flush();
            }
        }
    }

    private bool isDisposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (isDisposed) return;
        if (disposing)
        {
            FlushBuffers(true).Wait();
            if (!m_buffer.IsEmpty)
            {
                _streamWriter.BaseStream.Write(m_buffer.GetBuffer());
                m_buffer.Flush();
            }
            m_buffer.Dispose();
            m_buffer = null;
            _streamWriter.Close();
            _streamWriter.Dispose();
            _streamWriter = null;
        }

        isDisposed = true;
    }
}