using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.Keys;
using Mcd2SkinStudio.Core.Mods;

namespace Mcd2SkinStudio.Cli;

static class Program
{
    const string AppVersion = "0.1.0";

    static int Main(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }
        try
        {
            var cmd = args[0].TrimStart('-').ToLowerInvariant();
            var rest = args.Skip(1).ToList();
            string? gameArg = TakeOption(rest, "--game");
            switch (cmd)
            {
                case "info": return Info(gameArg);
                case "spike":
                {
                    using var a = OpenArchive(gameArg);
                    Spike.Run(a, rest.FirstOrDefault() ?? "spike-out");
                    return 0;
                }
                case "testmod":
                {
                    var g = Locate(gameArg);
                    var k = new KeyProvider().Resolve(g) ?? throw new Exception("No known key opens this game version.");
                    using var a = GameArchive.Open(g, k.Key);
                    Console.WriteLine("Building Phase 0 test containers:");
                    var files = TestMod.Build(a, k.Key);
                    Console.WriteLine("  read-back verification passed");
                    if (rest.Contains("--dry-run")) return 0;
                    Installer.Install(g, files, Installer.NewMarker(g, ["Tank (test red)", "Steve (test blue)"], files, AppVersion));
                    Console.WriteLine($"Installed to {g.ModsDir}:");
                    foreach (var f in Directory.GetFiles(g.ModsDir)) Console.WriteLine($"  {Path.GetFileName(f)}  {new FileInfo(f).Length} bytes");
                    return 0;
                }
                case "pakdump":
                {
                    // pakdump <substring> <outDir>: copy matching loose files out of the game's .pak (read-only)
                    var g = Locate(gameArg);
                    var k = new KeyProvider().Resolve(g) ?? throw new Exception("No known key opens this game version.");
                    using var pak = Mcd2SkinStudio.Core.IoStore.PakReader.Open(Path.Combine(g.PaksDir, g.TocName + ".pak"), k.Key);
                    Directory.CreateDirectory(rest[1]);
                    foreach (var f in pak.Files.Keys.Where(f => f.Contains(rest[0], StringComparison.OrdinalIgnoreCase)))
                    {
                        var data = pak.Read(f);
                        File.WriteAllBytes(Path.Combine(rest[1], f.Replace("../../../", "").Replace('/', '_')), data);
                        Console.WriteLine($"{f}  {data.Length}");
                    }
                    return 0;
                }
                case "uninstall":
                {
                    var g = Locate(gameArg);
                    Console.WriteLine($"Removed {Installer.Uninstall(g)} mod files. ~mods exists: {Directory.Exists(g.ModsDir)}");
                    return 0;
                }
                case "status":
                {
                    var g = Locate(gameArg);
                    Console.WriteLine(Installer.Check(g));
                    return 0;
                }
                default: Usage(); return 1;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("error: " + e.Message);
            Console.Error.WriteLine(e);
            return 2;
        }
    }

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
            mcd2skin - MCD2 Skin Studio command line (for testing)

              info                         locate the game, show fingerprint and key status
              spike [outDir]               Phase 0 read-only checks
              testmod [--dry-run]          Phase 0 (d): red Tank chest + blue Steve chest into ~mods
              status                       installed mod state
              uninstall                    remove everything this app installed

            Options: --game <folder>       use this install instead of auto-detect
            """);
    }

    static GameInstall Locate(string? gameArg)
    {
        if (gameArg != null) return GameInstall.FromFolder(gameArg) ?? throw new Exception("No Minecraft Dungeons II install in " + gameArg);
        var all = GameLocator.FindAll();
        if (all.Count == 0) throw new Exception("Minecraft Dungeons II was not found. Use --game <folder>.");
        return all[0];
    }

    static GameArchive OpenArchive(string? gameArg)
    {
        var g = Locate(gameArg);
        var keys = new KeyProvider();
        var k = keys.Resolve(g) ?? throw new Exception("No known key opens this game version.");
        return GameArchive.Open(g, k.Key);
    }

    static int Info(string? gameArg)
    {
        var all = gameArg != null ? [Locate(gameArg)] : GameLocator.FindAll();
        if (all.Count == 0) { Console.WriteLine("No install found."); return 1; }
        var keys = new KeyProvider();
        foreach (var g in all)
        {
            var fp = Fingerprint.Of(g);
            var k = keys.Resolve(g);
            Console.WriteLine($"{g.EditionName}\n  {g.Root}\n  {fp}\n  key: {(k == null ? "NOT KNOWN" : $"ok ({k.Source}, fingerprint match: {k.FingerprintMatched})")}");
        }
        return 0;
    }
}
