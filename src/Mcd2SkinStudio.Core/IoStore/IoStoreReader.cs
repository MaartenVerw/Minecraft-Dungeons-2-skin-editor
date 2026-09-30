using System.Text;

namespace Mcd2SkinStudio.Core.IoStore;

/// <summary>
/// Read-only access to a UE5 IoStore container (.utoc + .ucas). Opens the files with
/// FileShare.ReadWrite and FileAccess.Read only: the game's files are never written.
/// </summary>
public sealed class IoStoreReader : IDisposable
{
    public const string TocMagic = "-==--==--==--==-";
    public const byte FlagCompressed = 1, FlagEncrypted = 2, FlagSigned = 4, FlagIndexed = 8;
    public const byte ChunkTypeExportBundleData = 1, ChunkTypeContainerHeader = 6;

    public string UtocPath { get; }
    public byte Version { get; private set; }
    public int EntryCount { get; private set; }
    public int BlockSize { get; private set; }
    public ulong ContainerId { get; private set; }
    public byte Flags { get; private set; }
    public bool Encrypted => (Flags & FlagEncrypted) != 0;
    public string MountPoint { get; private set; } = "";
    public IReadOnlyList<string> CompressionMethods => _methods;

    /// <summary>Full path (mount point + path) → TOC entry index.</summary>
    public IReadOnlyDictionary<string, int> Files => _files;

    readonly byte[]? _key;
    byte[][] _ids = [];
    long[] _offsets = [], _lengths = [];
    long[] _blockOffset = [];
    int[] _blockCSize = [], _blockUSize = [];
    byte[] _blockMethod = [];
    string[] _methods = [];
    ulong _partitionSize;
    readonly Dictionary<string, int> _files = new(StringComparer.Ordinal);
    readonly Dictionary<int, FileStream> _ucas = new();

    IoStoreReader(string utocPath, byte[]? key)
    {
        UtocPath = utocPath;
        _key = key;
    }

    public static IoStoreReader Open(string utocPath, byte[]? key)
    {
        var r = new IoStoreReader(utocPath, key);
        r.Parse(ReadShared(utocPath));
        return r;
    }

    public static byte[] ReadShared(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var b = new byte[f.Length];
        f.ReadExactly(b);
        return b;
    }

    /// <summary>Header fields needed to find the directory index without parsing everything.</summary>
    public static (int Offset, int Size, byte Flags) LocateDirectoryIndex(ReadOnlySpan<byte> b)
    {
        if (b.Length < 0x90 || Encoding.ASCII.GetString(b[..16]) != TocMagic) throw new InvalidDataException("Not a .utoc file");
        int hs = Bin.I32(b, 20), n = Bin.I32(b, 24), nblk = Bin.I32(b, 28), bes = Bin.I32(b, 32);
        int nmeth = Bin.I32(b, 36), mlen = Bin.I32(b, 40), dsize = Bin.I32(b, 48);
        byte version = b[16];
        int seeds = version >= 4 ? Bin.I32(b, 0x54) : 0;
        int nohash = version >= 5 ? Bin.I32(b, 0x60) : 0;
        long p = hs + 12L * n + 10L * n + 4L * seeds + 4L * nohash + (long)bes * nblk + (long)mlen * nmeth;
        return ((int)p, dsize, b[0x50]);
    }

    /// <summary>True when <paramref name="key"/> decrypts the directory index to the expected mount point.</summary>
    public static bool KeyOpensIndex(ReadOnlySpan<byte> utoc, byte[] key)
    {
        var (off, size, flags) = LocateDirectoryIndex(utoc);
        if ((flags & FlagEncrypted) == 0) return true;
        if (size < 16 || off + 16 > utoc.Length) return false;
        var first = Bin.AesDecrypt(key, utoc.Slice(off, 16));
        ReadOnlySpan<byte> expect = [0x0A, 0, 0, 0, (byte)'.', (byte)'.', (byte)'/', (byte)'.', (byte)'.', (byte)'/', (byte)'.', (byte)'.', (byte)'/', 0];
        return first.AsSpan(0, expect.Length).SequenceEqual(expect);
    }

    void Parse(byte[] b)
    {
        if (b.Length < 0x90 || Encoding.ASCII.GetString(b, 0, 16) != TocMagic) throw new InvalidDataException("Not a .utoc file");
        Version = b[16];
        int hs = Bin.I32(b, 20);
        EntryCount = Bin.I32(b, 24);
        int nblk = Bin.I32(b, 28), bes = Bin.I32(b, 32), nmeth = Bin.I32(b, 36), mlen = Bin.I32(b, 40);
        BlockSize = Bin.I32(b, 44);
        int dsize = Bin.I32(b, 48);
        ContainerId = Bin.U64(b, 0x38);
        Flags = b[0x50];
        int seeds = Version >= 4 ? Bin.I32(b, 0x54) : 0;
        _partitionSize = Version >= 3 ? Bin.U64(b, 0x58) : ulong.MaxValue;
        if (_partitionSize == 0) _partitionSize = ulong.MaxValue;
        int nohash = Version >= 5 ? Bin.I32(b, 0x60) : 0;
        if (bes != 12) throw new InvalidDataException($"Unsupported compression block entry size {bes}");

        int n = EntryCount, p = hs;
        _ids = new byte[n][];
        for (int i = 0; i < n; i++) _ids[i] = b.AsSpan(p + 12 * i, 12).ToArray();
        p += 12 * n;
        _offsets = new long[n]; _lengths = new long[n];
        for (int i = 0; i < n; i++)
        {
            _offsets[i] = Bin.BigEndian5(b, p + 10 * i);
            _lengths[i] = Bin.BigEndian5(b, p + 10 * i + 5);
        }
        p += 10 * n;
        p += 4 * seeds + 4 * nohash;
        _blockOffset = new long[nblk]; _blockCSize = new int[nblk]; _blockUSize = new int[nblk]; _blockMethod = new byte[nblk];
        for (int i = 0; i < nblk; i++)
        {
            int e = p + 12 * i;
            _blockOffset[i] = Bin.LittleEndian(b, e, 5);
            _blockCSize[i] = (int)Bin.LittleEndian(b, e + 5, 3);
            _blockUSize[i] = (int)Bin.LittleEndian(b, e + 8, 3);
            _blockMethod[i] = b[e + 11];
        }
        p += 12 * nblk;
        _methods = new string[nmeth];
        for (int i = 0; i < nmeth; i++) _methods[i] = Encoding.ASCII.GetString(b, p + mlen * i, mlen).TrimEnd('\0');
        p += mlen * nmeth;

        if ((Flags & FlagIndexed) != 0 && dsize > 0)
        {
            byte[] d = b.AsSpan(p, dsize).ToArray();
            if (Encrypted)
            {
                if (_key == null) throw new KeyRequiredException();
                if (!KeyOpensIndex(b, _key)) throw new WrongKeyException();
                d = Bin.AesDecrypt(_key, d);
            }
            ReadDirectoryIndex(d);
        }
    }

