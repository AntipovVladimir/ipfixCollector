using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;

// ReSharper disable UseStringInterpolation

namespace ipfixCollector;

public class CollectorServer
{
    private const int IPFIX_PACKET_MAX_SIZE = 1500;
    private const int PORT_TO_LISTEN_TO = 1500;
    readonly IPEndPoint RemoteEndPoint = new(IPAddress.Any, PORT_TO_LISTEN_TO);
    private static readonly Logger Log = LogManager.GetLogger("error");
    
    private readonly bool debug;
/*
 * WARNING!
 * ipfix is BigEndian!
 * win/linux x86/x64 is LittleEndian! 
 * 
 */

    private void ConfigureLogging(bool console)
    {
        LoggingConfiguration configuration = new();
        if (console)
        {
            ConsoleTarget consoleTarget = new();
            configuration.AddTarget("console", consoleTarget);
            consoleTarget.Layout = @"${date:format=HH\:mm\:ss} ${message}";
            configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, consoleTarget));
        }

        AsyncTargetWrapper efileTarget = new(new FileTarget()
        {
            FileName = @"./Logs/collector.error.log",
            Layout = @"${date:format=yyyy.MM.dd HH\:mm\:ss} ${message}",
            ArchiveFileName = @"./Logs/collector.error.log",
            ArchiveSuffixFormat = @".{1:yyyyMMdd}",
            ArchiveEvery = FileArchivePeriod.Day,
            MaxArchiveDays = 500
        });

        configuration.AddTarget("error", efileTarget);
        configuration.LoggingRules.Add(new LoggingRule("error", LogLevel.Error, efileTarget));
        LogManager.Configuration = configuration;
    }

    private readonly System.Timers.Timer FlushTimer;

    public CollectorServer(bool _debug, bool _console)
    {
        m_bufferManagerSAEA = new ManagedQueue<SocketAsyncEventArgs>(MaxSimultaneousBuffers);
        m_bufferManagerB = new ManagedQueue<byte[]>(MaxSimultaneousBuffers);
        debug = _debug;
        ConfigureLogging(_console);
        PrepareBuffers();
        init();

        FlushTimer = new System.Timers.Timer();
        FlushTimer.Interval = 30_000;
        FlushTimer.Elapsed += FlushUnusedWriters;
        FlushTimer.Enabled = true;
    }

    private void PrepareBuffers()
    {
        for (int i = 0; i < MaxSimultaneousBuffers; i++)
        {
            SocketAsyncEventArgs a = new();
            m_bufferManagerSAEA.Insert(a);
            byte[] b = new byte[IPFIX_PACKET_MAX_SIZE];
            m_bufferManagerB.Insert(b);
        }

        Log.Info(string.Format("Built {0} packet buffers", MaxSimultaneousBuffers));
    }

    private int MaxSimultaneousBuffers { get; set; } = 256000;
    private Socket m_socket;
    private CancellationTokenSource tokenSource;
    readonly TaskFactory m_task_factory = new();
    private readonly ManagedQueue<SocketAsyncEventArgs> m_bufferManagerSAEA;
    private readonly ManagedQueue<byte[]> m_bufferManagerB;

    public async Task Start()
    {
        tokenSource = new CancellationTokenSource();
        m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        m_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
        m_socket.Bind(new IPEndPoint(IPAddress.Any, PORT_TO_LISTEN_TO));
        await m_task_factory.StartNew(Receive);
        Log.Error(string.Format("IPFIX Collector started. Listening on UDP:{0}", PORT_TO_LISTEN_TO));
    }

    public async Task Stop()
    {
        FlushTimer?.Stop();
        await tokenSource.CancelAsync();
        lock (writers)
        {
            foreach (BufferWriter writer in writers.Values)
                writer.Dispose();
            writers.Clear();
        }

        Log.Error(string.Format("IPFIX Collector stopped at {0}", DateTime.Now));
        if (m_socket is null)
            return;
        try
        {
            m_socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        m_socket.Close();
        await Task.CompletedTask;
        Log.Error("IPFIX Collector socket closed.");
    }

    public void Dispose()
    {
        Stop().Wait();
    }


    private int m_pause_stuck_counter;
    private const int MaxStuckTimes = 30;


    private async Task Receive()
    {
        if (tokenSource.IsCancellationRequested)
            return;
        if (m_bufferManagerSAEA.Last < 4)
        {
            if (m_pause_stuck_counter > MaxStuckTimes)
            {
                Log.Error(string.Format("No available buffers at {0} seconds. Service is dead.", MaxStuckTimes));
                await tokenSource.CancelAsync();
                return;
            }

            m_pause_stuck_counter++;
            await Task.Delay(1000);
            await Receive();
            return;
        }

        m_pause_stuck_counter = 0;
        SocketAsyncEventArgs args = m_bufferManagerSAEA.TakeNext();
        byte[] buff = m_bufferManagerB.TakeNext();
        args.SetBuffer(buff, 0, buff.Length);
        args.Completed += PacketReceived;
        args.RemoteEndPoint = RemoteEndPoint;
        try
        {
            if (!m_socket.ReceiveMessageFromAsync(args))
                await OnPacketReceived(args);
        }
        catch
        {
            // we should only jump here when we disconnect all the clients.
        }
    }

    private void PacketReceived(object sender, SocketAsyncEventArgs e)
    {
        OnPacketReceived(e).Wait();
    }


    private async Task OnPacketReceived(SocketAsyncEventArgs e)
    {
        await m_task_factory.StartNew(Receive);
        switch (e.BytesTransferred)
        {
            case <= 0:
                break;
            case > IPFIX_PACKET_MAX_SIZE:
                if (e.RemoteEndPoint is not null)
                    Log.Debug(string.Format("Data too large from {0}. Discarding packet.", ((IPEndPoint)e.RemoteEndPoint).Address));
                break;
            default:
            {
                byte[] data = new byte[e.BytesTransferred];
                if (e.Buffer is not null)
                    Buffer.BlockCopy(e.Buffer, 0, data, 0, e.BytesTransferred);
                OnDataReceived(data);
                break;
            }
        }

        ReleaseArgs(e);
    }

    private void ReleaseArgs(SocketAsyncEventArgs e)
    {
        e.Completed -= PacketReceived;
        m_bufferManagerSAEA.Insert(e);
        m_bufferManagerB.Insert(e.Buffer);
    }

    class ipfixHeader
    {
        public ushort Version;
        public ushort MessageLength;
        public uint ExportTime;
        public uint SequenceNumber;
        public uint DomainId;
        public uint SetId;
    }

    class templateField
    {
        public short ElementId;
        public ushort Length;
        public uint EnterpriseNumber;
    }

    class OptionsTemplate
    {
        public ushort TemplateId;
        public ushort FieldCount;
        public templateField[] TemplateField = Array.Empty<templateField>();
        public ushort ScopeFieldCount;
        public templateField[] ScopeField = Array.Empty<templateField>();
    }


    templateField decodeSingleTemplateField(ReadOnlySpan<byte> payload)
    {
        templateField result = new templateField
        {
            ElementId = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(0, 2)),
            Length = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2)),
        };
        if (result.ElementId < 0)
        {
            result.EnterpriseNumber = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        }

        return result;
    }

    void init()
    {
        OptionsTemplate template = new()
        {
            TemplateId = 256,
            FieldCount = 11,
            TemplateField =
            [
                new templateField() { ElementId = 323, EnterpriseNumber = 0, Length = 8 },
                new templateField() { ElementId = 4, EnterpriseNumber = 0, Length = 1 },
                new templateField() { ElementId = 230, EnterpriseNumber = 0, Length = 1 },
                new templateField() { ElementId = 8, EnterpriseNumber = 0, Length = 4 },
                new templateField() { ElementId = 225, EnterpriseNumber = 0, Length = 4 },
                new templateField() { ElementId = 7, EnterpriseNumber = 0, Length = 2 },
                new templateField() { ElementId = 227, EnterpriseNumber = 0, Length = 2 },
                new templateField() { ElementId = 12, EnterpriseNumber = 0, Length = 4 },
                new templateField() { ElementId = 11, EnterpriseNumber = 0, Length = 2 },
                new templateField() { ElementId = 2000, EnterpriseNumber = 0, Length = 8 },
                new templateField() { ElementId = 2003, EnterpriseNumber = 0, Length = 0 }
            ]
        };
        Templates.Add(256, template);
    }
  
