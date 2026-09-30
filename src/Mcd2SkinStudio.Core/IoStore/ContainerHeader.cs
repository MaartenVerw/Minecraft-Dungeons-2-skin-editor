using System.Buffers.Binary;

namespace Mcd2SkinStudio.Core.IoStore;

/// <summary>A package's store entry: the packages it imports and its shader map hashes.</summary>
public sealed record StoreEntry(byte[] Imports, byte[] ShaderMapHashes)
{
    public int ImportCount => Imports.Length / 8;
    public int ShaderMapCount => ShaderMapHashes.Length / 20;
}

/// <summary>
/// FIoContainerHeader (chunk type 6). Reads the game's header to learn each package's store
/// entry, and writes the small header a mod container needs.
/// </summary>
public sealed class ContainerHeader
{
    public const uint Magic = 0x496F436E;
    public const uint SupportedVersion = 5;

    public uint Version { get; }
    public ulong ContainerId { get; }
    public int PackageCount => _entries.Count;
    /// <summary>Everything after the store entries, kept for diagnostics.</summary>
    public byte[] Tail { get; }

    readonly Dictionary<ulong, StoreEntry> _entries;

    ContainerHeader(uint version, ulong containerId, Dictionary<ulong, StoreEntry> entries, byte[] tail)
    {
        Version = version; ContainerId = containerId; _entries = entries; Tail = tail;
    }

    public bool TryGetEntry(ulong packageId, out StoreEntry entry) => _entries.TryGetValue(packageId, out entry!);

    public static ContainerHeader Parse(ReadOnlySpan<byte> b)
    {
        if (Bin.U32(b, 0) != Magic) throw new InvalidDataException("Not an IoStore container header");
        uint version = Bin.U32(b, 4);
        if (version != SupportedVersion) throw new InvalidDataException($"Container header version {version} is not supported (expected {SupportedVersion})");
        int p = 16;
        int n = Bin.I32(b, p); p += 4;
        var ids = new ulong[n];
        for (int i = 0; i < n; i++) ids[i] = Bin.U64(b, p + 8 * i);
        p += 8 * n;
        int storeLen = Bin.I32(b, p); p += 4;
        int eb = p;
        var entries = new Dictionary<ulong, StoreEntry>(n);
        for (int i = 0; i < n; i++)
        {
            int e = eb + 16 * i;
            int ni = Bin.I32(b, e), oi = Bin.I32(b, e + 4), ns = Bin.I32(b, e + 8), os = Bin.I32(b, e + 12);
            var imports = ni > 0 ? b.Slice(e + oi, 8 * ni).ToArray() : [];
            var shaders = ns > 0 ? b.Slice(e + 8 + os, 20 * ns).ToArray() : [];
            entries[ids[i]] = new StoreEntry(imports, shaders);
        }
        return new ContainerHeader(version, Bin.U64(b, 8), entries, b[(eb + storeLen)..].ToArray());
    }

    /// <summary>Writes a v5 container header for the given packages, in order.</summary>
    public static byte[] Write(ulong containerId, IReadOnlyList<(ulong PackageId, StoreEntry Entry)> packages)
    {
        int k = packages.Count;
        var blob = new byte[16 * k];
        var tail = new MemoryStream();
        for (int i = 0; i < k; i++)
        {
            var en = packages[i].Entry;
            int e = 16 * i;
            int io = 16 * k + (int)tail.Length; tail.Write(en.Imports);
            int so = 16 * k + (int)tail.Length; tail.Write(en.ShaderMapHashes);
            // array offsets are relative to each (count, offset) pair's own start
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(e), en.ImportCount);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(e + 4), en.ImportCount > 0 ? io - e : 0);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(e + 8), en.ShaderMapCount);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(e + 12), en.ShaderMapCount > 0 ? so - (e + 8) : 0);
        }
        var store = blob.Concat(tail.ToArray()).ToArray();

        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(Magic); w.Write(SupportedVersion); w.Write(containerId);
        w.Write(k); foreach (var p in packages) w.Write(p.PackageId);
        w.Write(store.Length); w.Write(store);
        w.Write(0); w.Write(0);   // optional segment package ids / store entries
        w.Write(0);               // redirects name batch (empty)
        w.Write(0);               // localized packages
        w.Write(0);               // package redirects
        long off = m.Length + 16;
        w.Write(off); w.Write(4L); w.Write(0);   // soft package references: none
        return m.ToArray();
    }
}
