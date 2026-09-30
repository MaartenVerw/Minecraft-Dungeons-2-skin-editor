using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Mcd2SkinStudio.Core.Game;

/// <summary>Finds Minecraft Dungeons II installs: Xbox app first, then Steam, then a remembered folder.</summary>
public static partial class GameLocator
{
    public const string SteamAppId = "1912410";
    const string GameFolder = "Minecraft Dungeons II";

    public static List<GameInstall> FindAll(string? rememberedFolder = null)
    {
        var found = new List<GameInstall>();
        void Add(GameInstall? g)
        {
            if (g != null && !found.Any(f => string.Equals(Path.GetFullPath(f.Root), Path.GetFullPath(g.Root), StringComparison.OrdinalIgnoreCase)))
                found.Add(g);
        }
        foreach (var c in XboxCandidates()) Add(GameInstall.FromFolder(c));
        foreach (var c in SteamCandidates()) Add(GameInstall.FromFolder(c));
        Add(GameInstall.FromFolder(rememberedFolder));
        return found;
    }

    public static IEnumerable<string> XboxCandidates()
    {
        var list = new List<string>();
        foreach (var d in FixedDrives())
        {
            foreach (var name in ReadGamingRoot(Path.Combine(d, ".GamingRoot")))
                list.Add(Path.Combine(d, name, GameFolder, "Content"));
            list.Add(Path.Combine(d, "XboxGames", GameFolder, "Content"));
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>.GamingRoot: "RGBX", u32 count, then UTF-16LE null-terminated folder names.</summary>
    public static List<string> ReadGamingRoot(string path)
    {
        var names = new List<string>();
        try
        {
            if (!File.Exists(path)) return names;
            var b = File.ReadAllBytes(path);
            if (b.Length < 8 || Encoding.ASCII.GetString(b, 0, 4) != "RGBX") return names;
            names.AddRange(Encoding.Unicode.GetString(b, 8, (b.Length - 8) & ~1).Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return names;
    }

    public static IEnumerable<string> SteamCandidates()
    {
        var libs = new List<string>();
        string? steam = null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            steam = (k?.GetValue("SteamPath") as string)?.Replace('/', '\\');
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        if (steam != null)
        {
            libs.Add(steam);
            try
            {
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        foreach (var d in FixedDrives())
        {
            libs.Add(Path.Combine(d, "SteamLibrary"));
            libs.Add(Path.Combine(d, "Program Files (x86)", "Steam"));
        }
        var result = new List<string>();
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            try
            {
                var manifest = Path.Combine(apps, $"appmanifest_{SteamAppId}.acf");
                if (File.Exists(manifest))
                {
                    var m = VdfInstallDir().Match(File.ReadAllText(manifest));
                    if (m.Success) result.Add(Path.Combine(apps, "common", m.Groups[1].Value));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            result.Add(Path.Combine(apps, "common", GameFolder));
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    static IEnumerable<string> FixedDrives()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            bool ok;
            try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
            if (ok) yield return d.RootDirectory.FullName;
        }
    }

    [GeneratedRegex("\"path\"\\s+\"(.+?)\"")]
    private static partial Regex VdfPath();

    [GeneratedRegex("\"installdir\"\\s+\"(.+?)\"")]
    private static partial Regex VdfInstallDir();
}
