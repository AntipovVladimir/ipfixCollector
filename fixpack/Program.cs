// See https://aka.ms/new-console-template for more information


using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

void printHelp()
{
    Console.WriteLine("Usage: fixpack input.raw [-f] [-o path]");
    Console.WriteLine("options:");
    Console.WriteLine("\t-f - force delete input files after processing");
    Console.WriteLine("\t-o path - output path for processing result, instead of current dir");
}

if (Environment.GetCommandLineArgs().Length < 2)
{
    printHelp();
    return;
}

string[] iargs = Environment.GetCommandLineArgs();
bool forceDeleteInputFiles = false;
int index = 0;
string forceOutputPath = string.Empty;
foreach (var arg in iargs)
{
    switch (arg.ToLower())
    {
        case "-f":
            forceDeleteInputFiles = true;
            break;
        case "-o":
            Console.WriteLine("{0}/{1}", index, iargs.Length);

            if (iargs.Length > index + 1)
            {
                forceOutputPath = Directory.Exists(iargs[index + 1]) ? iargs[index + 1] : string.Empty;
                Console.WriteLine(forceOutputPath);
            }

            break;
        case "-h":
        case "--help":
            printHelp();
            return;
    }
    index++;
}

string inputFileName = Environment.GetCommandLineArgs()[1];
if (string.IsNullOrWhiteSpace(inputFileName))
{
    Console.WriteLine("filename error");
    return;
}

var attr = File.GetAttributes(inputFileName);
var packer = new Packer(forceDeleteInputFiles);

if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
{
    // is a directory
    DirectoryInfo d = new DirectoryInfo(inputFileName);
    var filelist = d.GetFiles("*.raw", SearchOption.AllDirectories);
    Console.WriteLine("Found {0} files", filelist.Length);
    int totalf = filelist.Length;
    int currentf = 0;
    foreach (var file in filelist)
    {
        currentf++;
        Console.WriteLine("Processing {0} / {1}", currentf, totalf);
        packer.Run(file.FullName, forceOutputPath);
    }
}
else
{
    packer.Run(inputFileName, forceOutputPath);
}

class OneRecord(ReadOnlySpan<byte> buffer)
{
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"SystemTimeEvent: {SystemtimeEvent}");
        sb.AppendLine($"ProtocolIdentifier: {ProtocolIndetifier}");
        sb.AppendLine($"TypeOfEvent: {TypeOfEvent}");
        sb.AppendLine($"SourceAddress: {SourceAddress[0]}.{SourceAddress[1]}.{SourceAddress[2]}.{SourceAddress[3]}");
        sb.AppendLine($"PostNatSourceAddress: {PostNatSourceAddress[0]}.{PostNatSourceAddress[1]}.{PostNatSourceAddress[2]}.{PostNatSourceAddress[3]}");
        sb.AppendLine($"DestinationAddress: {DestinationAddress[0]}.{DestinationAddress[1]}.{DestinationAddress[2]}.{DestinationAddress[3]}");
        sb.AppendLine($"SourcePort: {SourcePort}");
        sb.AppendLine($"PostNatSourcePort: {PostNatSourcePort}");
        sb.AppendLine($"DestinationPort: {DestinationPort}");
        sb.AppendLine($"SessionID: {SessionId}");
        sb.AppendLine($"Login: {Encoding.ASCII.GetString(Login)}");
        return sb.ToString();
    }


    private readonly byte[] _buffer = buffer.ToArray();
    public ReadOnlySpan<byte> Span => _buffer;

    private ulong SystemtimeEvent => BinaryPrimitives.ReadUInt64BigEndian(Span.Slice(0, 8));
    public byte ProtocolIndetifier => Span[8];
    public byte TypeOfEvent => Span[9];
    public ReadOnlySpan<byte> SourceAddress => Span.Slice(10, 4);
    public ReadOnlySpan<byte> PostNatSourceAddress => Span.Slice(14, 4);
    private ushort SourcePort => BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(18, 2));
    private ushort PostNatSourcePort => BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(20, 2));
    public ReadOnlySpan<byte> DestinationAddress => Span.Slice(22, 4);
    private ushort DestinationPort => BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(26, 2));
    public ulong SessionId => BinaryPrimitives.ReadUInt64BigEndian(Span.Slice(28, 8));
    private ReadOnlySpan<byte> Login => Span.Slice(36, 16);

    /*
     OneRecord packet format:
     start:length = description
     0:8 = SystemTimeEvent
     8:1 = ProtocolIdentifier
     9:1 = TypeOfEvent
     10:4 = SourceAddress
     14:4 = PostNatSourceAddress
     18:2 = SourcePort
     20:2 = PostNatSourcePort
     22:4 = DestinationAddress
     26:2 = DestinationPort
     28:8 = SessionId
     36:16 = Login
     */

    public string GetTargetFile()
    {
        return string.Format("{0}.{1}.{2}.{3}.raw", PostNatSourceAddress[0], PostNatSourceAddress[1], PostNatSourceAddress[2], PostNatSourceAddress[3]);
    }
}


