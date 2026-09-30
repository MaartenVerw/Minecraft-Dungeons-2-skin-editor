using System.Diagnostics;
using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.Mods;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.App;

/// <summary>A page that may hold unsaved work: asked before the user leaves it.</summary>
interface IConfirmLeave
{
    bool ConfirmLeave();
}

/// <summary>The window: a header, a scrollable page area (home, wizard or help) and a status line.</summary>
sealed class MainForm : Form, IElevator
{
    public Studio Studio { get; }
    readonly Panel _host;
    readonly Label _status;
    bool _busy;

    public MainForm()
    {
        Studio = new Studio(Program.Version) { Elevator = this };
        Text = "MCD2 Skin Studio";
        Icon = Ui.AppIcon();
        BackColor = Ui.Background;
        Font = Ui.Body;
        var screen = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(1280, 900);
        MinimumSize = new Size(Math.Min(Ui.Px(1000), screen.Width), Math.Min(Ui.Px(700), screen.Height));
        Size = new Size(Math.Min(Ui.Px(1220), screen.Width), Math.Min(Ui.Px(880), screen.Height));
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Ui.Header };
        var logo = new PictureBox { Image = Ui.Logo(40), SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(18, 12) };
        var title = new Label { Text = "MCD2 Skin Studio", ForeColor = Color.White, Font = Ui.Title, AutoSize = true, Location = new Point(68, 14) };
        var sub = new Label { Text = "Your own hero skins for Minecraft Dungeons II  ·  v" + Program.Version, ForeColor = Color.FromArgb(180, 182, 186), Font = Ui.Small, AutoSize = true };
        header.Controls.AddRange([logo, title, sub]);
        header.Layout += (_, _) => sub.Location = new Point(title.Right + 12, title.Top + 12);

        _status = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(16, 6, 16, 0), ForeColor = Ui.Muted, Font = Ui.Small, BackColor = Color.FromArgb(236, 236, 232) };
        _host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28, 20, 28, 20) };
        _host.Resize += (_, _) => ContentResized?.Invoke(this, EventArgs.Empty);
        Controls.Add(_host);
        Controls.Add(_status);
        Controls.Add(header);

        Shown += async (_, _) => await StartUp();
        FormClosing += (s, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                Status("Please wait until the current step is finished.");
            }
            else if (_host.Controls.Count > 0 && _host.Controls[0] is IConfirmLeave page && !page.ConfirmLeave())
                e.Cancel = true;
        };
        FormClosed += (_, _) => Studio.Dispose();
    }

    async Task StartUp()
    {
        await Busy("Looking for Minecraft Dungeons II…", async () =>
        {
            await Task.Run(Studio.Detect);
            if (Studio.Game != null) await Studio.EnsureKeyAsync();
        });
        ShowHome();
    }

    public void ShowPage(Control page)
    {
        _host.SuspendLayout();
        foreach (Control c in _host.Controls) c.Dispose();
        _host.Controls.Clear();
        page.Location = new Point(_host.Padding.Left, _host.Padding.Top);
        _host.Controls.Add(page);
        _host.ResumeLayout();
        _host.AutoScrollPosition = Point.Empty;
    }

    /// <summary>Usable width for page content, so long text wraps instead of scrolling sideways.</summary>
    public int ContentWidth => Math.Max(600, _host.ClientSize.Width - _host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);

    /// <summary>Usable height for page content.</summary>
    public int ContentHeight => _host.ClientSize.Height - _host.Padding.Vertical;

    /// <summary>Raised when the page area changes size (window resized or maximised).</summary>
    public event EventHandler? ContentResized;

    public void ShowHome() => ShowPage(new HomePage(this));
    public void ShowWizard(SkinEntry? preselect = null) => ShowPage(new WizardPage(this, preselect));
    public void ShowHelp() => ShowPage(new HelpPage(this));

    public void Status(string text) => _status.Text = text;

    public bool IsBusy => _busy;

    /// <summary>Runs work off the UI thread with a wait cursor; shows friendly messages for known failures.
    /// Returns false when it failed.</summary>
    public async Task<bool> Busy(string status, Func<Task> work)
    {
        if (_busy) return false;
        _busy = true;
        UseWaitCursor = true;
        _host.Enabled = false;
        Status(status);
        try
        {
            await work();
            Status("");
            return true;
        }
        catch (Exception e) when (e is StudioException or GameRunningException or SkinImageException)
        {
            Status("");
            Warn(e.Message);
            return false;
        }
        catch (Exception e)
        {
            Log.Error(status, e);
            Status("");
            MessageBox.Show(this, "That didn't work: " + e.Message + "\n\nIf it keeps happening, use Help > Report a problem.", "MCD2 Skin Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            _host.Enabled = true;
            UseWaitCursor = false;
            _busy = false;
        }
    }

    public Task<bool> Busy(string status, Action work) => Busy(status, () => Task.Run(work));

    public void Warn(string message) => MessageBox.Show(this, message, "MCD2 Skin Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    public void Info(string message) => MessageBox.Show(this, message, "MCD2 Skin Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
    public bool Ask(string message, string title = "MCD2 Skin Studio") =>
        MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Error("Open " + target, e); }
    }

    public void LaunchGame()
    {
        if (Studio.Game == null) return;
        Open(Studio.Game.Edition == Edition.Xbox
            ? @"shell:AppsFolder\Microsoft.MinecraftDungeons2_8wekyb3d8bbwe!AppMinecraftDungeonsIIShipping"
            : "steam://rungameid/" + GameLocator.SteamAppId);
    }

    // IElevator: called from a worker thread; the UAC prompt is Windows' own window.
    bool IElevator.ApplyStage(string stageDir, GameInstall game)
    {
        if (!(bool)Invoke(() => Ask("Windows needs administrator permission to put your skins in the game's mods folder.\n\nContinue? Windows will ask you to confirm.")))
            return false;
        return Program.RunElevated($"--apply-stage \"{stageDir}\" \"{game.Root}\"");
    }

    bool IElevator.Uninstall(GameInstall game)
    {
        if (!(bool)Invoke(() => Ask("Windows needs administrator permission to remove the skin files from the game's mods folder.\n\nContinue? Windows will ask you to confirm.")))
            return false;
        return Program.RunElevated($"--uninstall \"{game.Root}\"");
    }
}
