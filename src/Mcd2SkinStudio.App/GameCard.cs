using Mcd2SkinStudio.Core.Game;

namespace Mcd2SkinStudio.App;

/// <summary>The detected-game card: edition, folder, version and key status, with a Change menu.</summary>
static class GameCard
{
    public static Control Build(MainForm f, Action onChanged)
    {
        var s = f.Studio;
        var col = Ui.Column();
        var g = s.Game;
        if (g == null)
        {
            col.Controls.Add(Ui.Label("Minecraft Dungeons II wasn't found", Ui.H2));
            col.Controls.Add(Ui.Label("Pick the game folder yourself. It is the folder that contains a folder called \"Dungeons\":\n" +
                                      "  •  Xbox app / Microsoft Store / Minecraft Launcher:  usually C:\\XboxGames\\Minecraft Dungeons II\\Content\n" +
                                      "  •  Steam:  …\\steamapps\\common\\Minecraft Dungeons II", null, Ui.Muted, f.ContentWidth - 60));
        }
        else
        {
            col.Controls.Add(Ui.Label("Minecraft Dungeons II  ·  " + g.EditionName, Ui.H2));
            col.Controls.Add(Ui.Label(g.Root, Ui.Small, Ui.Muted));
            string version;
            try { var fp = Fingerprint.Of(g); version = $"Game version: {fp.UtocSize:N0} bytes · {fp.ContainerIdHex}"; }
            catch (IOException) { version = "Game version: couldn't be read"; }
            col.Controls.Add(Ui.Label(version, Ui.Small, Ui.Muted));
            col.Controls.Add(s.Key != null
                ? Ui.Label("✔  Game files can be read", Ui.Bold, Ui.AccentDark)
                : Ui.Label("✖  " + Core.Studio.KeyUnknownMessage, Ui.Bold, Color.FromArgb(180, 40, 30), f.ContentWidth - 60));
        }

        var change = Ui.Secondary(g == null ? "Find the game folder…" : "Change…", (_, _) => { });
        change.Click += (_, _) =>
        {
            var menu = new ContextMenuStrip { Font = Ui.Body };
            foreach (var i in s.Installs)
            {
                var item = new ToolStripMenuItem($"{i.EditionName}  —  {i.Root}") { Checked = g != null && i.Root == g.Root };
                item.Click += async (_, _) => await Select(f, i, onChanged);
                menu.Items.Add(item);
            }
            if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Browse for the game folder…", null, async (_, _) => await Browse(f, onChanged));
            menu.Show(change, new Point(0, change.Height));
        };
        var row = Ui.Row();
        row.Margin = new Padding(0, 6, 0, 0);
        row.Controls.Add(change);
        col.Controls.Add(row);
        return Ui.Card(col);
    }

    static async Task Select(MainForm f, GameInstall g, Action onChanged)
    {
        await f.Busy("Checking the game…", async () =>
        {
            f.Studio.SelectGame(g);
            await f.Studio.EnsureKeyAsync();
        });
        onChanged();
    }

    static async Task Browse(MainForm f, Action onChanged)
    {
        using var dlg = new FolderBrowserDialog { Description = "Pick the Minecraft Dungeons II folder (the one that contains \"Dungeons\")", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(f) != DialogResult.OK) return;
        var g = GameInstall.FromFolder(dlg.SelectedPath);
        if (g == null)
        {
            f.Warn("Minecraft Dungeons II isn't in that folder.\n\nPick the folder that contains a folder called \"Dungeons\". For the Xbox app that is usually C:\\XboxGames\\Minecraft Dungeons II\\Content.");
            return;
        }
        await Select(f, g, onChanged);
    }
}
