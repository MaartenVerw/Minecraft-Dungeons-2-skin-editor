using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.App;

/// <summary>Make a new skin: 1 Game · 2 Hero · 3 Paint (the built-in editor) · 4 Install.</summary>
sealed class WizardPage : FlowLayoutPanel, IConfirmLeave
{
    static readonly string[] Steps = ["Game", "Hero", "Paint", "Install"];

    readonly MainForm _f;
    readonly FlowLayoutPanel _stepBar = Ui.Row();
    readonly FlowLayoutPanel _body = Ui.Column();
    readonly FlowLayoutPanel _nav = Ui.Row();
    int _step;
    SkinEntry? _skin;
    SkinEditor? _editor;
    string? _editorKey;
    EditorView? _view;
    Dictionary<string, RgbaImage>? _originals;
    bool _installed;

    Studio S => _f.Studio;
    int W => _f.ContentWidth;

    public WizardPage(MainForm f, SkinEntry? preselect)
    {
        _f = f;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _stepBar.Margin = new Padding(0, 0, 0, 18);
        _body.Margin = new Padding(0, 0, 0, 16);
        Controls.AddRange([_stepBar, _body, _nav]);
        _skin = preselect;
        Go(preselect != null ? 2 : 0);
    }

    async void Go(int step)
    {
        _step = step;
        DrawStepBar();
        _body.SuspendLayout();
        foreach (Control c in _body.Controls) c.Dispose();
        _body.Controls.Clear();
        _nav.Controls.Clear();
        _body.ResumeLayout();
        switch (step)
        {
            case 0: StepGame(); break;
            case 1: await StepHero(); break;
            case 2: await StepPaint(); break;
            case 3: await StepInstall(); break;
        }
    }

    void DrawStepBar()
    {
        _stepBar.Controls.Clear();
        for (int i = 0; i < Steps.Length; i++)
        {
            bool cur = i == _step, done = i < _step;
            var l = Ui.Label($"{(done ? "✔" : (i + 1).ToString())}  {Steps[i]}", cur ? Ui.H2 : Ui.Body, cur ? Ui.AccentDark : done ? Ui.Text : Ui.Muted);
            l.Margin = new Padding(0, cur ? 0 : 4, 6, 0);
            _stepBar.Controls.Add(l);
            if (i < Steps.Length - 1)
            {
                var sep = Ui.Label("›", Ui.H2, Ui.Border);
                sep.Margin = new Padding(0, 0, 6, 0);
                _stepBar.Controls.Add(sep);
            }
        }
        if (_step == 2 && _skin != null)
        {
            var hero = Ui.Label($"Painting {_skin.DisplayName}", Ui.H2, Ui.Text);
            hero.Margin = new Padding(28, 0, 0, 0);
            _stepBar.Controls.Add(hero);
        }
    }

    void Nav(bool back, Button? next, bool cancel = true)
    {
        if (back) _nav.Controls.Add(Ui.Secondary("‹  Back", (_, _) => Go(_step - 1)));
        if (next != null) _nav.Controls.Add(next);
        if (cancel) _nav.Controls.Add(Ui.Secondary("Cancel", (_, _) => { if (ConfirmLeave()) _f.ShowHome(); }));
    }

    void Add(Control c) => _body.Controls.Add(c);
    void Heading(string h, string? sub = null)
    {
        Add(Ui.Label(h, Ui.Title));
        if (sub != null) Add(Ui.Label(sub, null, Ui.Muted, W));
    }

    /// <summary>True when there's nothing to lose, or the user agrees to lose their painting.</summary>
    public bool ConfirmLeave()
    {
        if (_installed || _editor?.HasEdits != true) return true;
        return _f.Ask($"Leave without installing? Your changes to {_skin?.DisplayName} will be lost.", "Unsaved changes");
    }

    // ---------------------------------------------------------------- 1 Game
    void StepGame()
    {
        Heading("Is this your game?", "MCD2 Skin Studio found this copy of Minecraft Dungeons II. If you have more than one, pick the one you play with “Change…”.");
        Add(GameCard.Build(_f, () => Go(0)));
        var next = Ui.Primary("Yes, next  ›", (_, _) => Go(1));
        next.Enabled = S.Game != null && S.Key != null;
        Nav(false, next);
    }