    void ReadDirectoryIndex(byte[] d)
    {
        int p = 0;
        MountPoint = Bin.FString(d, ref p);
        int nd = Bin.I32(d, p); p += 4;
        var dirs = new uint[nd, 4];
        for (int i = 0; i < nd; i++) for (int k = 0; k < 4; k++) dirs[i, k] = Bin.U32(d, p + 16 * i + 4 * k);
        p += 16 * nd;
        int nf = Bin.I32(d, p); p += 4;
        var files = new uint[nf, 3];
        for (int i = 0; i < nf; i++) for (int k = 0; k < 3; k++) files[i, k] = Bin.U32(d, p + 12 * i + 4 * k);
        p += 12 * nf;
        int ns = Bin.I32(d, p); p += 4;
        var strs = new string[ns];
        for (int i = 0; i < ns; i++) strs[i] = Bin.FString(d, ref p);

        const uint None = 0xFFFFFFFF;
        // iterative walk: (dir index, path prefix)
        var stack = new Stack<(uint Dir, string Path)>();
        if (nd > 0) stack.Push((0, ""));
        while (stack.Count > 0)
        {
            var (di, path) = stack.Pop();
            if (dirs[di, 0] != None) path = path + strs[dirs[di, 0]] + "/";
            for (uint f = dirs[di, 3]; f != None; f = files[f, 1])
                _files[MountPoint + path + strs[files[f, 0]]] = (int)files[f, 2];
            for (uint c = dirs[di, 1]; c != None; c = dirs[c, 2])
                stack.Push((c, path));
        }
    }

    public byte[] ChunkId(int index) => _ids[index];
    public long ChunkLength(int index) => _lengths[index];

    /// <summary>First entry whose chunk type byte matches, or -1.</summary>
    public int FindChunkOfType(byte type)
    {
        for (int i = 0; i < _ids.Length; i++) if (_ids[i][11] == type) return i;
        return -1;
    }

    /// <summary>Reads, decrypts and decompresses one chunk.</summary>
    public byte[] ReadChunk(int index)
    {
        long off = _offsets[index], len = _lengths[index];
        if (len == 0) return [];
        int first = (int)(off / BlockSize), last = (int)((off + len - 1) / BlockSize);
        var outBuf = new byte[(long)(last - first + 1) * BlockSize];
        int written = 0;
        for (int bi = first; bi <= last; bi++)
        {
            int cs = _blockCSize[bi], us = _blockUSize[bi];
            int readSize = Encrypted ? Bin.Align16(cs) : cs;
            var raw = ReadUcas(_blockOffset[bi], readSize);
            if (Encrypted)
            {
                if (_key == null) throw new KeyRequiredException();
                raw = Bin.AesDecrypt(_key, raw);
            }
            var src = raw.AsSpan(0, cs);
            var dst = outBuf.AsSpan(written, us);
            byte m = _blockMethod[bi];
            if (m == 0)
            {
                src[..us].CopyTo(dst);
            }
            else
            {
                string name = m - 1 < _methods.Length ? _methods[m - 1] : "?";
                if (!name.Equals("Oodle", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException($"Compression method '{name}' is not supported");
                int got = OodleSharp.OodleDecompressor.Decompress(src, dst);
                if (got != us) throw new InvalidDataException($"Oodle decompression failed on block {bi} ({got} of {us} bytes)");
            }
            written += us;
        }
        int start = (int)(off - (long)first * BlockSize);
        return outBuf.AsSpan(start, (int)len).ToArray();
    }

    byte[] ReadUcas(long offset, int size)
    {
        int part = _partitionSize == ulong.MaxValue ? 0 : (int)((ulong)offset / _partitionSize);
        long local = part == 0 ? offset : (long)((ulong)offset % _partitionSize);
        if (!_ucas.TryGetValue(part, out var fs))
        {
            string basePath = Path.ChangeExtension(UtocPath, null);
            string path = part == 0 ? basePath + ".ucas" : $"{basePath}_s{part}.ucas";
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
            _ucas[part] = fs;
        }
        var buf = new byte[size];
        fs.Seek(local, SeekOrigin.Begin);
        fs.ReadExactly(buf);
        return buf;
    }

    public void Dispose()
    {
        foreach (var f in _ucas.Values) f.Dispose();
        _ucas.Clear();
    }
}

public sealed class KeyRequiredException() : Exception("This archive is encrypted and needs a key.");
public sealed class WrongKeyException() : Exception("The archive key does not match this game version.");
