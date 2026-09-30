using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.IoStore;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Core;

/// <summary>The game's main container opened read-only with a working key: skin catalog + package reads.</summary>
public sealed class GameArchive : IDisposable
{
    public GameInstall Install { get; }
    public IoStoreReader Reader { get; }
    public ContainerHeader Header { get; }
    public SkinCatalog Catalog { get; }
    /// <summary>The key that opened this archive; mod containers are encrypted with it.</summary>
    public byte[] Key { get; }

    GameArchive(GameInstall install, IoStoreReader reader, ContainerHeader header, byte[] key)
    {
        Install = install; Reader = reader; Header = header; Key = key;
        var catalog = SkinCatalog.Build(reader.Files.Keys);
        try
        {
            var pakPath = Path.ChangeExtension(install.UtocPath, ".pak");
            using var pak = PakReader.Open(pakPath, key);
            catalog = catalog.WithNames(SkinNames.Load(pak));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or System.Text.Json.JsonException)
        {
            Log.Error("Could not read the game's skin names; using folder names", e);
        }
        Catalog = catalog;
    }

    public static GameArchive Open(GameInstall install, byte[] key)
    {
        var r = IoStoreReader.Open(install.UtocPath, key);
        try
        {
            int hi = r.FindChunkOfType(IoStoreReader.ChunkTypeContainerHeader);
            if (hi < 0) throw new InvalidDataException("The game archive has no container header.");
            var h = ContainerHeader.Parse(r.ReadChunk(hi));
            return new GameArchive(install, r, h, key);
        }
        catch
        {
            r.Dispose();
            throw;
        }
    }

    public int IndexOf(string fullPath) =>
        Reader.Files.TryGetValue(fullPath, out int i) ? i : throw new FileNotFoundException("Not in the game archive: " + fullPath);

    public byte[] ReadPackage(string fullPath) => Reader.ReadChunk(IndexOf(fullPath));

    public byte[] ChunkIdOf(string fullPath) => Reader.ChunkId(IndexOf(fullPath));

    /// <summary>Path as stored in a mod container: relative to the "../../../" mount point.</summary>
    public string RelativePath(string fullPath) =>
        fullPath.StartsWith(ContainerWriter.MountPoint, StringComparison.Ordinal) ? fullPath[ContainerWriter.MountPoint.Length..] : fullPath.TrimStart('/');

    public void Dispose() => Reader.Dispose();
}
