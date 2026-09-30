using System.Buffers.Binary;
using System.Security.Cryptography;
using Mcd2SkinStudio.Core.IoStore;

namespace Mcd2SkinStudio.Tests;

/// <summary>Write mod containers with synthetic data and read them back with the same reader the
/// app uses on the game's own files.</summary>
public class ContainerTests
{
    static byte[] ChunkId(ulong pkg, byte type)
    {
        var id = new byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(id, pkg);
        id[11] = type;
        return id;
    }

    static (string Utoc, List<ModChunk> Chunks) WriteTemp(byte[]? key, int chunkSize = 20_000)
    {
        var rnd = new Random(1);
        var chunks = new List<ModChunk>();
        for (int i = 0; i < 5; i++)
        {
            var data = new byte[chunkSize + i * 70_001];   // some chunks span several 64 KiB blocks
            rnd.NextBytes(data);
            chunks.Add(new ModChunk(ChunkId(0x1000UL + (ulong)i * 7919, 1), data, $"Dungeons/Content/Test/Folder{i % 2}/T_{i}.uasset"));
        }
        var header = ContainerHeader.Write(0xABCDEF, chunks.Select(c => (BinaryPrimitives.ReadUInt64LittleEndian(c.ChunkId), new StoreEntry(new byte[16], new byte[20]))).ToList());
        chunks.Add(new ModChunk(ChunkId(0xABCDEF, IoStoreReader.ChunkTypeContainerHeader), header, null));
        var (utoc, ucas) = ContainerWriter.Write(chunks, key);
        var dir = TestData.NewDir("container");
        var p = Path.Combine(dir, "zzz_Test_P.utoc");
        File.WriteAllBytes(p, utoc);
        File.WriteAllBytes(Path.ChangeExtension(p, ".ucas"), ucas);
        return (p, chunks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Container_round_trips(bool encrypted)
    {
        var key = encrypted ? RandomNumberGenerator.GetBytes(32) : null;
        var (utoc, chunks) = WriteTemp(key);
        using var r = IoStoreReader.Open(utoc, key);
        Assert.Equal(encrypted, r.Encrypted);
        Assert.Equal("../../../", r.MountPoint);
        Assert.Equal(5, r.Files.Count);
        foreach (var c in chunks.Where(c => c.Path != null))
        {
            int i = r.Files["../../../" + c.Path];
            Assert.Equal(c.ChunkId, r.ChunkId(i));
            Assert.Equal(c.Data, r.ReadChunk(i));
        }
        var h = ContainerHeader.Parse(r.ReadChunk(r.FindChunkOfType(IoStoreReader.ChunkTypeContainerHeader)));
        Assert.Equal(0xABCDEFUL, h.ContainerId);
        Assert.Equal(0xABCDEFUL, r.ContainerId);
        Assert.True(h.TryGetEntry(0x1000, out var e));
        Assert.Equal(2, e.ImportCount);
        Assert.Equal(1, e.ShaderMapCount);
    }

    [Fact]
    public void Encrypted_container_needs_the_right_key()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var (utoc, _) = WriteTemp(key);
        var bytes = File.ReadAllBytes(utoc);
        Assert.True(IoStoreReader.KeyOpensIndex(bytes, key));
        Assert.False(IoStoreReader.KeyOpensIndex(bytes, RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<WrongKeyException>(() => IoStoreReader.Open(utoc, RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<KeyRequiredException>(() => IoStoreReader.Open(utoc, null));
    }

    [Fact]
    public void Perfect_hash_finds_every_chunk_like_the_engine()
    {
        var (utoc, chunks) = WriteTemp(null);
        var b = File.ReadAllBytes(utoc);
        int n = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(24));
        int nseeds = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x54));
        int seedsAt = 0x90 + 22 * n;
        for (int i = 0; i < chunks.Count; i++)
        {
            var id = chunks[i].ChunkId;
            int seed = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(seedsAt + 4 * (int)(ContainerWriter.ChunkHash(0, id) % (ulong)nseeds)));
            Assert.NotEqual(0, seed);
            int slot = seed < 0 ? -seed - 1 : (int)(ContainerWriter.ChunkHash((ulong)(long)seed, id) % (ulong)n);
            Assert.Equal(i, slot);
        }
    }

    [Fact]
    public void Chunk_hash_matches_fnv1_reference()
    {
        // FNV-1 64 (multiply, then xor, as UE does) of empty input is the offset basis
        Assert.Equal(0xCBF29CE484222325UL, ContainerWriter.ChunkHash(0, []));
        Assert.Equal(0xAF63BD4C8601B7BEUL, ContainerWriter.ChunkHash(0, "a"u8));
    }

    [Fact]
    public void Empty_pak_has_v11_footer()
    {
        var pak = ContainerWriter.EmptyPak();
        var foot = pak.AsSpan(pak.Length - 221);
        Assert.Equal(0x5A6F12E1u, BinaryPrimitives.ReadUInt32LittleEndian(foot[17..]));
        Assert.Equal(11, BinaryPrimitives.ReadInt32LittleEndian(foot[21..]));
    }
}
