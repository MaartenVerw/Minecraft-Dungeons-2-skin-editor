using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcd2SkinStudio.Core.Game;

namespace Mcd2SkinStudio.Core.Mods;

/// <summary>Written next to the mod files; the only list uninstall trusts.</summary>
public sealed class InstallMarker
{
    [JsonPropertyName("app")] public string App { get; set; } = "MCD2 Skin Studio";
    [JsonPropertyName("appVersion")] public string AppVersion { get; set; } = "";
    [JsonPropertyName("installedAt")] public DateTime InstalledAt { get; set; }
    [JsonPropertyName("edition")] public string Edition { get; set; } = "";
    [JsonPropertyName("utocSize")] public long UtocSize { get; set; }
    [JsonPropertyName("containerId")] public string ContainerId { get; set; } = "";
    [JsonPropertyName("skins")] public List<string> Skins { get; set; } = [];
    /// <summary>File name → SHA-256 hex.</summary>
    [JsonPropertyName("files")] public Dictionary<string, string> Files { get; set; } = [];
    [JsonPropertyName("createdModsDir")] public bool CreatedModsDir { get; set; }

    public bool SameBuild(Fingerprint fp) =>
        Edition == fp.Edition.ToString() && UtocSize == fp.UtocSize && ContainerId == fp.ContainerIdHex;
}

public enum InstallState
{
    NotInstalled,
    Ok,
    FilesMissing,
    FilesChanged,
    GameUpdated,
}

public sealed class GameRunningException() : Exception("Close Minecraft Dungeons II first, then try again.");

/// <summary>Writing to ~mods was refused; the files are staged and need an elevated copy.</summary>
public sealed class NeedsElevationException(string stageDir) : Exception("Windows needs administrator permission to write to the game's mods folder.")
{
    public string StageDir { get; } = stageDir;
}

/// <summary>
/// Installs into &lt;Paks&gt;\~mods only, and only ever deletes files it wrote itself (listed in the marker
/// and named zzz_MCD2SkinStudio*). The game's own files are never opened for writing.
/// </summary>
public static class Installer
{
    public const string MarkerName = "MCD2SkinStudio.install.json";
    static readonly string[] AllowedExtensions = [".pak", ".utoc", ".ucas"];
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string MarkerPath(GameInstall g) => Path.Combine(g.ModsDir, MarkerName);

    public static bool IsOurFile(string name) =>
        name.StartsWith(ModBuilder.FilePrefix, StringComparison.OrdinalIgnoreCase)
        && AllowedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
        && Path.GetFileName(name) == name;