    // ---------------------------------------------------------------- 2 Hero
    async Task StepHero()
    {
        Heading("Pick a hero", "Choose the skin you want to change. You paint it right here in the app; your version replaces it in the Locker and in-game.");
        if (_originals == null)
        {
            var loaded = new Dictionary<string, RgbaImage>();
            if (!await _f.Busy("Reading the heroes from your game…", () => { foreach (var s in S.Skins()) loaded[s.Key] = S.Original(s); }))
            {
                Nav(true, null);
                return;
            }
            _originals = loaded;
        }
        var grid = Ui.Row();
        grid.MaximumSize = new Size(W, 0);
        Button next = Ui.Primary("Next  ›", (_, _) => Go(2));
        next.Enabled = _skin != null;
        var tiles = new List<Panel>();
        foreach (var s in S.Skins())
        {
            if (!_originals.TryGetValue(s.Key, out var img)) continue;
            var col = Ui.Column();
            var pic = Ui.Picture(Ui.ToBitmap(SkinRender.Front(img), 4, checker: false));
            pic.Margin = new Padding(22, 0, 22, 4);
            col.Controls.Add(pic);
            var name = Ui.Label(s.DisplayName, Ui.Bold);
            name.MaximumSize = new Size(108, 0);
            col.Controls.Add(name);
            if (S.State.Skins.Any(x => x.Key == s.Key)) col.Controls.Add(Ui.Label("custom", Ui.Small, Ui.AccentDark));
            var tile = new Panel { Size = new Size(124, 188), Padding = new Padding(8), Margin = new Padding(0, 0, 10, 10), Cursor = Cursors.Hand, BackColor = s == _skin ? Ui.Selected : Ui.Surface };
            tile.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, tile.ClientRectangle, tile.BackColor == Ui.Selected ? Ui.Accent : Ui.Border, ButtonBorderStyle.Solid);
            col.Location = new Point(8, 8);
            tile.Controls.Add(col);
            bool Pick()
            {
                if (_editor != null && _editorKey != s.Key && _editor.HasEdits && !_installed &&
                    !_f.Ask($"Switch to {s.DisplayName}? Your changes to {_skin?.DisplayName} will be lost.", "Unsaved changes"))
                    return false;
                _skin = s;
                foreach (var t in tiles) { t.BackColor = t == tile ? Ui.Selected : Ui.Surface; t.Invalidate(); }
                next.Enabled = true;
                return true;
            }
            foreach (var c in new Control[] { tile, col, pic, name })
            {
                c.Click += (_, _) => Pick();
                c.DoubleClick += (_, _) => { if (Pick()) Go(2); };
            }
            tiles.Add(tile);
            grid.Controls.Add(tile);
        }
        Add(grid);
        Nav(true, next);
    }

    // --------------------------------------------------------------- 3 Paint
    async Task StepPaint()
    {
        var s = _skin!;
        if (_editor == null || _editorKey != s.Key)
        {
            RgbaImage? original = null, mine = null;
            if (!await _f.Busy($"Opening {s.DisplayName}…", () =>
                {
                    original = S.Original(s);
                    mine = S.Saved(s);
                }))
            {
                Nav(true, null);
                return;
            }
            _editor = new SkinEditor(original!, mine);
            _editorKey = s.Key;
            _view = null;
        }
        var editor = new SkinEditorControl(_editor) { Size = EditorSize(), Margin = Padding.Empty };
        if (_view is EditorView v) editor.ShowView(v);
        EventHandler resize = (_, _) => editor.Size = EditorSize();
        _f.ContentResized += resize;
        editor.Disposed += (_, _) =>
        {
            _f.ContentResized -= resize;
            _view = editor.View;
        };
        Add(editor);
        Nav(true, Ui.Primary("Install  ›", (_, _) =>
        {
            if (_editor.Unchanged)
            {
                _f.Warn($"This is still exactly the game's own {s.DisplayName}. Paint something first.");
                return;
            }
            Go(3);
        }));
    }

    /// <summary>The editor fills the window below the step bar, above the buttons.</summary>
    Size EditorSize() => new(W, Math.Max(540, _f.ContentHeight - 124));

    // ------------------------------------------------------------- 4 Install
    async Task StepInstall()
    {
        var s = _skin!;
        if (_installed)
        {
            Done(s);
            return;
        }
        Heading("Installing…", $"Putting your {s.DisplayName} into the game.");
        InstallResult? r = null;
        bool ok = await _f.Busy($"Installing {s.DisplayName}…", () =>
        {
            S.SaveSkin(s, _editor!.Skin.Clone());
            r = S.InstallAll();
        });
        _body.Controls.Clear();
        if (!ok)
        {
            Heading("Not installed yet", "Your skin is saved. Fix the problem shown in the message (for example, close the game) and try again.");
            Nav(true, Ui.Primary("Try again", (_, _) => Go(3)));
            return;
        }
        _installed = true;
        if (r!.Skipped.Count > 0) _f.Warn("These saved skins don't exist in this game version and were skipped: " + string.Join(", ", r.Skipped));
        Done(s);
    }

    void Done(SkinEntry s)
    {
        DrawStepBar();
        Heading("Done!", $"Start the game and pick {s.DisplayName} in the Locker.");
        var row = Ui.Row();
        row.WrapContents = false;
        row.Controls.Add(Ui.Picture(Ui.ToBitmap(SkinRender.Front(_editor!.Skin), 8, checker: false)));
        row.Controls.Add(Ui.Picture(Ui.ToBitmap(SkinRender.Back(_editor.Skin), 8, checker: false)));
        Add(row);
        Add(Ui.Label("Didn't change in the game? Close the game completely, then press Repair on the home screen.", Ui.Small, Ui.Muted, W));
        _nav.Controls.Add(Ui.Primary("▶  Launch game", (_, _) => _f.LaunchGame()));
        _nav.Controls.Add(Ui.Secondary("Make another skin", (_, _) => _f.ShowWizard()));
        _nav.Controls.Add(Ui.Secondary("Back to home", (_, _) => _f.ShowHome()));
    }
}
