using System.Runtime.CompilerServices;
using Mcd2SkinStudio.Core;

namespace Mcd2SkinStudio.Tests;

/// <summary>Keeps every test away from the user's real %APPDATA%\MCD2SkinStudio and Documents: the
/// app's data and export folders point into one temp folder per test run, deleted when the run ends.</summary>
static class TestData
{
    internal static string Root { get; private set; } = "";

#pragma warning disable CA2255 // a module initializer is exactly what a test assembly needs here
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init()
    {
        Root = Directory.CreateTempSubdirectory("mcd2test").FullName;
        AppPaths.OverrideDataDir(Path.Combine(Root, "data"));
        AppPaths.OverrideExportDir(Path.Combine(Root, "export"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
    }

    /// <summary>A fresh folder inside this run's temp folder.</summary>
    internal static string NewDir(string name) =>
        Directory.CreateDirectory(Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N")[..8])).FullName;
}

public class TestDataTests
{
    [Fact]
    public void App_folders_point_into_the_test_run_folder()
    {
        Assert.StartsWith(TestData.Root, AppPaths.DataDir);
        Assert.StartsWith(TestData.Root, AppPaths.ExportDir);
    }
}
