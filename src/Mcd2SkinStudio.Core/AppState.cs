using System.Text.Json;
using System.Text.Json.Serialization;
using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core;

public sealed class SavedSkin
{
    /// <summary>Catalog key (folder-based, stable across game languages), e.g. "Tank_Deluxe".</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>File name inside %APPDATA%\MCD2SkinStudio\skins.</summary>
    [JsonPropertyName("png")] public string Png { get; set; } = "";
    [JsonPropertyName("added")] public DateTime Added { get; set; }
}

/// <summary>%APPDATA%\MCD2SkinStudio\config.json: the chosen game and the user's skins.
/// Edited PNGs are copied into AppData so Repair works even if the originals move.</summary>
public sealed class AppState
{
    [JsonPropertyName("gamePath")] public string? GamePath { get; set; }
    [JsonPropertyName("edition")] public string? Edition { get; set; }
    [JsonPropertyName("skins")] public List<SavedSkin> Skins { get; set; } = [];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppState Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(AppPaths.ConfigFile)) ?? new AppState();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("config.json unreadable, starting fresh", e);
        }
        return new AppState();
    }

    public void Save()
    {
        var tmp = AppPaths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, AppPaths.ConfigFile, overwrite: true);
    }

    public SavedSkin Put(string key, string name, RgbaImage image)
    {
        var fileName = string.Concat(key.Split(Path.GetInvalidFileNameChars())) + ".png";
        Imaging.Png.Save(image, Path.Combine(AppPaths.SkinsDir, fileName));
        Skins.RemoveAll(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var s = new SavedSkin { Key = key, Name = name, Png = fileName, Added = DateTime.Now };
        Skins.Add(s);
        return s;
    }

    public void Remove(string key)
    {
        foreach (var s in Skins.Where(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            try { File.Delete(Path.Combine(AppPaths.SkinsDir, s.Png)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Skins.Remove(s);
        }
    }

    public RgbaImage? LoadImage(SavedSkin s)
    {
        var p = Path.Combine(AppPaths.SkinsDir, s.Png);
        try { return File.Exists(p) ? Imaging.Png.Load(p) : null; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }
}
