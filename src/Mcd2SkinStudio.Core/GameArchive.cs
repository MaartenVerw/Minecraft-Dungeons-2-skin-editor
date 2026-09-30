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

    GameArchive(GameInstall install, IoStoreReader reader, ContainerHeader header)
    {
        Install = install; Reader = reader; Header = header;
        Catalog = SkinCatalog.Build(reader.Files.Keys);
    }

    public static GameArchive Open(GameInstall install, byte[] key)
    {
        var r = IoStoreReader.Open(install.UtocPath, key);
        try
        {
            int hi = r.FindChunkOfType(IoStoreReader.ChunkTypeContainerHeader);
            if (hi < 0) throw new InvalidDataException("The game archive has no container header.");
            var h = ContainerHeader.Parse(r.ReadChunk(hi));
            return new GameArchive(install, r, h);
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
