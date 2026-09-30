namespace Mcd2SkinStudio.Core.Game;

public enum Edition { Xbox, Steam }

/// <summary>A located Minecraft Dungeons II install. <see cref="Root"/> is the folder that contains "Dungeons".</summary>
public sealed record GameInstall(string Root, Edition Edition)
{
    public const string XboxTocName = "Dungeons-WinGDK";
    public const string SteamTocName = "Dungeons-Windows";

    public string PaksDir => Path.Combine(Root, "Dungeons", "Content", "Paks");
    public string TocName => Edition == Edition.Xbox ? XboxTocName : SteamTocName;
    public string UtocPath => Path.Combine(PaksDir, TocName + ".utoc");
    public string ModsDir => Path.Combine(PaksDir, "~mods");

    public string EditionName => Edition == Edition.Xbox
        ? "Xbox app / Microsoft Store / Minecraft Launcher"
        : "Steam";

    /// <summary>Recognises an install from any folder the user might pick: the install root,
    /// its "Content" parent, or any folder inside it down to Paks.</summary>
    public static GameInstall? FromFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            var dir = new DirectoryInfo(folder);
            // walk up from inside the install (e.g. the Paks folder)
            for (var d = dir; d != null; d = d.Parent)
            {
                var hit = Check(d.FullName);
                if (hit != null) return hit;
            }
            // or one level down (the Xbox "Minecraft Dungeons II" folder holds "Content")
            if (dir.Exists)
                foreach (var sub in dir.EnumerateDirectories())
                {
                    var hit = Check(sub.FullName);
                    if (hit != null) return hit;
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        return null;
    }

    static GameInstall? Check(string root)
    {
        var paks = Path.Combine(root, "Dungeons", "Content", "Paks");
        if (File.Exists(Path.Combine(paks, XboxTocName + ".utoc"))) return new GameInstall(root, Edition.Xbox);
        if (File.Exists(Path.Combine(paks, SteamTocName + ".utoc"))) return new GameInstall(root, Edition.Steam);
        return null;
    }
}
