using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.Keys;
using Mcd2SkinStudio.Core.Mods;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Core;

/// <param name="Sheet">The design sheet to paint (starts from the user's saved version when there is one).</param>
/// <param name="KeptEarlierWork">The sheet file already existed with the user's painting in it, so it was left as it is.</param>
public sealed record ExportResult(string Folder, string Sheet, string Texture, string? OriginalSheet, bool KeptEarlierWork);

public sealed record InstallResult(int Installed, IReadOnlyList<string> Skipped);

public enum Health { NoGame, KeyUnknown, NothingInstalled, Ok, NeedsRepair }

public sealed record HealthReport(Health Health, InstallState Install, string Message);

/// <summary>Runs one file operation as administrator when ~mods isn't writable (the app relaunches
/// itself with runas; the CLI has none). Returns false when the user declines.</summary>
public interface IElevator
{
    bool ApplyStage(string stageDir, GameInstall game);
    bool Uninstall(GameInstall game);
}

/// <summary>A user-facing failure: the message says what happened and the one thing to do.</summary>
public sealed class StudioException(string message) : Exception(message);

/// <summary>
/// Everything the app does, UI-free: find the game, get a key, export textures, keep the user's
/// skins, install them into ~mods and repair after game updates. The UI and CLI both drive this.
/// </summary>
public sealed class Studio : IDisposable
{
    public const string KeyUnknownMessage = "The game was updated and the new key isn't known yet. Try Repair again in a day or two.";

    public string AppVersion { get; }
    public AppState State { get; }
    public KeyProvider Keys { get; } = new();
    public IReadOnlyList<GameInstall> Installs { get; private set; } = [];
    public GameInstall? Game { get; private set; }
    public ResolvedKey? Key { get; private set; }
    public IElevator? Elevator { get; set; }

    const string ElevationDeclined = "Windows didn't allow writing to the game's mods folder. Try again and choose Yes when Windows asks for permission.";

    GameArchive? _archive;

    public Studio(string appVersion, AppState? state = null)
    {
        AppVersion = appVersion;
        State = state ?? AppState.Load();
    }

    /// <summary>Finds installs and picks the remembered one, or the first found.</summary>
    public void Detect()
    {
        Installs = GameLocator.FindAll(State.GamePath);
        var remembered = GameInstall.FromFolder(State.GamePath);
        var pick = remembered != null ? Installs.FirstOrDefault(i => SamePath(i.Root, remembered.Root)) ?? remembered : Installs.FirstOrDefault();
        SetGame(pick, save: false);
    }

    static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public void SelectGame(GameInstall g) => SetGame(g, save: true);

    void SetGame(GameInstall? g, bool save)
    {
        if (Game != null && g != null && SamePath(Game.Root, g.Root) && !save) return;
        CloseArchive();
        Game = g;
        Key = null;
        if (g != null && !Installs.Any(i => SamePath(i.Root, g.Root))) Installs = [.. Installs, g];
        if (save && g != null)
        {
            State.GamePath = g.Root;
            State.Edition = g.Edition.ToString();
            State.Save();
        }
    }

    GameInstall RequireGame() => Game ?? throw new StudioException("Minecraft Dungeons II wasn't found. Use 'Change' to pick the game folder.");

