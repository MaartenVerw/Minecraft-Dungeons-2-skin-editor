using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.App;

/// <summary>Make a new skin: 1 Game · 2 Hero · 3 Download · 4 Upload · 5 Install.</summary>
sealed class WizardPage : FlowLayoutPanel
{
    static readonly string[] Steps = ["Game", "Hero", "Download", "Upload", "Install"];

    readonly MainForm _f;
    readonly FlowLayoutPanel _stepBar = Ui.Row();
    readonly FlowLayoutPanel _body = Ui.Column();
    readonly FlowLayoutPanel _nav = Ui.Row();
    int _step;
    SkinEntry? _skin;
    RgbaImage? _original, _edited;
    ExportResult? _export;
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
            case 2: await StepDownload(); break;
            case 3: StepUpload(); break;
            case 4: await StepInstall(); break;
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
    }

    void Nav(bool back, Button? next, bool cancel = true)
    {
        if (back) _nav.Controls.Add(Ui.Secondary("‹  Back", (_, _) => Go(_step - 1)));
        if (next != null) _nav.Controls.Add(next);
        if (cancel) _nav.Controls.Add(Ui.Secondary("Cancel", (_, _) => _f.ShowHome()));
    }

    void Add(Control c) => _body.Controls.Add(c);
    void Heading(string h, string? sub = null)
    {
        Add(Ui.Label(h, Ui.Title));
        if (sub != null) Add(Ui.Label(sub, null, Ui.Muted, W));
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
        Heading("Pick a hero", "Choose the skin you want to change. Your version replaces it in the Locker and in-game.");
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
            var pic = Ui.Picture(Ui.ToBitmap(SkinRender.Front(img, portraitFace: true), 4, checker: false));
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
            void Pick()
            {
                _skin = s;
                foreach (var t in tiles) { t.BackColor = t == tile ? Ui.Selected : Ui.Surface; t.Invalidate(); }
                next.Enabled = true;
            }
            foreach (var c in new Control[] { tile, col, pic, name })
            {
                c.Click += (_, _) => Pick();
                c.DoubleClick += (_, _) => { Pick(); Go(2); };
            }
            tiles.Add(tile);
            grid.Controls.Add(tile);
        }
        Add(grid);
        Nav(true, next);
    }

    // ------------------------------------------------------------ 3 Download
    async Task StepDownload()
    {
        var s = _skin!;
        Heading($"Paint your {s.DisplayName}", "The hero's texture was saved to your Documents folder. Open it in Paint, paint over it, and save it.");
        ExportResult? r = null;
        string? mine = null;
        if (!await _f.Busy($"Saving the {s.DisplayName} texture…", () =>
            {
                r = S.Export(s);
                _original = S.Original(s);
                var saved = S.State.Skins.FirstOrDefault(x => x.Key == s.Key);
                var img = saved == null ? null : S.State.LoadImage(saved);
                if (img != null)
                {
                    mine = Path.Combine(r.Folder, s.FileStem + "_my_version.png");
                    Png.Save(img, mine);
                }
            }))
        {
            Nav(true, null);
            return;
        }
        _export = r;
        var tex = mine ?? r!.Texture;

        var files = Ui.Column();
        files.Controls.Add(Ui.Label("Saved to", Ui.Small, Ui.Muted));
        files.Controls.Add(Ui.Label(r!.Folder, Ui.Bold, null, W - 40));
        files.Controls.Add(Ui.Label($"{Path.GetFileName(r.Texture)}  –  the texture to edit (64×64)\n{Path.GetFileName(r.Preview)}  –  the same, 8× bigger, easier to see\n{Path.GetFileName(r.Guide)}  –  shows which part of the hero each area is" +
                                    (mine != null ? $"\n{Path.GetFileName(mine)}  –  your current custom version, if you want to continue from it" : ""), Ui.Small, Ui.Muted, W - 40));
        var buttons = Ui.Row();
        buttons.Margin = new Padding(0, 8, 0, 0);
        buttons.Controls.Add(Ui.Primary("Open in Paint", (_, _) => MainForm.OpenInPaint(tex)));
        buttons.Controls.Add(Ui.Secondary("Open folder", (_, _) => MainForm.Open(r.Folder)));
        buttons.Controls.Add(Ui.Secondary("Show guide", (_, _) => MainForm.Open(r.Guide)));
        files.Controls.Add(buttons);
        Add(Ui.Card(files));

        var pics = Ui.Row();
        pics.Controls.Add(Ui.Picture(Ui.ToBitmap(SkinRender.Front(_original!), 8, checker: false)));
        pics.Controls.Add(Ui.Picture(Ui.ToBitmap(_original!, 4)));
        Add(pics);

        Add(Ui.Label("Three tips", Ui.H2));
        var tips = Ui.Row();
        tips.MaximumSize = new Size(W, 0);
        tips.Controls.Add(Tip("1  Add a layer", "In Paint, click Layers on the toolbar and add a new layer. Paint on it, so the original stays underneath."));
        tips.Controls.Add(Tip("2  Don't resize", "Keep the picture 64×64. To see better, zoom in with Ctrl + mouse wheel, never with Resize."));
        tips.Controls.Add(Tip("3  Save as PNG", "File › Save as › PNG picture. Transparent areas must stay transparent."));
        Add(tips);
        Add(Ui.Label("Leave the red top-left corner (face animation) as it is. Edit the yellow portrait square so the Locker picture matches your new face.", Ui.Small, Ui.Muted, W));
        Nav(true, Ui.Primary("I've saved my picture  ›", (_, _) => Go(3)));
    }

    Control Tip(string title, string text)
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Label(title, Ui.Bold));
        col.Controls.Add(Ui.Label(text, Ui.Small, Ui.Muted, 230));
        var c = Ui.Card(col);
        c.Margin = new Padding(0, 0, 12, 12);
        c.MinimumSize = new Size(262, 0);
        return c;
    }

    // -------------------------------------------------------------- 4 Upload
    void StepUpload()
    {
        var s = _skin!;
        Heading("Upload your picture", $"Drop the PNG you saved, or browse for it. You'll see your {s.DisplayName} next to the original before anything is installed.");
        var next = Ui.Primary("Install  ›", (_, _) => Go(4));
        next.Enabled = _edited != null;

        var drop = new Panel { Size = new Size(Math.Min(W, 720), 120), BackColor = Ui.Surface, AllowDrop = true, Margin = new Padding(0, 0, 0, 14) };
        drop.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Accent, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, 1, 1, drop.Width - 3, drop.Height - 3);
        };
        var dropText = Ui.Label("Drop your edited PNG here", Ui.H2, Ui.AccentDark);
        dropText.Location = new Point(24, 22);
        var browse = Ui.Secondary("Browse…", (_, _) => { });
        browse.Location = new Point(24, 62);
        drop.Controls.AddRange([dropText, browse]);
        Add(drop);

        var compare = Ui.Row();
        compare.MaximumSize = new Size(W, 0);
        Add(compare);

        void Load(string path)
        {
            try
            {
                var img = SkinImport.Load(path);
                if (SkinImport.Unchanged(_original!, img))
                {
                    _f.Warn("This picture is still exactly the original skin. Paint your changes, save the file, then upload it again.");
                    return;
                }
                if (SkinImport.PaletteChanged(_original!, img) &&
                    _f.Ask("Your picture changed the top-left corner. The game uses those pixels to animate the eyes, eyebrows and mouth.\n\nKeep the game's face animation? (recommended)\n\nYes: put the game's corner back.\nNo: keep your pixels (the face may look strange in-game).", "Face animation"))
                    img = SkinImport.RestorePalette(_original!, img);
                _edited = img;
                ShowCompare(compare);
                next.Enabled = true;
                _f.Status("Loaded " + Path.GetFileName(path));
            }
            catch (SkinImageException e)
            {
                _f.Warn(e.Message);
            }
        }

        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Pick your edited skin",
                Filter = "PNG pictures (*.png)|*.png",
                InitialDirectory = _export?.Folder ?? AppPaths.ExportDir,
            };
            if (dlg.ShowDialog(_f) == DialogResult.OK) Load(dlg.FileName);
        };
        drop.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        drop.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) Load(files[0]);
        };
        if (_edited != null) ShowCompare(compare);
        Nav(true, next);
    }

    void ShowCompare(FlowLayoutPanel compare)
    {
        compare.Controls.Clear();
        compare.Controls.Add(Side("Original", _original!));
        compare.Controls.Add(Side("Yours", _edited!));
    }

    static Control Side(string title, RgbaImage img)
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Label(title, Ui.H2));
        var row = Ui.Row();
        row.WrapContents = false;
        row.Controls.Add(Ui.Picture(Ui.ToBitmap(SkinRender.Front(img), 8, checker: false)));
        row.Controls.Add(Ui.Picture(Ui.ToBitmap(img, 4)));
        col.Controls.Add(row);
        var c = Ui.Card(col);
        c.Margin = new Padding(0, 0, 14, 14);
        return c;
    }

    // ------------------------------------------------------------- 5 Install
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
            S.SaveSkin(s, _edited!);
            r = S.InstallAll();
        });
        _body.Controls.Clear();
        if (!ok)
        {
            Heading("Not installed yet", "Your picture is saved. Fix the problem shown in the message (for example, close the game) and try again.");
            Nav(true, Ui.Primary("Try again", (_, _) => Go(4)));
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
        var pic = Ui.Picture(Ui.ToBitmap(SkinRender.Front(_edited!), 8, checker: false));
        Add(pic);
        Add(Ui.Label("Didn't change in the game? Close the game completely, then press Repair on the home screen.", Ui.Small, Ui.Muted, W));
        _nav.Controls.Add(Ui.Primary("▶  Launch game", (_, _) => _f.LaunchGame()));
        _nav.Controls.Add(Ui.Secondary("Make another skin", (_, _) => _f.ShowWizard()));
        _nav.Controls.Add(Ui.Secondary("Back to home", (_, _) => _f.ShowHome()));
    }
}