class PairRecord
{
    public PairRecord(OneRecord firstRecord, OneRecord secondRecord)
    {
        _buffer = new byte[59];
        if (firstRecord.TypeOfEvent == 1)
        {
            firstRecord.Span.Slice(0, 8).CopyTo(SystemTimeEventStart);
            secondRecord.Span.Slice(0, 8).CopyTo(SystemTimeEventStop);
        }
        else if (firstRecord.TypeOfEvent == 2)
        {
            secondRecord.Span.Slice(0, 8).CopyTo(SystemTimeEventStart);
            firstRecord.Span.Slice(0, 8).CopyTo(SystemTimeEventStop);
        }

        _buffer[16] = firstRecord.ProtocolIndetifier;
        firstRecord.SourceAddress.CopyTo(SourceAddress);
        firstRecord.PostNatSourceAddress.CopyTo(PostNatSourceAddress);
        firstRecord.DestinationAddress.CopyTo(DestinationAddress);
        firstRecord.Span.Slice(18, 2).CopyTo(SourcePort);
        firstRecord.Span.Slice(20, 2).CopyTo(PostNatSourcePort);
        firstRecord.Span.Slice(26, 2).CopyTo(DestinationPort);
        firstRecord.Span.Slice(28, 8).CopyTo(SessionId);
        firstRecord.Span.Slice(36, 16).CopyTo(Login);
    }

/* PairPacket structure
 start:length = description
 0:8 = SystemTimeEventStart
 8:8 = SystemTimeEventStop
 16:1 = ProtocolIdentifier
 17:4 = SourceAddress
 21:4 = DestinationAddress
 25:2 = SourcePort
 27:2 = PostNatSourcePort
 29:2 = DestinationPort
 31:8 = SessionId
 39:16 = Login
 offPacket structure (not stored inline, only in header)
 55:4 = PostNatSourceAddress
 */

    private readonly byte[] _buffer;
    public Span<byte> span => _buffer;
    private Span<byte> SystemTimeEventStart => span.Slice(0, 8);
    private Span<byte> SystemTimeEventStop => span.Slice(8, 8);
    public Span<byte> ProtocolIdentifier => span.Slice(16, 1);
    private Span<byte> SourceAddress => span.Slice(17, 4);
    private Span<byte> DestinationAddress => span.Slice(21, 4);
    private Span<byte> SourcePort => span.Slice(25, 2);
    private Span<byte> PostNatSourcePort => span.Slice(27, 2);
    private Span<byte> DestinationPort => span.Slice(29, 2);
    private Span<byte> SessionId => span.Slice(31, 8);
    private Span<byte> Login => span.Slice(39, 16);

    public Span<byte> PostNatSourceAddress => span.Slice(55, 4);


    public string GetPath()
    {
        ulong dtstart = BinaryPrimitives.ReadUInt64BigEndian(SystemTimeEventStart);
        ulong dtstop = BinaryPrimitives.ReadUInt64BigEndian(SystemTimeEventStop);
        ulong dts = dtstart != 0 ? dtstart : dtstop;
        var dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(dts);
        string year = dt.Year.ToString();
        string month = dt.Month.ToString();
        string day = dt.Day.ToString();
        return string.Format("{0}{1}{2}{3}{4}", year, Path.DirectorySeparatorChar, month, Path.DirectorySeparatorChar, day);
    }

    public string GetTargetFile()
    {
        return string.Format("{0}.{1}.{2}.{3}.rawlog", PostNatSourceAddress[0], PostNatSourceAddress[1], PostNatSourceAddress[2], PostNatSourceAddress[3]);
    }
}


/*
 * Flex raw nat log format
 * 20b length:
 * 0..12 - FLEXRAWNATLOG
 * 13..14 - Version (ushort, bigendian)
 * 15..18 - Post Nat Source Address
 * 19 - zero byte (end of header)
 */

class PairDataHeader
{
    private readonly byte[] header;
    private const ushort Version = 1;

    public PairDataHeader(ReadOnlySpan<byte> postNatSourceAddress)
    {
        header = new byte[20];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("FLEXRAWNATLOG"), 0, header, 0, 13);
        Buffer.BlockCopy(BitConverter.GetBytes(Version), 0, header, 13, 2);
        postNatSourceAddress.CopyTo(header.AsSpan().Slice(15, 4));
        header[19] = 0;
    }

    public ReadOnlySpan<byte> Header => header;
}

