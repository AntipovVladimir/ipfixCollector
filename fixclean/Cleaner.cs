using System.Buffers.Binary;
using System.Text;
// ReSharper disable UseStringInterpolation

namespace fixclean;

internal class Cleaner
{
    public void Run(string inputFileName)
    {
        if (!File.Exists(inputFileName))
            return;
        Console.WriteLine("cleaning {0}...", inputFileName);
        using StreamReader sr = new StreamReader(inputFileName);
        byte[] header = new byte[20];
        try
        {
            sr.BaseStream.ReadExactly(header, 0, 20);
            ReadOnlySpan<byte> sh = header;
            byte[] sheader = Encoding.ASCII.GetBytes("FLEXRAWNATLOG");
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(sh.Slice(13, 2));
            bool proceedrename = false;
            if (sh.Slice(0, 13).SequenceEqual(sheader) && version == 1)
            {
                ReadOnlySpan<byte> postNatSourceAddress = sh.Slice(15, 4);
                byte[] buffer = new byte[55];
                HashSet<byte[]> uniq = new HashSet<byte[]>(new ByteArrayComparer());
                int totalcnt = 0;
                while (sr.BaseStream.Read(buffer, 0, 55) == 55)
                {
                    totalcnt++;
                    uniq.Add(buffer.ToArray());
                }
                Console.WriteLine("Total records read: {0}, uniq records: {1}", totalcnt, uniq.Count);
                if (totalcnt != uniq.Count)
                {
                    string outputFileName = string.Format("{0}.cln", inputFileName);
                    using StreamWriter sw = new StreamWriter(outputFileName, append:false);
                    sw.BaseStream.Write(header);
                    int todo = uniq.Count;
                    HashSet<byte[]>.Enumerator enumerator = uniq.GetEnumerator();
                    while (todo > 0)
                    {
                        sw.BaseStream.Write(enumerator.Current);
                        enumerator.MoveNext();
                        todo--;
                    }
                    enumerator.Dispose();
                    sw.Close();
                    uniq.Clear();
                    proceedrename = true;
                }
                else
                {
                    Console.WriteLine("nothing to clean, done.");
                }
            }
            else
            {
                Console.WriteLine("wrong header");
            }
            sr.Close();
            if (proceedrename)
            {
                string oldFile = string.Format("{0}.old", inputFileName);
                File.Move(inputFileName, oldFile);
                File.Move(string.Format("{0}.cln", inputFileName),inputFileName);
                File.Delete(oldFile);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
