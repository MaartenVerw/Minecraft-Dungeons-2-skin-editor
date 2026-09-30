namespace Mcd2SkinStudio.Core;

/// <summary>Where the app keeps its own files. Nothing here is inside the game folder.</summary>
public static class AppPaths
{
    static string? _dataOverride;

    /// <summary>For tests: redirect %APPDATA%\MCD2SkinStudio somewhere else.</summary>
    public static void OverrideDataDir(string? dir) => _dataOverride = dir;

    public static string DataDir
    {
        get
        {
            var d = _dataOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCD2SkinStudio");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string SkinsDir { get { var d = Path.Combine(DataDir, "skins"); Directory.CreateDirectory(d); return d; } }
    public static string KeysCacheFile => Path.Combine(DataDir, "keys.cache.json");
    public static string LogFile => Path.Combine(DataDir, "log.txt");

    /// <summary>Documents\MCD2 Skin Studio: where exported textures go for editing.</summary>
    public static string ExportDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MCD2 Skin Studio");
            Directory.CreateDirectory(d);
            return d;
        }
    }
}

/// <summary>Plain append-only log in %APPDATA%\MCD2SkinStudio\log.txt.</summary>
public static class Log
{
    static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? e = null) => Write("ERROR", e == null ? message : $"{message}: {e}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var f = AppPaths.LogFile;
                if (File.Exists(f) && new FileInfo(f).Length > 1_000_000)
                    File.Move(f, f + ".old", overwrite: true);
                File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
