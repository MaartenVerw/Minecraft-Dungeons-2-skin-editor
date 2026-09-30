using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mcd2SkinStudio.Core.IoStore;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>
/// The names players see in the Locker ("Kellen", "Fancy Flores", …), read from the game's own
/// DT_SkinDefinition table and its English string table inside the .pak.
/// </summary>
public static partial class SkinNames
{
    const string TableJson = "Dungeons/Content/Spicewood/Core/DataTables/UFS/DT_SkinDefinition.json";
    const string StringsCsv = "Dungeons/Content/Text/Release/DT_SkinDefinition.csv";

    /// <summary>One table row: hero folder + variant flags → display name.</summary>
    public sealed record Row(string Folder, bool Deluxe, bool PreOrder, string Name, string TypeTag);

    public static List<Row> Load(PakReader pak)
    {
        string? Find(string suffix) => pak.Files.Keys.FirstOrDefault(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var jsonPath = Find(TableJson) ?? throw new FileNotFoundException("DT_SkinDefinition.json not found");
        var csvPath = Find(StringsCsv);
        var strings = csvPath != null ? ParseCsv(Encoding.UTF8.GetString(pak.Read(csvPath))) : [];
        return Parse(Encoding.UTF8.GetString(pak.Read(jsonPath)), strings);
    }

    public static List<Row> Parse(string json, Dictionary<string, string> strings)
    {
        var rows = new List<Row>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            var avatar = r.TryGetProperty("AvatarMaterial", out var a) ? a.GetString() ?? "" : "";
            var m = AvatarPath().Match(avatar);
            if (!m.Success) continue;
            string folder = m.Groups[1].Value, material = m.Groups[2].Value;
            string tag = r.TryGetProperty("TypeTag", out var t) && t.TryGetProperty("TagName", out var tn) ? tn.GetString() ?? "" : "";
            string name = r.TryGetProperty("Name", out var n) ? n.GetString() ?? folder : folder;
            foreach (var text in Strings(r))
            {
                var key = LocKey().Match(text);
                if (key.Success && strings.TryGetValue(key.Groups[1].Value, out var s)) { name = s; break; }
            }
            rows.Add(new Row(folder,
                material.Contains("deluxe", StringComparison.OrdinalIgnoreCase),
                material.Contains("preorder", StringComparison.OrdinalIgnoreCase),
                Tidy(name), tag));
        }
        return rows;
    }

    static IEnumerable<string> Strings(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: yield return e.GetString() ?? ""; break;
            case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) foreach (var s in Strings(p.Value)) yield return s; break;
            case JsonValueKind.Array: foreach (var i in e.EnumerateArray()) foreach (var s in Strings(i)) yield return s; break;
        }
    }

    /// <summary>"Kellen Skin" → "Kellen".</summary>
    static string Tidy(string s)
    {
        s = s.Trim();
        return s.EndsWith(" Skin", StringComparison.OrdinalIgnoreCase) ? s[..^5] : s;
    }

    /// <summary>UE string-table CSV: "Key","SourceString",… with doubled quotes inside values.</summary>
    public static Dictionary<string, string> ParseCsv(string csv)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in csv.Split('\n'))
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') q = false;
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                else if (c != '\r') sb.Append(c);
            }
            cells.Add(sb.ToString());
            if (cells.Count >= 2 && cells[0] != "Key" && cells[0].Length > 0) d[cells[0]] = cells[1];
        }
        return d;
    }

    [GeneratedRegex(@"/Skins/([^/]+)/([^./]+)\.", RegexOptions.IgnoreCase)]
    private static partial Regex AvatarPath();

    [GeneratedRegex(@"LOCTABLE\(""[^""]+"",\s*""([^""]+)""\)")]
    private static partial Regex LocKey();
}
