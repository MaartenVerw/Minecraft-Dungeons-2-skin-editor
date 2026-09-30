using System.Diagnostics;
using Mcd2SkinStudio.Core.IoStore;

namespace Mcd2SkinStudio.Core.Game;

/// <summary>Identifies one game version: changes whenever an update rewrites the main .utoc.</summary>
public sealed record Fingerprint(Edition Edition, long UtocSize, ulong ContainerId, DateTime UtocWriteUtc)
{
    public string ContainerIdHex => "0x" + ContainerId.ToString("X16");

    public static Fingerprint Of(GameInstall g)
    {
        var fi = new FileInfo(g.UtocPath);
        using var f = new FileStream(g.UtocPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var b = new byte[0x40];
        f.ReadExactly(b);
        return new Fingerprint(g.Edition, fi.Length, Bin.U64(b, 0x38), fi.LastWriteTimeUtc);
    }

    /// <summary>Same game build (the write time is ignored: copying an install changes it).</summary>
    public bool SameBuild(Fingerprint? o) => o != null && o.Edition == Edition && o.UtocSize == UtocSize && o.ContainerId == ContainerId;

    public override string ToString() => $"{Edition} utoc {UtocSize} {ContainerIdHex}";
}

public static class GameProcess
{
    /// <summary>True while any build of the game is running.</summary>
    public static bool IsRunning()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var n = p.ProcessName;
                if (n.StartsWith("Dungeons-Win", StringComparison.OrdinalIgnoreCase) && n.EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        return false;
    }
}
