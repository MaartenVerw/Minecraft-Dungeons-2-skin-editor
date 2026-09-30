using System.ComponentModel;
using System.Diagnostics;
using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.Mods;

namespace Mcd2SkinStudio.App;

static class Program
{
    public static string Version => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    [STAThread]
    static int Main(string[] args)
    {
        // Elevated helper modes: only the file copy runs as administrator, never the whole app.
        if (args.Length == 3 && args[0] == "--apply-stage") return Helper(() => Installer.ApplyStage(Game(args[2]), args[1]), args[1]);
        if (args.Length == 2 && args[0] == "--uninstall") return Helper(() => Installer.Uninstall(Game(args[1])), null);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);
        Log.Info($"MCD2 Skin Studio {Version} started");
        Application.Run(new MainForm());
        return 0;
    }

    static GameInstall Game(string root) => GameInstall.FromFolder(root) ?? throw new DirectoryNotFoundException(root);

    static int Helper(Action work, string? stageToDelete)
    {
        try
        {
            work();
            return 0;
        }
        catch (Exception e)
        {
            Log.Error("Elevated helper failed", e);
            return 1;
        }
        finally
        {
            if (stageToDelete != null)
                try { Directory.Delete(stageToDelete, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Runs this exe again as administrator for one file operation. Returns false if the
    /// user said no to the Windows prompt or the helper failed.</summary>
    public static bool RunElevated(string arguments)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = true, Verb = "runas" });
            if (p == null) return false;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;   // the user cancelled the UAC prompt
        }
    }

    static void Crash(Exception? e)
    {
        Log.Error("Unhandled error", e);
        MessageBox.Show("Something went wrong and MCD2 Skin Studio has to stop.\n\nA log was saved. Open Help > Open log folder and attach log.txt when you report it.\n\n" + e?.Message,
            "MCD2 Skin Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