    public static InstallMarker? ReadMarker(GameInstall g)
    {
        try
        {
            var p = MarkerPath(g);
            return File.Exists(p) ? JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(p)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static string Sha256(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    public static InstallMarker NewMarker(GameInstall g, IEnumerable<string> skins, Dictionary<string, byte[]> files, string appVersion)
    {
        var fp = Fingerprint.Of(g);
        return new InstallMarker
        {
            AppVersion = appVersion,
            InstalledAt = DateTime.Now,
            Edition = fp.Edition.ToString(),
            UtocSize = fp.UtocSize,
            ContainerId = fp.ContainerIdHex,
            Skins = skins.ToList(),
            Files = files.ToDictionary(kv => kv.Key, kv => Sha256(kv.Value)),
        };
    }

    public static InstallState Check(GameInstall g)
    {
        var m = ReadMarker(g);
        if (m == null) return InstallState.NotInstalled;
        foreach (var (name, hash) in m.Files)
        {
            var p = Path.Combine(g.ModsDir, name);
            if (!File.Exists(p)) return InstallState.FilesMissing;
            try { if (!Sha256(File.ReadAllBytes(p)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return InstallState.FilesChanged; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return InstallState.FilesChanged; }
        }
        return m.SameBuild(Fingerprint.Of(g)) ? InstallState.Ok : InstallState.GameUpdated;
    }

    /// <summary>Replaces any earlier install with these files.</summary>
    public static void Install(GameInstall g, Dictionary<string, byte[]> files, InstallMarker marker)
    {
        foreach (var name in files.Keys)
            if (!IsOurFile(name)) throw new ArgumentException("Refusing to write " + name);
        if (GameProcess.IsRunning()) throw new GameRunningException();
        try
        {
            ApplyFiles(g, files, marker);
        }
        catch (UnauthorizedAccessException)
        {
            throw new NeedsElevationException(Stage(files, marker));
        }
    }

    static void ApplyFiles(GameInstall g, Dictionary<string, byte[]> files, InstallMarker marker)
    {
        var old = ReadMarker(g);
        bool created = !Directory.Exists(g.ModsDir);
        Directory.CreateDirectory(g.ModsDir);
        marker.CreatedModsDir = created || (old?.CreatedModsDir ?? false);
        if (old != null)
            foreach (var name in old.Files.Keys.Where(n => IsOurFile(n) && !files.ContainsKey(n)))
                DeleteIfExists(Path.Combine(g.ModsDir, name));
        foreach (var (name, data) in files)
        {
            var tmp = Path.Combine(g.ModsDir, name + ".tmp");
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, Path.Combine(g.ModsDir, name), overwrite: true);
        }
        File.WriteAllText(MarkerPath(g), JsonSerializer.Serialize(marker, Json));
        Log.Info($"Installed {string.Join(", ", marker.Skins)} to {g.ModsDir}");
    }

    /// <summary>Writes the files and marker to a temp folder for an elevated helper to copy.</summary>
    public static string Stage(Dictionary<string, byte[]> files, InstallMarker marker)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MCD2SkinStudio-stage-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        foreach (var (name, data) in files) File.WriteAllBytes(Path.Combine(dir, name), data);
        File.WriteAllText(Path.Combine(dir, MarkerName), JsonSerializer.Serialize(marker, Json));
        return dir;
    }

    /// <summary>Runs in the elevated helper: copies a staged install into the game's ~mods.</summary>
    public static void ApplyStage(GameInstall g, string stageDir)
    {
        var marker = JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(Path.Combine(stageDir, MarkerName)))
                     ?? throw new InvalidDataException("Bad staged install");
        var files = new Dictionary<string, byte[]>();
        foreach (var name in marker.Files.Keys)
        {
            if (!IsOurFile(name)) throw new InvalidDataException("Refusing to write " + name);
            var data = File.ReadAllBytes(Path.Combine(stageDir, name));
            if (!Sha256(data).Equals(marker.Files[name], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Staged file changed: " + name);
            files[name] = data;
        }
        ApplyFiles(g, files, marker);
    }

    /// <summary>Best-effort removal of a staged install once the elevated copy is done or was refused.</summary>
    public static void DeleteStage(string stageDir)
    {
        try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Uninstall: deletes only files listed in the marker (and named like ours), then the marker.
    /// Returns how many files were removed.</summary>
    public static int Uninstall(GameInstall g)
    {
        if (GameProcess.IsRunning()) throw new GameRunningException();
        var m = ReadMarker(g);
        if (m == null) return 0;
        int n = 0;
        foreach (var name in m.Files.Keys.Where(IsOurFile))
            if (DeleteIfExists(Path.Combine(g.ModsDir, name))) n++;
        DeleteIfExists(MarkerPath(g));
        if (m.CreatedModsDir)
        {
            try { if (!Directory.EnumerateFileSystemEntries(g.ModsDir).Any()) Directory.Delete(g.ModsDir); }
            catch (IOException) { }
        }
        Log.Info($"Uninstalled {n} files from {g.ModsDir}");
        return n;
    }

    static bool DeleteIfExists(string p)
    {
        if (!File.Exists(p)) return false;
        File.Delete(p);
        return true;
    }
}
