using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.IoStore;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Core.Mods;

public sealed record SkinReplacement(SkinEntry Skin, RgbaImage Image);

/// <summary>
/// Builds the mod container: each chosen hero's original texture package (read from the user's own
/// game) with only its 16,384 pixel bytes replaced, plus a container header, as
/// zzz_MCD2SkinStudio_P.utoc/.ucas (encrypted with the game key) and an empty .pak.
/// </summary>
public static class ModBuilder
{
    public const string ModName = "zzz_MCD2SkinStudio_P";
    public const string FilePrefix = "zzz_MCD2SkinStudio";

    /// <summary>Builds the container, encrypted with the game's own key: Phase 0 showed the game
    /// ignores unencrypted mod containers on the Xbox build.</summary>
    public static Dictionary<string, byte[]> Build(GameArchive game, IReadOnlyList<SkinReplacement> items, string modName = ModName) =>
        Build(game, items, modName, game.Key);

    /// <summary>Test hook: <paramref name="encryptKey"/> null writes an unencrypted container.</summary>
    internal static Dictionary<string, byte[]> Build(GameArchive game, IReadOnlyList<SkinReplacement> items, string modName, byte[]? encryptKey)
    {
        if (items.Count == 0) throw new ArgumentException("No skins to install");
        if (!modName.StartsWith(FilePrefix, StringComparison.Ordinal)) throw new ArgumentException("Mod name must start with " + FilePrefix);

        var chunks = new List<ModChunk>();
        var packages = new List<(ulong, StoreEntry)>();
        foreach (var it in items)
        {
            var path = it.Skin.TexturePath;
            var id = game.ChunkIdOf(path);
            if (chunks.Any(c => c.ChunkId.AsSpan().SequenceEqual(id))) continue;
            var patched = TextureIO.PatchSkin(game.ReadPackage(path), it.Image);
            ulong pid = BinaryPrimitives.ReadUInt64LittleEndian(id);
            if (!game.Header.TryGetEntry(pid, out var entry))
                throw new InvalidDataException($"The game's package list has no entry for {it.Skin.DisplayName}.");
            chunks.Add(new ModChunk(id, patched, game.RelativePath(path)));
            packages.Add((pid, entry));
        }

        ulong containerId = BinaryPrimitives.ReadUInt64LittleEndian(SHA1.HashData(Encoding.ASCII.GetBytes(modName)));
        var header = ContainerHeader.Write(containerId, packages);
        var headerId = new byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(headerId, containerId);
        headerId[11] = IoStoreReader.ChunkTypeContainerHeader;
        chunks.Add(new ModChunk(headerId, header, null));   // last: its id is the container id

        var (utoc, ucas) = ContainerWriter.Write(chunks, encryptKey);
        return new Dictionary<string, byte[]>
        {
            [modName + ".utoc"] = utoc,
            [modName + ".ucas"] = ucas,
            [modName + ".pak"] = ContainerWriter.EmptyPak(),
        };
    }
}
