using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mcd2SkinStudio.Core.IoStore;

/// <summary>Little helpers for the byte layouts used by UE archives.</summary>
internal static class Bin
{
    public static int I32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadInt32LittleEndian(b[o..]);
    public static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
    public static ulong U64(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b[o..]);
    public static long I64(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadInt64LittleEndian(b[o..]);

    public static long BigEndian5(ReadOnlySpan<byte> b, int o)
    {
        long v = 0;
        for (int i = 0; i < 5; i++) v = (v << 8) | b[o + i];
        return v;
    }

    public static long LittleEndian(ReadOnlySpan<byte> b, int o, int n)
    {
        long v = 0;
        for (int i = n - 1; i >= 0; i--) v = (v << 8) | b[o + i];
        return v;
    }

    public static void WriteBigEndian5(Stream s, long v)
    {
        for (int i = 4; i >= 0; i--) s.WriteByte((byte)(v >> (8 * i)));
    }

    public static void WriteLittleEndian(Stream s, long v, int n)
    {
        for (int i = 0; i < n; i++) s.WriteByte((byte)(v >> (8 * i)));
    }

    public static int Align16(int n) => (n + 15) & ~15;

    /// <summary>Reads a UE FString (int32 length incl. null; negative = UTF-16).</summary>
    public static string FString(ReadOnlySpan<byte> b, ref int p)
    {
        int n = I32(b, p); p += 4;
        if (n == 0) return "";
        if (n < 0)
        {
            int bytes = -2 * n;
            if (bytes < 0 || p + bytes > b.Length) throw new InvalidDataException("bad FString");
            var s = Encoding.Unicode.GetString(b.Slice(p, bytes - 2)); p += bytes; return s;
        }
        if (p + n > b.Length) throw new InvalidDataException("bad FString");
        var a = Encoding.Latin1.GetString(b.Slice(p, n - 1)); p += n; return a;
    }

    public static byte[] FStringBytes(string s)
    {
        var e = Encoding.ASCII.GetBytes(s + "\0");
        var o = new byte[4 + e.Length];
        BinaryPrimitives.WriteInt32LittleEndian(o, e.Length);
        e.CopyTo(o, 4);
        return o;
    }

    public static byte[] AesDecrypt(byte[] key, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptEcb(data, PaddingMode.None);
    }

    public static byte[] AesEncrypt(byte[] key, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptEcb(data, PaddingMode.None);
    }

    public static byte[] Pad16(ReadOnlySpan<byte> d)
    {
        var o = new byte[Align16(d.Length)];
        d.CopyTo(o);
        return o;
    }
}
