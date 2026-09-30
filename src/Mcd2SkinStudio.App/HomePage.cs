using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.App;

/// <summary>Home: the game card, a health banner, the user's custom skins and the main actions.</summary>
sealed class HomePage : FlowLayoutPanel
{
    readonly MainForm _f;

    public HomePage(MainForm f)
    {
        _f = f;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Build();
    }

    Studio S => _f.Studio;

    void Build()
    {
        Controls.Add(GameCard.Build(_f, _f.ShowHome));
        var health = S.Game == null ? null : S.Check();
        if (health != null && health.Health != Health.NothingInstalled) Controls.Add(Banner(health));

        Controls.Add(Ui.Label("Your custom skins", Ui.H2));
        Controls.Add(SkinsGrid());

        var actions = Ui.Row();
        actions.Margin = new Padding(0, 8, 0, 0);
        var make = Ui.Primary("＋  Make a new skin", (_, _) => _f.ShowWizard());
        make.Enabled = S.Game != null && S.Key != null;
        actions.Controls.Add(make);
        var repair = Ui.Secondary("Repair", async (_, _) => await Repair());
        repair.Enabled = S.Game != null;
        actions.Controls.Add(repair);
        var removeAll = Ui.Secondary("Remove all custom skins", async (_, _) => await RemoveAll());
        removeAll.Enabled = S.Game != null && (S.State.Skins.Count > 0 || health?.Install != Core.Mods.InstallState.NotInstalled);
        actions.Controls.Add(removeAll);
        var launch = Ui.Secondary("▶  Launch game", (_, _) => _f.LaunchGame());
        launch.Enabled = S.Game != null;
        actions.Controls.Add(launch);
        actions.Controls.Add(Ui.Secondary("Help", (_, _) => _f.ShowHelp()));
        Controls.Add(actions);

        Controls.Add(Ui.Label("Custom skins only change how heroes look on your own PC. Other players in co-op see the normal heroes.", Ui.Small, Ui.Muted, _f.ContentWidth));
    }

    Control Banner(HealthReport h)
    {
        var (colour, icon) = h.Health switch
        {
            Health.Ok => (Ui.Good, "✔"),
            Health.NothingInstalled => (Ui.Surface, "ℹ"),
            Health.NeedsRepair => (Ui.Warn, "⚠"),
            _ => (Ui.Bad, "✖"),
        };
        var row = Ui.Row();
        row.WrapContents = false;
        row.BackColor = colour;
        row.Padding = new Padding(14, 10, 14, 4);
        row.Margin = new Padding(0, 0, 0, 14);
        var text = Ui.Label($"{icon}  {h.Message}", Ui.Bold, null, _f.ContentWidth - 200);
        text.Margin = new Padding(0, 6, 16, 6);
        row.Controls.Add(text);
        if (h.Health == Health.NeedsRepair || h.Health == Health.KeyUnknown)
            row.Controls.Add(Ui.Primary("Repair", async (_, _) => await Repair()));
        return row;
    }

    Control SkinsGrid()
    {
        var grid = Ui.Row();
        grid.MaximumSize = new Size(_f.ContentWidth, 0);
        grid.Margin = new Padding(0, 0, 0, 8);
        if (S.State.Skins.Count == 0)
        {
            grid.Controls.Add(Ui.Card(Ui.Label("No custom skins yet. Press “Make a new skin” to start: pick a hero, paint it right here in the app, and install it.", null, Ui.Muted, 520)));
            return grid;
        }
        foreach (var saved in S.State.Skins)
        {
            var col = Ui.Column();
            var img = S.State.LoadImage(saved);
            if (img != null) col.Controls.Add(Ui.Picture(Ui.ToBitmap(SkinRender.Front(img), 6, checker: false)));
            col.Controls.Add(Ui.Label(saved.Name, Ui.Bold));
            var links = Ui.Row();
            links.Controls.Add(Ui.Link("Edit again", () => EditAgain(saved)));
            links.Controls.Add(Ui.Link("Remove", async () => await Remove(saved)));
            col.Controls.Add(links);
            var card = Ui.Card(col);
            card.Margin = new Padding(0, 0, 12, 12);
            grid.Controls.Add(card);
        }
        return grid;
    }

    void EditAgain(SavedSkin saved)
    {
        SkinEntry? entry = null;
        try { entry = S.FindSkin(saved.Key); } catch (StudioException e) { _f.Warn(e.Message); return; }
        if (entry == null) { _f.Warn($"{saved.Name} isn't in this game version any more."); return; }
        _f.ShowWizard(entry);
    }

    async Task Remove(SavedSkin saved)
    {
        if (!_f.Ask($"Remove your custom {saved.Name} skin? The hero goes back to its normal look.")) return;
        if (await _f.Busy($"Removing {saved.Name}…", () => { S.ForgetSkin(saved.Key); S.InstallAll(); }))
            _f.Status($"{saved.Name} is back to normal.");
        _f.ShowHome();
    }

    async Task RemoveAll()
    {
        if (!_f.Ask("Remove all your custom skins from the game? Every hero goes back to its normal look, and your saved skins are forgotten.")) return;
        int n = 0;
        if (await _f.Busy("Removing all custom skins…", () => { n = S.RemoveAll(); }))
            _f.Info("All custom skins were removed. The game looks normal again.");
        _f.ShowHome();
    }

    async Task Repair()
    {
        string msg = "";
        if (await _f.Busy("Repairing: checking the game and rebuilding your skins…", async () => msg = await S.RepairAsync()))
            _f.Info(msg);
        _f.ShowHome();
    }
}
