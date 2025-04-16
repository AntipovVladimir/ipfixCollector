using System.Buffers.Binary;
using System.Text;

/*
 * Flex raw nat log header format
 * 20b length:
 * 0..12 - FLEXRAWNATLOG
 * 13..14 - Version (ushort, bigendian)
 * 15..18 - Post Nat Source Address
 * 19 - zero byte (end of header)
 */

/* PairRecord structure
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
 */


if (Environment.GetCommandLineArgs().Length < 2)
{
    Console.WriteLine("Usage: searcher input.rawlog \"dd.mm.yyyy HH:mi\" [-u] [-ip aa.bb.cc.dd]");
    Console.WriteLine("options:");
    Console.WriteLine("\t-u - get unique matched logins only, instead of translation metadata");
    Console.WriteLine("\t-ip aa.bb.cc.dd - search only for certain translation matches destination ip");
    return;
}

string filename = args[0];
string datetime = args[1];
bool getUniqs = false;
int index = 0;
string[] iargs = Environment.GetCommandLineArgs();
byte[] destIp = Array.Empty<byte>();
foreach (var arg in iargs)
{
    switch (arg.ToLower())
    {
        case "-u":
            getUniqs = true;
            break;
        case "-ip":
            if (iargs.Length > index + 1)
            {
                string _destIp = iargs[index + 1];
                string[] _strings = _destIp.Split('.');
                if (_strings.Length == 4)
                {
                    byte.TryParse(_strings[0], out byte b0);
                    byte.TryParse(_strings[1], out byte b1);
                    byte.TryParse(_strings[2], out byte b2);
                    byte.TryParse(_strings[3], out byte b3);
                    destIp = new[] { b0, b1, b2, b3 };
                }
            }

            break;
    }

    index++;
}


if (!File.Exists(filename))
{
    Console.WriteLine("file {0} not exists", filename);
    return;
}

List<string> logins = new List<string>();

byte[] headerBuffer = new byte[20];
DateTime argTime = DateTime.ParseExact(datetime, "dd.MM.yyyy HH:mm", null);

ulong ts = (ulong)((DateTimeOffset)(argTime)).ToUnixTimeMilliseconds();
ulong ts2 = (ulong)((DateTimeOffset)(argTime.AddMinutes(10))).ToUnixTimeMilliseconds();

byte[] _dataBuffer = new byte[55];
using StreamReader sr = new StreamReader(filename);
sr.BaseStream.ReadExactly(headerBuffer);

while (sr.BaseStream.Read(_dataBuffer, 0, 55) == 55)
{
    PairRecord pr = new PairRecord(_dataBuffer);
    if (ts2 < BinaryPrimitives.ReadUInt64BigEndian(pr.SystemTimeEventStart)
        || ts > BinaryPrimitives.ReadUInt64BigEndian(pr.SystemTimeEventStart)) continue;
    if (destIp.Length == 4 && (pr.DestinationAddress[0] != destIp[0] 
                               || pr.DestinationAddress[1] != destIp[1] 
                               || pr.DestinationAddress[2] != destIp[2] 
                               || pr.DestinationAddress[3] != destIp[3])) continue;
    
    if (getUniqs)
    {
        string login = Encoding.ASCII.GetString(pr.Login).TrimEnd('\0');
        if (!logins.Contains(login))
            logins.Add(login);
    }
    else
        Console.WriteLine(pr.ToString());
}

foreach (var item in logins)
{
    Console.WriteLine(item);
}

class PairRecord
{
    public PairRecord(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer.ToArray();
    }

    private readonly byte[] _buffer;
    private Span<byte> span => _buffer;
    public Span<byte> SystemTimeEventStart => span.Slice(0, 8);
    public Span<byte> SystemTimeEventStop => span.Slice(8, 8);
    public Span<byte> ProtocolIdentifier => span.Slice(16, 1);
    public Span<byte> SourceAddress => span.Slice(17, 4);
    public Span<byte> DestinationAddress => span.Slice(21, 4);
    public Span<byte> SourcePort => span.Slice(25, 2);
    public Span<byte> PostNatSourcePort => span.Slice(27, 2);
    public Span<byte> DestinationPort => span.Slice(29, 2);
    public Span<byte> SessionId => span.Slice(31, 8);
    public Span<byte> Login => span.Slice(39, 16);

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        ulong ts = BinaryPrimitives.ReadUInt64BigEndian(SystemTimeEventStart);
        sb.Append($"Start: {dateTime.AddHours(3).AddMilliseconds(ts)} ({ts}) Stop: {BinaryPrimitives.ReadUInt64BigEndian(SystemTimeEventStop)},");
        sb.Append($"Protocol: {ProtocolIdentifier[0]},");
        sb.Append(
            $"Destination: {DestinationAddress[0]}.{DestinationAddress[1]}.{DestinationAddress[2]}.{DestinationAddress[3]}:{BinaryPrimitives.ReadUInt16BigEndian(DestinationPort)},,");
        sb.Append(
            $"Source: {SourceAddress[0]}.{SourceAddress[1]}.{SourceAddress[2]}.{SourceAddress[3]}:{BinaryPrimitives.ReadUInt16BigEndian(SourcePort)},");
        sb.Append($"Login: {Encoding.ASCII.GetString(Login).Trim()} PostNatSourcePort: {BinaryPrimitives.ReadUInt16BigEndian(PostNatSourcePort)}");
        return sb.ToString();
    }
}