    /// <summary>Finds a working key; refreshes keys.json from GitHub when the known keys don't fit.</summary>
    public async Task<ResolvedKey?> EnsureKeyAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var g = RequireGame();
        if (Key != null && !forceRefresh) return Key;
        if (forceRefresh) await Keys.RefreshRemoteAsync(ct).ConfigureAwait(false);
        Key = Keys.Resolve(g);
        if (Key == null && !forceRefresh)
        {
            await Keys.RefreshRemoteAsync(ct).ConfigureAwait(false);
            Key = Keys.Resolve(g);
        }
        return Key;
    }

    public GameArchive Archive()
    {
        if (_archive != null) return _archive;
        var g = RequireGame();
        var k = Key ?? Keys.Resolve(g) ?? throw new StudioException(KeyUnknownMessage);
        Key = k;
        try
        {
            _archive = GameArchive.Open(g, k.Key);
        }
        catch (IOException e)
        {
            Log.Error("Opening the game archive failed", e);
            throw new StudioException("The game files couldn't be read. If the game is updating, wait for it to finish and try again.");
        }
        return _archive;
    }

    public void CloseArchive()
    {
        _archive?.Dispose();
        _archive = null;
    }

    public IReadOnlyList<SkinEntry> Skins() => Archive().Catalog.Skins;

    public SkinEntry? FindSkin(string key) => Archive().Catalog.Find(key);

    public RgbaImage Original(SkinEntry s) => TextureIO.ExtractSkin(Archive().ReadPackage(s.TexturePath));

    /// <summary>The user's saved version of this skin, or null when they haven't made one.</summary>
    public RgbaImage? Saved(SkinEntry s)
    {
        var saved = State.Skins.FirstOrDefault(x => x.Key.Equals(s.Key, StringComparison.OrdinalIgnoreCase));
        return saved == null ? null : State.LoadImage(saved);
    }

    /// <summary>
    /// Saves the design sheet and the 64×64 texture to Documents\MCD2 Skin Studio\&lt;skin&gt;. When the
    /// user already has a custom version, the sheet starts from it and the original sheet is saved too.
    /// A file the user has painted on is never overwritten.
    /// </summary>
    public ExportResult Export(SkinEntry s, string? baseDir = null)
    {
        var original = Original(s);
        var mine = Saved(s);
        var start = mine ?? original;
        var dir = Path.Combine(baseDir ?? AppPaths.ExportDir, s.FileStem);
        Directory.CreateDirectory(dir);
        var sheet = Path.Combine(dir, s.FileStem + " design sheet.png");
        var tex = Path.Combine(dir, s.FileStem + " texture 64x64.png");
        bool kept = !SaveUnlessEdited(DesignSheet.Create(start, s.DisplayName, s.Key), sheet);
        SaveUnlessEdited(start, tex);
        string? orig = null;
        if (mine != null)
        {
            orig = Path.Combine(dir, s.FileStem + " design sheet (original).png");
            SaveUnlessEdited(DesignSheet.Create(original, s.DisplayName, s.Key), orig);
        }
        Log.Info($"Exported {s.InternalName} to {dir}{(kept ? " (kept the user's painted sheet)" : "")}");
        return new ExportResult(dir, sheet, tex, orig, kept);
    }

    /// <summary>Writes the image unless the file exists with different pixels (the user's work). Returns false when it kept the file.</summary>
    static bool SaveUnlessEdited(RgbaImage img, string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var existing = Png.Load(path);
                if (existing.SameAs(img)) return true;
                return false;
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                return false;   // unreadable or locked by a paint program: leave it alone
            }
        }
        Png.Save(img, path);
        return true;
    }

    /// <summary>Remembers an edited skin (copied into AppData) without installing it yet.</summary>
    public void SaveSkin(SkinEntry s, RgbaImage image)
    {
        State.Put(s.Key, s.DisplayName, image);
        State.Save();
    }

    public void ForgetSkin(string key)
    {
        State.Remove(key);
        State.Save();
    }

    /// <summary>Builds one container from every saved skin and installs it (or uninstalls when none are left).</summary>
    public InstallResult InstallAll()
    {
        var g = RequireGame();
        if (State.Skins.Count == 0)
        {
            UninstallFiles();
            return new InstallResult(0, []);
        }
        var a = Archive();
        var items = new List<SkinReplacement>();
        var skipped = new List<string>();
        foreach (var saved in State.Skins)
        {
            var entry = a.Catalog.Find(saved.Key);
            var img = State.LoadImage(saved);
            if (entry == null || img == null || img.Width != 64 || img.Height != 64)
            {
                skipped.Add(saved.Name);
                Log.Info($"Skipping {saved.Key}: {(entry == null ? "not in this game version" : "image missing")}");
                continue;
            }
            items.Add(new SkinReplacement(entry, img));
        }
        if (items.Count == 0) throw new StudioException("None of your saved skins match this game version. Make them again with 'Make a new skin'.");
        var files = ModBuilder.Build(a, items);
        var marker = Installer.NewMarker(g, items.Select(i => i.Skin.DisplayName), files, AppVersion);
        try
        {
            Installer.Install(g, files, marker);
        }
        catch (NeedsElevationException e)
        {
            if (Elevator?.ApplyStage(e.StageDir, g) != true) throw new StudioException(ElevationDeclined);
        }
        return new InstallResult(items.Count, skipped);
    }

    /// <summary>Removes this app's files from ~mods (asking for permission if needed).</summary>
    public int UninstallFiles()
    {
        var g = RequireGame();
        try
        {
            return Installer.Uninstall(g);
        }
        catch (UnauthorizedAccessException)
        {
            int before = Installer.ReadMarker(g)?.Files.Count ?? 0;
            if (Elevator?.Uninstall(g) != true) throw new StudioException(ElevationDeclined);
            return before;
        }
    }

    /// <summary>Removes every custom skin from the game and forgets them.</summary>
    public int RemoveAll()
    {
        int n = UninstallFiles();
        foreach (var s in State.Skins.ToList()) State.Remove(s.Key);
        State.Save();
        return n;
    }

    /// <summary>The start-up check: is everything the user made still installed and current?</summary>
    public HealthReport Check()
    {
        if (Game == null) return new(Health.NoGame, InstallState.NotInstalled, "Minecraft Dungeons II wasn't found.");
        var state = Installer.Check(Game);
        if (Keys.Resolve(Game) == null) return new(Health.KeyUnknown, state, KeyUnknownMessage);
        int n = State.Skins.Count;
        if (n == 0) return new(state == InstallState.NotInstalled ? Health.NothingInstalled : Health.NeedsRepair, state,
            state == InstallState.NotInstalled ? "No custom skins yet." : "Old skin files are left over. Press Repair to clean up.");
        return state switch
        {
            InstallState.Ok => new(Health.Ok, state, n == 1 ? "Your custom skin is installed." : $"Your {n} custom skins are installed."),
            InstallState.GameUpdated => new(Health.NeedsRepair, state, "The game was updated. Press Repair to bring your skins back."),
            InstallState.NotInstalled or InstallState.FilesMissing => new(Health.NeedsRepair, state, "Your skins are missing from the game folder. Press Repair to put them back."),
            _ => new(Health.NeedsRepair, state, "Your skin files were changed. Press Repair to rebuild them."),
        };
    }

    /// <summary>Repair: re-find the game, refresh keys, rebuild from the stored PNGs against the
    /// current game files and reinstall.</summary>
    public async Task<string> RepairAsync(CancellationToken ct = default)
    {
        CloseArchive();
        Detect();
        RequireGame();
        var k = await EnsureKeyAsync(forceRefresh: true, ct).ConfigureAwait(false);
        if (k == null) throw new StudioException(KeyUnknownMessage);
        if (State.Skins.Count == 0)
        {
            int removed = UninstallFiles();
            return removed > 0 ? "Old skin files were removed. The game looks normal again." : "There are no custom skins to repair.";
        }
        var r = InstallAll();
        var msg = r.Installed == 1 ? "Your custom skin is back." : $"Your {r.Installed} custom skins are back.";
        if (r.Skipped.Count > 0) msg += $" Skipped (not in this game version): {string.Join(", ", r.Skipped)}.";
        return msg;
    }

    public void Dispose() => CloseArchive();
}
