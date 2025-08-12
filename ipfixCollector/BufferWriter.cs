namespace ipfixCollector;

public class BufferWriter : IDisposable
{
    private const int simultaneousBuffers = 16;
    private const int bufferLength = 4096; // packets
    private const int limitReached = simultaneousBuffers / 2;
    private readonly StreamWriter _streamWriter;
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
    private readonly Queue<ManagedBuffer> m_buffers = new Queue<ManagedBuffer>(simultaneousBuffers);
    public bool IsDisposed => isDisposed;
    public bool Write(ReadOnlySpan<byte> data)
    {
        if (isDisposed)
            return false;
        if (m_buffer.IsFull)
        {
            if (m_buffers.Count == limitReached)
            {
                Task.Run(FlushBuffers).ConfigureAwait(ConfigureAwaitOptions.None);
            }

            m_buffers.Enqueue(m_buffer);
            m_buffer = new ManagedBuffer(bufferLength);
        }

        m_buffer.Insert(data);
        LastAccessedTime = Environment.TickCount64;
        return true;
    }

    private async Task FlushBuffers()
    {
        if (m_buffers is null)
            return;
        while (m_buffers.Any())
        {
            var buf = m_buffers.Dequeue();
            var buffer = buf.GetBufferArray();
            if (!buffer.IsEmpty)
                await _streamWriter.BaseStream.WriteAsync(buffer);
        }
    }

    private bool isDisposed ;

    public void Dispose()
    {
        if (isDisposed)
            return;
        FlushBuffers().Wait();
        if (!m_buffer.IsEmpty)
            _streamWriter.BaseStream.Write(m_buffer.GetBuffer());
        _streamWriter.Close();
        isDisposed = true;
    }
}