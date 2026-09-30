using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mcd2SkinStudio.Core.IoStore;

/// <summary>One chunk to put in a mod container. <paramref name="Path"/> is relative to the mount point
/// (e.g. "Dungeons/Content/.../T_Tank_Skin.uasset"), or null for chunks without a file (the header).</summary>
public sealed record ModChunk(byte[] ChunkId, byte[] Data, string? Path);

/// <summary>
/// Writes a mod IoStore container: TOC v8, uncompressed, Indexed (optionally Encrypted with the
/// game key), with a perfect-hash seed table, plus the empty v11 .pak UE needs beside it.
/// </summary>
public static class ContainerWriter
{
    public const int BlockSize = 0x10000;
    public const string MountPoint = "../../../";

    public static (byte[] Utoc, byte[] Ucas) Write(IReadOnlyList<ModChunk> chunks, byte[]? encryptKey = null)
    {
        if (chunks.Count == 0) throw new ArgumentException("No chunks");
        bool encrypt = encryptKey != null;
        var ucas = new MemoryStream();
        var offLen = new List<(long Off, long Len)>();
        var blocks = new List<(long Off, int CSize, int USize)>();
        long uoff = 0;
        foreach (var c in chunks)
        {
            if (c.Data.Length == 0) throw new ArgumentException("Empty chunk");
            offLen.Add((uoff, c.Data.Length));
            for (int i = 0; i < c.Data.Length; i += BlockSize)
            {
                int n = Math.Min(BlockSize, c.Data.Length - i);
                var part = c.Data.AsSpan(i, n);
                blocks.Add((ucas.Length, n, n));
                ucas.Write(encrypt ? Bin.AesEncrypt(encryptKey!, Bin.Pad16(part)) : part);
            }
            uoff += (c.Data.Length + BlockSize - 1) / BlockSize * (long)BlockSize;
        }

        var paths = chunks.Select((c, i) => (c.Path, i)).Where(t => t.Path != null).Select(t => (t.Path!, t.i)).ToList();
        var di = DirectoryIndex(MountPoint, paths);
        if (encrypt) di = Bin.AesEncrypt(encryptKey!, Bin.Pad16(di));

        // Perfect-hash table (the engine won't mount a v8 TOC without one): pick a seed-table size
        // where every chunk lands in its own bucket, then point each bucket at its slot with a negative seed.
        int count = chunks.Count, nseeds = count;
        while (chunks.Select(c => ChunkHash(0, c.ChunkId) % (ulong)nseeds).Distinct().Count() < count) nseeds++;
        var seeds = new int[nseeds];
        for (int i = 0; i < count; i++) seeds[(int)(ChunkHash(0, chunks[i].ChunkId) % (ulong)nseeds)] = -i - 1;

        var hdr = new byte[0x90];
        Encoding.ASCII.GetBytes(IoStoreReader.TocMagic).CopyTo(hdr, 0);
        hdr[16] = 8; // version: ReplaceIoChunkHashWithIoHash
        int[] fields = [0x90, count, blocks.Count, 12, 0, 32, BlockSize, di.Length, 1];
        for (int i = 0; i < fields.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(hdr.AsSpan(20 + 4 * i), fields[i]);
        chunks[^1].ChunkId.AsSpan(0, 8).CopyTo(hdr.AsSpan(0x38));   // container id = last chunk's package id
        hdr[0x50] = (byte)(IoStoreReader.FlagIndexed | (encrypt ? IoStoreReader.FlagEncrypted : 0));
        BinaryPrimitives.WriteInt32LittleEndian(hdr.AsSpan(0x54), nseeds);
        BinaryPrimitives.WriteUInt64LittleEndian(hdr.AsSpan(0x58), ulong.MaxValue);   // partition size
        BinaryPrimitives.WriteInt32LittleEndian(hdr.AsSpan(0x60), 0);                 // chunks without perfect hash

        var t = new MemoryStream();
        t.Write(hdr);
        foreach (var c in chunks) t.Write(c.ChunkId);
        foreach (var (off, len) in offLen) { Bin.WriteBigEndian5(t, off); Bin.WriteBigEndian5(t, len); }
        foreach (var s in seeds) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, s); t.Write(b); }
        foreach (var (off, cs, us) in blocks)
        {
            Bin.WriteLittleEndian(t, off, 5); Bin.WriteLittleEndian(t, cs, 3); Bin.WriteLittleEndian(t, us, 3); t.WriteByte(0);
        }
        t.Write(di);
        // entry metas: 20-byte hash + flags + 3 pad (the hash is informational for unsigned containers)
        foreach (var c in chunks) { t.Write(SHA1.HashData(c.Data)); t.Write(new byte[4]); }
        return (t.ToArray(), ucas.ToArray());
    }

    /// <summary>FIoStoreTocResource::HashChunkIdWithSeed (FNV-1a style, 64-bit).</summary>
    public static ulong ChunkHash(ulong seed, ReadOnlySpan<byte> id)
    {
        ulong x = seed != 0 ? seed : 0xCBF29CE484222325UL;
        foreach (var b in id) x = unchecked(x * 0x100000001B3UL) ^ b;
        return x;
    }

    /// <summary>FIoDirectoryIndexResource for the given (relative path, toc index) pairs.</summary>
    public static byte[] DirectoryIndex(string mount, IReadOnlyList<(string Path, int Index)> paths)
    {
        const uint None = 0xFFFFFFFF;
        var strings = new List<string>();
        var sidx = new Dictionary<string, int>(StringComparer.Ordinal);
        int S(string x)
        {
            if (!sidx.TryGetValue(x, out int v)) { v = strings.Count; sidx[x] = v; strings.Add(x); }
            return v;
        }
        var dirs = new List<uint[]> { new[] { None, None, None, None } };   // name, first child, sibling, first file
        var files = new List<uint[]>();                                     // name, next file, toc index
        int Child(int di, string name)
        {
            for (uint c = dirs[di][1]; c != None; c = dirs[(int)c][2])
                if (strings[(int)dirs[(int)c][0]] == name) return (int)c;
            dirs.Add([(uint)S(name), None, dirs[di][1], None]);
            dirs[di][1] = (uint)(dirs.Count - 1);
            return dirs.Count - 1;
        }
        foreach (var (path, index) in paths)
        {
            var parts = path.Split('/');
            int d = 0;
            for (int i = 0; i < parts.Length - 1; i++) d = Child(d, parts[i]);
            files.Add([(uint)S(parts[^1]), dirs[d][3], (uint)index]);
            dirs[d][3] = (uint)(files.Count - 1);
        }
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(Bin.FStringBytes(mount));
        w.Write(dirs.Count); foreach (var x in dirs) foreach (var v in x) w.Write(v);
        w.Write(files.Count); foreach (var x in files) foreach (var v in x) w.Write(v);
        w.Write(strings.Count); foreach (var x in strings) w.Write(Bin.FStringBytes(x));
        return m.ToArray();
    }

    /// <summary>A v11 .pak with no files: UE only mounts a .utoc/.ucas pair that has a .pak beside it.</summary>
    public static byte[] EmptyPak()
    {
        var fdi = new MemoryStream();
        var fw = new BinaryWriter(fdi);
        fw.Write(1); fw.Write(Bin.FStringBytes("/")); fw.Write(0);
        var phi = new byte[8];
        int primLen = Bin.FStringBytes(MountPoint).Length + 4 + 8 + 4 + 16 + 20 + 4 + 16 + 20 + 4 + 4;
        long phiOff = primLen, fdiOff = phiOff + phi.Length;
        var pm = new MemoryStream();
        var pw = new BinaryWriter(pm);
        pw.Write(Bin.FStringBytes(MountPoint)); pw.Write(0); pw.Write(0UL);
        pw.Write(1); pw.Write(phiOff); pw.Write((long)phi.Length); pw.Write(SHA1.HashData(phi));
        pw.Write(1); pw.Write(fdiOff); pw.Write(fdi.Length); pw.Write(SHA1.HashData(fdi.ToArray()));
        pw.Write(0); pw.Write(0);
        var prim = pm.ToArray();
        if (prim.Length != primLen) throw new InvalidOperationException("pak index size mismatch");
        var o = new MemoryStream();
        var w = new BinaryWriter(o);
        w.Write(prim); w.Write(phi); w.Write(fdi.ToArray());
        w.Write(new byte[16]); w.Write((byte)0); w.Write(0x5A6F12E1); w.Write(11); w.Write(0L); w.Write((long)prim.Length);
        w.Write(SHA1.HashData(prim)); w.Write(new byte[160]);
        return o.ToArray();
    }
}