/*
 323	0	8	int64	SYSTEM_TIME_WHEN_THE_EVENT_OCCURRED	Системное время, когда произошло событие
4	0	1	int8	PROTOCOL_IDENTIFIER	Идентификатор протокола транспортного уровня
230	0	1	int8	TYPE_OF_EVENT	Тип события
8	0	4	IPv4	SOURCE_IPV4_ADDRESS	Адрес отправителя
225	0	4	IPv4	POST_NAT_SOURCE_IPV4_ADDRESS	Адрес отправителя после NAT
7	0	2	int16	SOURCE_PORT	Порт отправителя
227	0	2	int16	POST_NAPT_SOURCE_TRANSPORT_PORT	Порт отправителя после NAT
12	0	4	IPv4	DESTINATION_IPV4_ADDRESS	Адрес получателя
11	0	2	int16	DESTINATION_TRANSPORT_PORT	Порт получателя
2000	43823	8	int64	SESSION_ID	Идентификатор сессии
2003	43823	-	string	LOGIN	User name при входе в систему
 */

    private readonly Dictionary<byte, string> NatEvents = new()
    {
        { 0, "Reserved" },
        { 1, "NAT translation create" },
        { 2, "NAT translation delete" },
        { 3, "NAT addresses exhausted" },
        { 4, "Nat44 session create" },
        { 5, "Nat44 session delete" },
        { 6, "Nat64 session create" },
        { 7, "Nat64 session delete" },
        { 8, "Nat44 Bib create" },
        { 9, "Nat44 Bib delete" },
        { 10, "Nat64 Bib create" },
        { 11, "Nat64 Bib delete" },
        { 12, "Nat ports exhausted" },
        { 13, "Quota exceeded" },
        { 14, "Address binding create" },
        { 15, "Address binding delete" },
        { 16, "Port block allocation" },
        { 17, "Port block deallocation" },
        { 18, "Threshold reached" }
    };

    void m_decodeSingleOption(ReadOnlySpan<byte> data, templateField field, Memory<byte> buffer)
    {
        if (data.Length < field.Length)
            return;
        if (field.EnterpriseNumber != 0)
            return;
        int start = 0;
        int len = 0;
        bool fixlen = false;
        switch (field.ElementId)
        {
            case 323: //  323	0	8	int64	SYSTEM_TIME_WHEN_THE_EVENT_OCCURRED	Системное время, когда произошло событие
                start = 0;
                len = 8;
                break;
            case 4: // 4	0	1	int8	PROTOCOL_IDENTIFIER	Идентификатор протокола транспортного уровня
                start = 8;
                len = 1;
                break;
            case 230: // 230	0	1	int8	TYPE_OF_EVENT	Тип события
                start = 9;
                len = 1;
                break;
            case 8: // 8	0	4	IPv4	SOURCE_IPV4_ADDRESS	Адрес отправителя
                start = 10;
                len = 4;
                break;
            case 225: // 225	0	4	IPv4	POST_NAT_SOURCE_IPV4_ADDRESS	Адрес отправителя после NAT
                start = 14;
                len = 4;
                break;
            case 7: // 7	0	2	int16	SOURCE_PORT	Порт отправителя
                start = 18;
                len = 2;
                break;
            case 227: // 227	0	2	int16	POST_NAPT_SOURCE_TRANSPORT_PORT	Порт отправителя после NAT
                start = 20;
                len = 2;
                break;
            case 12: // 12	0	4	IPv4	DESTINATION_IPV4_ADDRESS	Адрес получателя
                start = 22;
                len = 4;
                break;
            case 11: // 11	0	2	int16	DESTINATION_TRANSPORT_PORT	Порт получателя
                start = 26;
                len = 2;
                break;
            case 2000: // 2000	43823	8	int64	SESSION_ID	Идентификатор сессии
                start = 28;
                len = 8;
                break;
            case 2003: // 2003	43823	-	string	LOGIN	User name при входе в систему
                start = 36;
                len = data[0];
                if (len > data.Length - 1)
                    len = data.Length - 1;
                fixlen = true;
                break;
        }

        Span<byte> span = buffer.Slice(start, len).Span;
        if (fixlen)
            data.Slice(1, len).CopyTo(span);
        else
            data.Slice(0, len).CopyTo(span);
    }


    private void OnDataReceived(byte[] data)
    {
        m_parsePayload(data);
    }

    private readonly Dictionary<uint, OptionsTemplate> Templates = new();

    void parseOptionsTemplate(ipfixHeader header, ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= 30)
            return;
        if (Templates.ContainsKey(header.SetId))
            return;

        OptionsTemplate template = new OptionsTemplate()
        {
            TemplateId = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(20, 2)),
            FieldCount = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(22, 2)),
            ScopeFieldCount = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(24, 2))
        };
        ReadOnlySpan<byte> slice = payload[26..];
        List<templateField> tfs = new();
        for (int i = template.ScopeFieldCount; i > 0; i--)
        {
            if (slice.Length < 4)
                break;
            templateField tf = decodeSingleTemplateField(slice);
            tfs.Add(tf);
            int offset = tf.ElementId < 0 ? 8 : 4;
            if (slice.Length < offset)
                break;
            slice = slice[offset..];
        }

        template.ScopeField = tfs.ToArray();
        tfs.Clear();
        for (int i = template.FieldCount - template.ScopeFieldCount; i > 0; i--)
        {
            if (slice.Length < 4)
                break;
            templateField tf = decodeSingleTemplateField(slice);
            tfs.Add(tf);
            int offset = tf.ElementId < 0 ? 8 : 4;
            if (slice.Length < offset)
                break;
            slice = slice[offset..];
        }

        template.TemplateField = tfs.ToArray();
        Templates.Add(template.TemplateId, template);
    }

    private const int fixedPayload = 36;
    private static readonly byte[] sizeNuller = "\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0"u8.ToArray(); // 16 byte

    void m_parseOptions(ipfixHeader header, ReadOnlySpan<byte> payload)
    {
        int plength = payload.Length;
        if (plength < 57) return;
        if (!Templates.TryGetValue(header.SetId, out OptionsTemplate template)) return;

        byte[] fixedRaw = new byte[52];
        Memory<byte> buf = fixedRaw;
        int currentOffset = 20;
        while (currentOffset < plength - fixedPayload)
        {
            int strlen = 0;
            Span<byte> span = buf.Slice(0, fixedPayload).Span;
            payload.Slice(currentOffset, fixedPayload).CopyTo(span);
            currentOffset += fixedPayload;
            strlen = payload[currentOffset];
            currentOffset++;
            if (strlen == 0)
            {
                WriteData(fixedRaw);
                continue;
            }

            if ((strlen > plength - currentOffset) || strlen > 16)
            {
                WriteData(fixedRaw);
                Log.Error("strlen>payload.Length - currentOffset or username length > 16, packet is broken");
                Log.Error(string.Format("payloadlen: {0}, currentOffset: {1}, strlen: {2}", plength, currentOffset, strlen));
                break;
            }

            span = buf.Slice(fixedPayload, 16).Span;
            sizeNuller.CopyTo(span);
            span = buf.Slice(fixedPayload, strlen).Span;
            payload.Slice(currentOffset, strlen).CopyTo(span);
            WriteData(fixedRaw);
            currentOffset += strlen;
        }
    }

    void m_parsePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 18) return;
        StringBuilder sb = new();
        ipfixHeader header = new ipfixHeader()
        {
            Version = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(0, 2)),
            MessageLength = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2)),
            ExportTime = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)),
            SequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4)),
            DomainId = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12, 4)),
            SetId = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(16, 2))
        };
        if (header.Version != 10) return;
        if (payload.Length < 24) return;
        try
        {
            if (header.SetId == 3)
                parseOptionsTemplate(header, payload);
            else if (header.SetId >= 256)
                m_parseOptions(header, payload);
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            if (ex.StackTrace != null)
                Log.Error(ex.StackTrace);
            if (ex.InnerException != null)
            {
                Log.Error(ex.InnerException.Message);
                if (ex.InnerException.StackTrace != null)
                    Log.Error(ex.InnerException.StackTrace);
            }
        }
    }


    private readonly ConcurrentDictionary<string, BufferWriter> writers = new();


    void FlushUnusedWriters(object sender, EventArgs args)
    {
        Log.Info("flush unused writers run");
        long currentTick = Environment.TickCount64;
        Queue<string> toRemove = new Queue<string>();
        int totalWriters = writers.Count;
        int totalWritersToRemove = 0;

        foreach (KeyValuePair<string, BufferWriter> kvp in writers)
        {
            if (kvp.Value is null) continue;
            if (currentTick - kvp.Value.LastAccessedTime >= 3_600_000) // 1h, 43_200_000) // 12h 
            {
                toRemove.Enqueue(kvp.Key);
                totalWritersToRemove++;
            }
        }

        try
        {
            Log.Info("Total Memory: {0}", GC.GetTotalMemory(false));
            Log.Info(string.Format("writers status: [{0}/{1}]", totalWritersToRemove, totalWriters));
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            if (ex.StackTrace  != null)
                Log.Error(ex.StackTrace);
        }

        lock (writers)
            while (toRemove.Count > 0)
            {
                string writerName = toRemove.Dequeue();
                var writer = writers[writerName];
                writers[writerName] = null;
                if (writer != null)
                {
                    writer.Dispose();
                    writer = null;
                }
                Log.Info(string.Format("releasing unused writer: {0}", writerName));
            }
        GC.Collect();
    }

//    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
    void WriteData(ReadOnlySpan<byte> buf)
    {
        if (buf.Length != 52) return;
        ulong dts = BinaryPrimitives.ReadUInt64BigEndian(buf.Slice(0, 8));
        DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(dts);
        int year = dt.Year;
        int month = dt.Month;
        int day = dt.Day;
        string path = string.Format("Logs/{0}/{1}/{2}", year, month, day);
        string filename = string.Format("{0}/{1}.{2}.{3}.{4}.raw", path, buf[14], buf[15], buf[16], buf[17]);
        try
        {
            BufferWriter writer;
            lock (writers)
            {
                if (!writers.ContainsKey(filename))
                {
                    writers.TryAdd(filename, new BufferWriter(path, filename));
                    Log.Info(string.Format("Added writer for {0}", filename));
                }

                writer = writers[filename];
            }

            if (writer is null || writer.IsDisposed) // maybe it is already disposed!
            {
                writer = new BufferWriter(path, filename);
                Log.Info(string.Format("Reopened writer for {0}", filename));
                lock (writers)
                    writers[filename] = writer;
            }

            writer.Write(buf);
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            if (ex.StackTrace != null)
                Log.Error(ex.StackTrace);
        }
    }
}