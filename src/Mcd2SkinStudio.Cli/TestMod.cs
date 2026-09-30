using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.IoStore;
using Mcd2SkinStudio.Core.Mods;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Cli;

/// <summary>
/// Phase 0 (d): Tank's chest bright red in an unencrypted container (the blueprint's plan) and
/// Steve's chest bright blue in an encrypted one (what the Steam reference app ships), so one
/// game launch shows which variant the Xbox build loads.
/// </summary>
static class TestMod
{
    public static Dictionary<string, byte[]> Build(GameArchive a, byte[] key)
    {
        var files = new Dictionary<string, byte[]>();
        void Add(string skinName, uint colour, string modName, byte[]? encKey)
        {
            var s = a.Catalog.Find(skinName) ?? a.Catalog.Skins.First(x => x.Hero.Equals(skinName, StringComparison.OrdinalIgnoreCase));
            var img = TextureIO.ExtractSkin(a.ReadPackage(s.TexturePath));
            img.FillRect(20, 20, 8, 12, colour);   // body front
            var built = ModBuilder.Build(a, [new SkinReplacement(s, img)], modName, encKey);
            Verify(a, built, modName, encKey, s, img);
            foreach (var kv in built) files[kv.Key] = kv.Value;
            Console.WriteLine($"  {s.DisplayName}: chest {(colour == Red ? "red" : "blue")}, container {modName} ({(encKey == null ? "unencrypted" : "encrypted")})");
        }
        Add("Tank", Red, ModBuilder.ModName, null);
        Add("Steve", Blue, ModBuilder.FilePrefix + "_Enc_P", key);
        return files;
    }

    static readonly uint Red = RgbaImage.Pack(255, 0, 0, 255), Blue = RgbaImage.Pack(0, 64, 255, 255);

    /// <summary>Reads the container back with our own reader before anything goes near the game.</summary>
    public static void Verify(GameArchive a, Dictionary<string, byte[]> files, string modName, byte[]? key, SkinEntry s, RgbaImage expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcd2skin-verify-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (n, d) in files) File.WriteAllBytes(Path.Combine(dir, n), d);
            using var r = IoStoreReader.Open(Path.Combine(dir, modName + ".utoc"), key);
            var full = ContainerWriter.MountPoint + a.RelativePath(s.TexturePath);
            if (!r.Files.TryGetValue(full, out int i)) throw new Exception("verify: path missing in directory index");
            var pkg = r.ReadChunk(i);
            if (!TextureIO.ExtractSkin(pkg).SameAs(expected)) throw new Exception("verify: pixels differ");
            if (!r.ChunkId(i).AsSpan().SequenceEqual(a.ChunkIdOf(s.TexturePath))) throw new Exception("verify: chunk id differs");
            var h = ContainerHeader.Parse(r.ReadChunk(r.FindChunkOfType(IoStoreReader.ChunkTypeContainerHeader)));
            if (h.ContainerId != r.ContainerId) throw new Exception("verify: container id mismatch");
            // perfect hash lookup exactly as the engine does it
            var id = r.ChunkId(i);
            int nseeds = BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(dir, modName + ".utoc")), 0x54);
            var utoc = File.ReadAllBytes(Path.Combine(dir, modName + ".utoc"));
            int seedsAt = 0x90 + 22 * r.EntryCount;
            int seed = BitConverter.ToInt32(utoc, seedsAt + 4 * (int)(ContainerWriter.ChunkHash(0, id) % (ulong)nseeds));
            int slot = seed < 0 ? -seed - 1 : (int)(ContainerWriter.ChunkHash((ulong)(long)seed, id) % (ulong)r.EntryCount);
            if (slot != i) throw new Exception("verify: perfect hash lookup failed");
        }
        finally { Directory.Delete(dir, true); }
    }
}