class Packer(bool forceFileDelete = false)
{
    readonly Dictionary<ulong, OneRecord> oneRecords = new();
    private readonly Queue<OneRecord> unpairedRecords = new();
    private readonly Queue<PairRecord> pairRecords = new();
    private readonly Dictionary<string, StreamWriter> writes = new();

    public void Run(string inputFileName, string forceOutputPath)
    {
        if (!File.Exists(inputFileName))
        {
            Console.WriteLine("Error: file not exists {0}", inputFileName);
            return;
        }

        Console.WriteLine("Processing file: {0}", inputFileName);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int writerCounter = 0;
        try
        {
            using StreamReader sr = new StreamReader(inputFileName);
            ulong totalRecords = 0;
            ulong pairRecordCount = 0;
            byte[] buffer = new byte[52];
            while (sr.BaseStream.Read(buffer, 0, 52) == 52)
            {
                OneRecord data = new OneRecord(buffer);
                totalRecords++;
                if (oneRecords.TryAdd(data.SessionId, data)) continue;
                oneRecords.Remove(data.SessionId, out var found);

                PairRecord newRecord = new(data, found);
                if (newRecord.span.Length == 59)
                    pairRecords.Enqueue(newRecord);

                pairRecordCount++;
            }

            if (!string.IsNullOrEmpty(forceOutputPath) && !Directory.Exists(forceOutputPath))
                Directory.CreateDirectory(forceOutputPath!);

            Console.WriteLine("Total processed records: {0}", totalRecords);
            Console.WriteLine("Total found pairs: {0}", pairRecordCount);
            Console.WriteLine("Total unpaired: {0}", oneRecords.Count);
            while (pairRecords.Count > 0)
            {
                var record = pairRecords.Dequeue();
                string dir =
                    string.IsNullOrEmpty(forceOutputPath)
                        ? record.GetPath()
                        : string.Concat(forceOutputPath, Path.DirectorySeparatorChar, record.GetPath());

                string fname = string.Concat(dir, Path.DirectorySeparatorChar, record.GetTargetFile());
                StreamWriter swf;
                if (!writes.ContainsKey(fname))
                {
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir!);
                    bool fileExists = File.Exists(fname);
                    bool fileCorrupt = false;
                    if (fileExists)
                    {
                        var fheader = new PairDataHeader(record.PostNatSourceAddress.ToArray());
                        using StreamReader srfh = new StreamReader(fname);
                        byte[] bheader = new byte[20];
                        int read = srfh.BaseStream.Read(bheader, 0, 20);
                        fileCorrupt = read != 20 || !fheader.Header.SequenceEqual(bheader);
                        if (fileCorrupt)
                        {
                            Console.WriteLine("File {0} corrupt! Header mismatch!", fname);
                            srfh.Close();
                            File.Move(fname, string.Format("{0}.bak", fname));
                        }
                    }

                    swf = new StreamWriter(fname, append: true);
                    if (!fileExists || fileCorrupt)
                    {
                        var fheader = new PairDataHeader(record.PostNatSourceAddress);
                        swf.BaseStream.Write(fheader.Header);
                    }

                    writes.Add(fname, swf);
                }
                else
                {
                    swf = writes[fname];
                }

                swf.BaseStream.Write(record.span);
            }


            foreach (var rec in oneRecords.Values)
            {
                unpairedRecords.Enqueue(rec);
            }

            oneRecords.Clear();
            Dictionary<string, StreamWriter> _writers = new();
            long unpairedCount = 0;
            while (unpairedRecords.Any())
            {
                var rec = unpairedRecords.Dequeue();
                string unpairTarget = rec.GetTargetFile();
                if (!_writers.ContainsKey(unpairTarget))
                {
                    StreamWriter srw = new StreamWriter(string.Concat("unpaired.", unpairTarget), append: true);
                    _writers.Add(unpairTarget, srw);
                }

                _writers[unpairTarget].BaseStream.Write(rec.Span);
                unpairedCount++;
            }

            foreach (var kvp in _writers)
                kvp.Value.Close();
            Console.WriteLine("Written {0} unpaired records", unpairedCount);
            foreach (var writer in writes.Values)
            {
                writerCounter++;
                writer.Close();
            }

            writes.Clear();

            Console.WriteLine("Written {0} files", writerCounter);
        }
        catch (Exception e)
        {
            Console.WriteLine("The process failed: {0}", e.Message);
            Console.WriteLine(e.StackTrace);
        }
        finally
        {
            if (writerCounter > 0 && forceFileDelete)
            {
                try
                {
                    File.Delete(inputFileName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        Console.WriteLine("Measured time: {0}", stopwatch.Elapsed);
    }
}