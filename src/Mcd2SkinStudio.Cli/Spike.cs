using System.Text;
using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.IoStore;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Cli;

/// <summary>Phase 0 checks (a)–(c): read-only facts about the installed game's skin textures.</summary>
static class Spike
{
    public static void Run(GameArchive a, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var r = a.Reader;
        Console.WriteLine($"utoc v{r.Version}, {r.EntryCount} entries, flags 0x{r.Flags:x2}, methods [{string.Join(",", r.CompressionMethods)}], mount {r.MountPoint}, {r.Files.Count} files");
        Console.WriteLine($"container header v{a.Header.Version}, {a.Header.PackageCount} packages, tail {a.Header.Tail.Length} bytes: {Convert.ToHexString(a.Header.Tail)}");

        // every file in the skin folders, with size
        Console.WriteLine();
        Console.WriteLine("Skin folder files:");
        foreach (var p in r.Files.Keys.Where(k => k.Contains(SkinCatalog.SkinsFolder)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  {p[(p.IndexOf(SkinCatalog.SkinsFolder) + SkinCatalog.SkinsFolder.Length)..],-60} {r.ChunkLength(r.Files[p]),8}");

        // (a) Oodle: Tank skin decompresses to the expected size
        Console.WriteLine();
        var tank = a.Catalog.Find("Tank") ?? throw new Exception("Tank not in catalog");
        var pkg = a.ReadPackage(tank.TexturePath);
        Console.WriteLine($"(a) {tank.TexturePath} → {pkg.Length} bytes (expected 16925), chunk {Convert.ToHexString(a.ChunkIdOf(tank.TexturePath)).ToLower()}");
        File.WriteAllBytes(Path.Combine(outDir, "T_Tank_Skin.uasset"), pkg);
        Png.Save(TextureIO.ExtractSkin(pkg), Path.Combine(outDir, "Tank.png"));
        Png.Save(TextureIO.ExtractSkin(pkg).ScaleNearest(8), Path.Combine(outDir, "Tank_x8.png"));

        // (b) MRES format
        if (tank.MresPath != null)
        {
            var m = a.ReadPackage(tank.MresPath);
            File.WriteAllBytes(Path.Combine(outDir, "T_Tank_MRES.uasset"), m);
            Console.WriteLine($"(b) {tank.MresPath} → {m.Length} bytes; pixel formats named: {string.Join(", ", PixelFormats(m))}");
            foreach (int n in new[] { 16384, 4096 })
            {
                try { Console.WriteLine($"    64x64 mip with {n} bytes at offset {TextureIO.FindMip(m, 64, 64, n)}"); }
                catch (InvalidDataException) { Console.WriteLine($"    no 64x64 triple for {n} bytes"); }
            }
        }
        Console.WriteLine($"    skin pixel formats named: {string.Join(", ", PixelFormats(pkg))}");

        // (c) does (56,20) hold a copy of the face (8,8) with the hat (40,8) composited on top?
        Console.WriteLine();
        Console.WriteLine("(c) portrait face at (56,20) vs face (8,8)+hat (40,8), per skin:");
        foreach (var s in a.Catalog.Skins)
        {
            try
            {
                var p = a.ReadPackage(s.TexturePath);
                var img = TextureIO.ExtractSkin(p);
                Png.Save(img, Path.Combine(outDir, s.Key + ".png"));
                var face = img.Crop(8, 8, 8, 8);
                var withHat = face.Clone(); withHat.Composite(img.Crop(40, 8, 8, 8), 0, 0);
                var portrait = img.Crop(56, 20, 8, 8);
                int same = Diff(portrait, face), sameHat = Diff(portrait, withHat);
                int opaque = 0; for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) if (portrait.Alpha(x, y) > 0) opaque++;
                int palette = 0; for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) if (img.Alpha(x, y) > 0) palette++;
                Console.WriteLine($"  {s.Key,-28} pkg {p.Length,6}  portrait opaque {opaque,2}/64  ==face {64 - same,2}/64  ==face+hat {64 - sameHat,2}/64  palette-corner opaque {palette,2}/64");
            }
            catch (Exception e) { Console.WriteLine($"  {s.Key,-28} ERROR {e.Message}"); }
        }
    }

    static int Diff(RgbaImage a, RgbaImage b)
    {
        int n = 0;
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++) if (!RgbaImage.SameColour(a.Get(x, y), b.Get(x, y))) n++;
        return n;
    }

    static IEnumerable<string> PixelFormats(byte[] pkg)
    {
        var s = Encoding.ASCII.GetString(pkg);
        int i = 0;
        var seen = new HashSet<string>();
        while ((i = s.IndexOf("PF_", i, StringComparison.Ordinal)) >= 0)
        {
            int j = i;
            while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
            seen.Add(s[i..j]);
            i = j;
        }
        return seen;
    }
}
