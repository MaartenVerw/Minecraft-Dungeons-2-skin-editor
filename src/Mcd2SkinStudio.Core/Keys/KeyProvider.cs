using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.IoStore;

namespace Mcd2SkinStudio.Core.Keys;

public sealed class KeyEntry
{
    [JsonPropertyName("edition")] public string Edition { get; set; } = "";
    [JsonPropertyName("gameVersionHint")] public string? GameVersionHint { get; set; }
    [JsonPropertyName("utocSize")] public long UtocSize { get; set; }
    [JsonPropertyName("containerId")] public string? ContainerId { get; set; }
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("added")] public string? Added { get; set; }

    public byte[]? KeyBytes()
    {
        var h = Key.Trim();
        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h[2..];
        if (h.Length != 64) return null;
        try { return Convert.FromHexString(h); } catch (FormatException) { return null; }
    }

    public ulong? ContainerIdValue()
    {
        var h = ContainerId?.Trim();
        if (string.IsNullOrEmpty(h)) return null;
        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h[2..];
        return ulong.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public bool Matches(Fingerprint fp) =>
        string.Equals(Edition, fp.Edition == Game.Edition.Xbox ? "WinGDK" : "Windows", StringComparison.OrdinalIgnoreCase)
        && UtocSize == fp.UtocSize && ContainerIdValue() == fp.ContainerId;
}

public sealed class KeyFile
{
    [JsonPropertyName("keys")] public List<KeyEntry> Keys { get; set; } = [];
}

public enum KeySource { Embedded, Remote, Cache }

public sealed record ResolvedKey(byte[] Key, KeyEntry Entry, KeySource Source, bool FingerprintMatched);

/// <summary>
/// Knows the game's archive keys: keys.json embedded at build time, plus a remote copy the
/// maintainer updates after game updates (cached in AppData). Never asks the user for a key.
/// </summary>
public sealed class KeyProvider
{
    public const string RemoteUrl = "https://raw.githubusercontent.com/maartenverw06/mcd2-skin-studio/main/keys.json";
    static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(5);

    readonly List<(KeyEntry Entry, KeySource Source)> _entries = [];
    public string? LastRemoteError { get; private set; }

    public KeyProvider()
    {
        Load(ReadEmbedded(), KeySource.Embedded);
        try
        {
            if (File.Exists(AppPaths.KeysCacheFile)) Load(File.ReadAllText(AppPaths.KeysCacheFile), KeySource.Cache);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public IReadOnlyList<KeyEntry> Entries => _entries.Select(e => e.Entry).ToList();

    static string ReadEmbedded()
    {
        using var s = typeof(KeyProvider).Assembly.GetManifestResourceStream("keys.json")
            ?? throw new InvalidOperationException("keys.json is not embedded");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public int Load(string json, KeySource source)
    {
        KeyFile? f;
        try { f = JsonSerializer.Deserialize<KeyFile>(json); }
        catch (JsonException) { return 0; }
        int added = 0;
        foreach (var e in f?.Keys ?? [])
        {
            if (e.KeyBytes() == null) continue;
            if (_entries.Any(x => x.Entry.Key.Equals(e.Key, StringComparison.OrdinalIgnoreCase) && x.Entry.UtocSize == e.UtocSize)) continue;
            _entries.Add((e, source));
            added++;
        }
        return added;
    }

    /// <summary>Fetches keys.json from GitHub (5 s timeout) and caches it. Failures are ignored.</summary>
    public async Task<int> RefreshRemoteAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = RemoteTimeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MCD2SkinStudio");
            var json = await http.GetStringAsync(RemoteUrl, ct).ConfigureAwait(false);
            int added = Load(json, KeySource.Remote);
            if (JsonSerializer.Deserialize<KeyFile>(json)?.Keys.Count > 0)
                await File.WriteAllTextAsync(AppPaths.KeysCacheFile, json, ct).ConfigureAwait(false);
            LastRemoteError = null;
            return added;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException)
        {
            LastRemoteError = e.Message;
            Log.Info("keys.json refresh failed: " + e.Message);
            return 0;
        }
    }

    /// <summary>Finds a key that opens this install: fingerprint matches first, then every known key.</summary>
    public ResolvedKey? Resolve(GameInstall g)
    {
        var fp = Fingerprint.Of(g);
        var utoc = IoStoreReader.ReadShared(g.UtocPath);
        var ordered = _entries.Where(e => e.Entry.Matches(fp)).Concat(_entries.Where(e => !e.Entry.Matches(fp)));
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entry, source) in ordered)
        {
            var k = entry.KeyBytes()!;
            if (!tried.Add(Convert.ToHexString(k))) continue;
            if (IoStoreReader.KeyOpensIndex(utoc, k))
                return new ResolvedKey(k, entry, source, entry.Matches(fp));
        }
        return null;
    }
}
