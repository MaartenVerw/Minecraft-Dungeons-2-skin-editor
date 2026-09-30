using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.IoStore;
using Mcd2SkinStudio.Core.Keys;
using Mcd2SkinStudio.Core.Mods;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Cli;

/// <summary>Command line for testing and maintenance. Uses the same state (%APPDATA%\MCD2SkinStudio) as the app.</summary>
static class Program
{
    const string AppVersion = "0.1.0";

    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }
        try
        {
            var cmd = args[0].TrimStart('-').ToLowerInvariant();
            var rest = args.Skip(1).ToList();
            string? gameArg = TakeOption(rest, "--game");
            using var studio = new Studio(AppVersion);
            if (gameArg != null)
                studio.SelectGame(GameInstall.FromFolder(gameArg) ?? throw new StudioException("No Minecraft Dungeons II install in " + gameArg));
            else
                studio.Detect();

            switch (cmd)
            {
                case "info": return await Info(studio);
                case "list":
                    await Key(studio);
                    foreach (var s in studio.Skins()) Console.WriteLine($"  {s.Key,-20} {s.DisplayName,-22} ({s.InternalName})");
                    return 0;
                case "export":
                {
                    await Key(studio);
                    var targets = rest.Count == 0 || rest[0] == "all" ? studio.Skins().ToList() : [Skin(studio, rest[0])];
                    string? dir = rest.Count > 1 ? rest[1] : null;
                    foreach (var s in targets) Console.WriteLine($"{s.DisplayName}: {studio.Export(s, dir).Folder}");
                    return 0;
                }
                case "install":
                {
                    // install <skin>=<png> [...]: saves each edited skin, then installs all saved skins
                    await Key(studio);
                    foreach (var pair in rest)
                    {
                        var i = pair.IndexOf('=');
                        if (i < 0) throw new StudioException("Use <skin>=<file.png>");
                        var s = Skin(studio, pair[..i]);
                        var img = SkinImport.Load(pair[(i + 1)..]);
                        var orig = studio.Original(s);
                        if (SkinImport.PaletteChanged(orig, img))
                        {
                            Console.WriteLine($"  {s.DisplayName}: face-animation corner changed, restoring the game's (use the app to keep your version)");
                            img = SkinImport.RestorePalette(orig, img);
                        }
                        studio.SaveSkin(s, img);
                    }
                    var r = studio.InstallAll();
                    Console.WriteLine($"Installed {r.Installed} skin(s) to {studio.Game!.ModsDir}" + (r.Skipped.Count > 0 ? $"; skipped {string.Join(", ", r.Skipped)}" : ""));
                    return 0;
                }
                case "remove":
                {
                    await Key(studio);
                    var s = Skin(studio, rest[0]);
                    studio.ForgetSkin(s.Key);
                    var r = studio.InstallAll();
                    Console.WriteLine($"Removed {s.DisplayName}; {r.Installed} custom skin(s) remain installed.");
                    return 0;
                }
                case "uninstall":
                    Console.WriteLine($"Removed {studio.RemoveAll()} mod files and forgot all saved skins. ~mods exists: {Directory.Exists(studio.Game!.ModsDir)}");
                    return 0;
                case "status":
                {
                    var h = studio.Check();
                    Console.WriteLine($"{h.Health} ({h.Install}): {h.Message}");
                    foreach (var s in studio.State.Skins) Console.WriteLine($"  saved: {s.Name} [{s.Key}]");
                    return 0;
                }
                case "repair":
                    Console.WriteLine(await studio.RepairAsync());
                    return 0;
                case "spike":
                    await Key(studio);
                    Spike.Run(studio.Archive(), rest.FirstOrDefault() ?? "spike-out");
                    return 0;
                case "pakdump":
                {
                    // pakdump <substring> <outDir>: copy matching loose files out of the game's .pak (read-only)
                    var k = await Key(studio);
                    using var pak = PakReader.Open(Path.ChangeExtension(studio.Game!.UtocPath, ".pak"), k.Key);
                    Directory.CreateDirectory(rest[1]);
                    foreach (var f in pak.Files.Keys.Where(f => f.Contains(rest[0], StringComparison.OrdinalIgnoreCase)))
                    {
                        var data = pak.Read(f);
                        File.WriteAllBytes(Path.Combine(rest[1], f.Replace("../../../", "").Replace('/', '_')), data);
                        Console.WriteLine($"{f}  {data.Length}");
                    }
                    return 0;
                }
                default: Usage(); return 1;
            }
        }
        catch (Exception e) when (e is StudioException or SkinImageException or GameRunningException or NeedsElevationException)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("error: " + e);
            return 3;
        }
    }

    static async Task<ResolvedKey> Key(Studio s) => await s.EnsureKeyAsync() ?? throw new StudioException(Studio.KeyUnknownMessage);

    static SkinEntry Skin(Studio s, string name) => s.FindSkin(name) ?? throw new StudioException($"Unknown skin '{name}'. Run 'list' to see them.");

    static string? TakeOption(List<string> args, string name)
    {
        int i = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Count) return null;
        var v = args[i + 1];
        args.RemoveRange(i, 2);
        return v;
    }

    static void Usage()
    {
        Console.WriteLine("""
            mcd2skin - MCD2 Skin Studio command line (testing and maintenance)

              info                         found installs, fingerprint and key status
              list                         skins in the game (key, in-game name, internal name)
              export [skin|all] [dir]      save texture + 8x preview + guide (default Documents\MCD2 Skin Studio)
              install <skin>=<png> ...     save edited skins and install every saved skin
              remove <skin>                forget one skin and reinstall the rest
              uninstall                    remove all mod files and forget all saved skins
              status                       start-up health check
              repair                       rebuild and reinstall from the saved skins
              spike [dir]                  Phase 0 read-only format checks
              pakdump <text> <dir>         copy loose files matching <text> out of the game's .pak

            Options: --game <folder>       use this install instead of auto-detect
            """);
    }

    static async Task<int> Info(Studio studio)
    {
        if (studio.Installs.Count == 0) { Console.WriteLine("No install found."); return 1; }
        foreach (var g in studio.Installs)
        {
            var fp = Fingerprint.Of(g);
            var k = studio.Keys.Resolve(g);
            Console.WriteLine($"{g.EditionName}{(g == studio.Game ? "  (selected)" : "")}\n  {g.Root}\n  {fp}\n  key: {(k == null ? "NOT KNOWN" : $"ok ({k.Source}, fingerprint match: {k.FingerprintMatched})")}");
        }
        await Task.CompletedTask;
        return 0;
    }
}
