namespace Mcd2SkinStudio.Core.Skins;

/// <summary>One replaceable hero skin texture found in the game.</summary>
public sealed record SkinEntry(string Key, string Hero, string DisplayName, string TexturePath, string? MresPath, string? IconPath)
{
    /// <summary>Folder-based name ("Tank (Deluxe)"), kept for logs and support.</summary>
    public string InternalName { get; init; } = DisplayName;
    public bool Deluxe => TexturePath.Contains("deluxe", StringComparison.OrdinalIgnoreCase);
    public bool PreOrder => TexturePath.Contains("preorder", StringComparison.OrdinalIgnoreCase);
    /// <summary>Safe file name for exports, e.g. "Fancy Kellen".</summary>
    public string FileStem => string.Concat(DisplayName.Split(Path.GetInvalidFileNameChars()));
}

/// <summary>
/// Lists the hero skins under Spicewood/Art/Characters/Player/Skins/&lt;Hero&gt;/. Names are matched with
/// patterns, never hardcoded: the game's naming is inconsistent.
/// </summary>
public sealed class SkinCatalog
{
    public const string SkinsFolder = "/Dungeons/Content/Spicewood/Art/Characters/Player/Skins/";
    public const string MasterMesh = "/Dungeons/Content/Spicewood/Art/Characters/Player/Master/SK_Player_Master.uasset";

    public IReadOnlyList<SkinEntry> Skins { get; }

    SkinCatalog(List<SkinEntry> skins) => Skins = skins;

    public SkinEntry? Find(string keyOrName) =>
        Skins.FirstOrDefault(s => s.Key.Equals(keyOrName, StringComparison.OrdinalIgnoreCase))
        ?? Skins.FirstOrDefault(s => s.DisplayName.Equals(keyOrName, StringComparison.OrdinalIgnoreCase))
        ?? Skins.FirstOrDefault(s => s.InternalName.Equals(keyOrName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces folder-based names with the game's own skin names where the table has a row.</summary>
    public SkinCatalog WithNames(IReadOnlyList<SkinNames.Row> rows)
    {
        var named = Skins.Select(s =>
        {
            var row = rows.FirstOrDefault(r => r.Folder.Equals(s.Hero, StringComparison.OrdinalIgnoreCase) && r.Deluxe == s.Deluxe && r.PreOrder == s.PreOrder)
                      ?? rows.FirstOrDefault(r => r.Folder.Equals(s.Hero, StringComparison.OrdinalIgnoreCase) && r.Deluxe == s.Deluxe);
            return row == null ? s : s with { DisplayName = row.Name, InternalName = s.InternalName };
        }).ToList();
        // two skins must never share a display name
        foreach (var g in named.GroupBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
            foreach (var s in g.Skip(1).ToList())
                named[named.IndexOf(s)] = s with { DisplayName = s.DisplayName + " (" + s.InternalName + ")" };
        return new SkinCatalog(named.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static SkinCatalog Build(IEnumerable<string> archivePaths)
    {
        var byHero = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in archivePaths)
        {
            int i = p.IndexOf(SkinsFolder, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var rel = p[(i + SkinsFolder.Length)..];
            var parts = rel.Split('/');
            if (parts.Length != 2) continue;   // only files directly in Skins/<Hero>/
            if (!parts[1].StartsWith("T_", StringComparison.OrdinalIgnoreCase) || !parts[1].EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
            if (!byHero.TryGetValue(parts[0], out var l)) byHero[parts[0]] = l = [];
            l.Add(p);
        }

        var skins = new List<SkinEntry>();
        foreach (var (hero, files) in byHero)
        {
            var textures = files.Where(f => Kind(f) == TexKind.Skin).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var tex in textures)
            {
                var name = FileName(tex);
                bool deluxe = name.Contains("deluxe", StringComparison.OrdinalIgnoreCase);
                bool preorder = name.Contains("preorder", StringComparison.OrdinalIgnoreCase) || name.Contains("pre_order", StringComparison.OrdinalIgnoreCase);
                string? mres = BestMatch(files, TexKind.Mres, deluxe, preorder);
                string? icon = BestMatch(files, TexKind.Icon, deluxe, preorder);
                var display = HeroDisplay(hero) + (deluxe ? " (Deluxe)" : "") + (preorder ? " (Pre-order)" : "");
                var key = hero.Trim('_') + (deluxe ? "_Deluxe" : "") + (preorder ? "_PreOrder" : "");
                skins.Add(new SkinEntry(key, hero, display, tex, mres, icon));
            }
        }
        // disambiguate duplicates by texture name
        foreach (var g in skins.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
            foreach (var s in g.ToList())
            {
                var stem = Path.GetFileNameWithoutExtension(FileName(s.TexturePath));
                int ix = skins.IndexOf(s);
                skins[ix] = s with { Key = stem, DisplayName = s.DisplayName + " – " + stem };
            }
        return new SkinCatalog(skins.OrderBy(s => s.Hero.StartsWith('_')).ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    enum TexKind { Skin, Mres, Icon }

    static string FileName(string p) => p[(p.LastIndexOf('/') + 1)..];

    static TexKind Kind(string path)
    {
        var n = FileName(path);
        if (n.Contains("MRES", StringComparison.OrdinalIgnoreCase)) return TexKind.Mres;
        if (n.Contains("Icon", StringComparison.OrdinalIgnoreCase)) return TexKind.Icon;
        return TexKind.Skin;
    }

    static string? BestMatch(List<string> files, TexKind kind, bool deluxe, bool preorder)
    {
        var c = files.Where(f => Kind(f) == kind).ToList();
        return c.FirstOrDefault(f => FileName(f).Contains("deluxe", StringComparison.OrdinalIgnoreCase) == deluxe
                                     && FileName(f).Contains("preorder", StringComparison.OrdinalIgnoreCase) == preorder)
               ?? c.FirstOrDefault(f => !FileName(f).Contains("deluxe", StringComparison.OrdinalIgnoreCase));
    }

    public static string HeroDisplay(string folder)
    {
        var n = folder.Trim('_');
        if (n.Equals("PizzaChef", StringComparison.OrdinalIgnoreCase)) return "Pizza Chef";
        return folder.StartsWith('_') ? n + " (starter)" : n;
    }
}
