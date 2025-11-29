namespace fixclean;

public class ByteArrayComparer : IEqualityComparer<byte[]> {
    public bool Equals(byte[] x, byte[] y)
    {
        return x.AsSpan().SequenceEqual(y.AsSpan());
    }
    public int GetHashCode(byte[] a)
    {
        uint b = 0;
        for (int i = 0; i < a.Length; i++)
            b = ((b << 23) | (b >> 9)) ^ a[i];
        return unchecked((int)b);
    }
}