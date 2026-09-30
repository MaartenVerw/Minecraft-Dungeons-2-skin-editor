namespace Mcd2SkinStudio.Core.IoStore;

/// <summary>
/// Read-only access to the game's legacy v11 .pak (loose non-asset files such as localization
/// and CSV/JSON tables). Opened with FileAccess.Read only.
/// </summary>
public sealed class PakReader : IDisposable
{
    public string MountPoint { get; private set; } = "";
    /// <summary>Full path (mount + path) → offset into the encoded entries blob.</summary>
    public IReadOnlyDictionary<string, int> Files => _files;

    readonly FileStream _f;
    readonly byte[] _key;
    byte[] _encoded = [];
    string[] _methods = [];
    readonly Dictionary<string, int> _files = new(StringComparer.Ordinal);

    PakReader(string path, byte[] key)
    {
        _f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        _key = key;
    }

    public static PakReader Open(string path, byte[] key)
    {
        var r = new PakReader(path, key);
        try { r.Parse(); return r; }
        catch { r.Dispose(); throw; }
    }

    byte[] ReadAt(long off, int size)
    {
        var b = new byte[size];
        _f.Seek(off, SeekOrigin.Begin);
        _f.ReadExactly(b);
        return b;
    }

    void Parse()
    {
        const int FooterSize = 221;   // v11: guid 16, encrypted 1, magic 4, version 4, offset 8, size 8, hash 20, 5×32 method names
        var foot = ReadAt(_f.Length - FooterSize, FooterSize);
        if (Bin.U32(foot, 17) != 0x5A6F12E1) throw new InvalidDataException("Not a v11 .pak");
        long io = Bin.I64(foot, 25), isz = Bin.I64(foot, 33);
        bool encIndex = foot[16] != 0;
        _methods = Enumerable.Range(0, 5).Select(i => System.Text.Encoding.ASCII.GetString(foot, 61 + 32 * i, 32).TrimEnd('\0')).ToArray();

        var prim = ReadIndex(io, (int)isz, encIndex);
        int p = 0;
        MountPoint = Bin.FString(prim, ref p);
        p += 4 + 8;                                   // entry count, path hash seed
        if (Bin.U32(prim, p) != 0) { p += 4 + 8 + 8 + 20; } else p += 4;
        if (Bin.U32(prim, p) == 0) throw new InvalidDataException("pak has no full directory index");
        p += 4;
        long fdo = Bin.I64(prim, p), fds = Bin.I64(prim, p + 8); p += 16 + 20;
        int encLen = Bin.I32(prim, p); p += 4;
        _encoded = prim.AsSpan(p, encLen).ToArray();

        var fdi = ReadIndex(fdo, (int)fds, encIndex);
        int q = 0;
        int nd = Bin.I32(fdi, q); q += 4;
        for (int i = 0; i < nd; i++)
        {
            var dir = Bin.FString(fdi, ref q);
            int nf = Bin.I32(fdi, q); q += 4;
            for (int j = 0; j < nf; j++)
            {
                var fn = Bin.FString(fdi, ref q);
                _files[MountPoint + dir + fn] = Bin.I32(fdi, q); q += 4;
            }
        }
    }

    byte[] ReadIndex(long off, int size, bool encrypted)
    {
        if (!encrypted) return ReadAt(off, size);
        return Bin.AesDecrypt(_key, ReadAt(off, Bin.Align16(size)));
    }

    public byte[] Read(string fullPath)
    {
        if (!_files.TryGetValue(fullPath, out int eoff)) throw new FileNotFoundException("Not in the .pak: " + fullPath);
        // FPakFile::DecodePakEntry
        var e = _encoded;
        int p = eoff;
        uint v = Bin.U32(e, p); p += 4;
        long cbs;
        if ((v & 0x3f) == 0x3f) { cbs = Bin.U32(e, p); p += 4; } else cbs = (v & 0x3f) << 11;
        int method = (int)((v >> 23) & 0x3f);
        long offset; if ((v & (1u << 31)) != 0) { offset = Bin.U32(e, p); p += 4; } else { offset = Bin.I64(e, p); p += 8; }
        long usize; if ((v & (1u << 30)) != 0) { usize = Bin.U32(e, p); p += 4; } else { usize = Bin.I64(e, p); p += 8; }
        long size = usize;
        if (method != 0) { if ((v & (1u << 29)) != 0) { size = Bin.U32(e, p); p += 4; } else { size = Bin.I64(e, p); p += 8; } }
        bool encrypted = (v & (1u << 22)) != 0;
        int nb = (int)((v >> 6) & 0xffff);
        var blockSizes = new List<long>();
        if (nb > 0 && (encrypted || nb != 1))
            for (int i = 0; i < nb; i++) { blockSizes.Add(Bin.U32(e, p)); p += 4; }
        else if (nb == 1) blockSizes.Add(size);

        // the serialized FPakEntry header in front of the data
        int header = 8 + 8 + 8 + 4 + 20 + (method != 0 ? 4 + 16 * nb : 0) + 1 + 4;
        long pos = offset + header;
        if (method == 0)
        {
            var raw = ReadAt(pos, encrypted ? Bin.Align16((int)size) : (int)size);
            if (encrypted) raw = Bin.AesDecrypt(_key, raw);
            return raw.AsSpan(0, (int)size).ToArray();
        }
        string name = method - 1 < _methods.Length ? _methods[method - 1] : "?";
        if (!name.Equals("Oodle", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("pak compression " + name);
        var outBuf = new byte[usize];
        long written = 0;
        foreach (var cs in blockSizes)
        {
            int disk = encrypted ? Bin.Align16((int)cs) : (int)cs;
            var raw = ReadAt(pos, disk);
            pos += disk;
            if (encrypted) raw = Bin.AesDecrypt(_key, raw);
            int us = (int)Math.Min(cbs > 0 ? cbs : usize, usize - written);
            int got = OodleSharp.OodleDecompressor.Decompress(raw.AsSpan(0, (int)cs), outBuf.AsSpan((int)written, us));
            if (got != us) throw new InvalidDataException("Oodle decompression failed in .pak");
            written += us;
        }
        return outBuf;
    }

    public void Dispose() => _f.Dispose();
}
