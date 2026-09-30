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
        Heading($"Paint your {s.DisplayName}", "Your design sheet was saved to your Documents folder. Open it in Paint, paint inside the squares, and save it.");
        ExportResult? r = null;
        if (!await _f.Busy($"Making the {s.DisplayName} design sheet…", () =>
            {
                r = S.Export(s);
                _original = S.Original(s);
            }))
        {
            Nav(true, null);
            return;
        }
        _export = r;

        var files = Ui.Column();
        files.Controls.Add(Ui.Label("Saved to", Ui.Small, Ui.Muted));
        files.Controls.Add(Ui.Label(r!.Folder, Ui.Bold, null, W - 40));
        var list = $"{Path.GetFileName(r.Sheet)}  –  the design sheet: every body part unfolded and labelled, plus the face animation and the portrait. Paint this one.\n" +
                   $"{Path.GetFileName(r.Texture)}  –  the raw 64×64 texture, for experienced skin makers.";
        if (r.OriginalSheet != null) list += $"\n{Path.GetFileName(r.OriginalSheet)}  –  the game's original look, if you want to start over.";
        files.Controls.Add(Ui.Label(list, Ui.Small, Ui.Muted, W - 40));
        if (r.KeptEarlierWork)
            files.Controls.Add(Ui.Label("Your earlier painting on this design sheet was kept, so you can carry on where you stopped.", Ui.Small, Ui.AccentDark, W - 40));
        var buttons = Ui.Row();
        buttons.Margin = new Padding(0, 8, 0, 0);
        buttons.Controls.Add(Ui.Primary("Open design sheet in Paint", (_, _) => MainForm.OpenInPaint(r.Sheet)));
        buttons.Controls.Add(Ui.Secondary("Open folder", (_, _) => MainForm.Open(r.Folder)));
        files.Controls.Add(buttons);
        Add(Ui.Card(files));

        Add(Ui.Label("How the design sheet works", Ui.H2));
        var tips = Ui.Row();
        tips.MaximumSize = new Size(W, 0);
        tips.Controls.Add(Tip("Every square is one pixel", "Each body part is unfolded like a paper model: the big middle square is the front, the sides are next to it. Right and left are the hero's own right and left."));
        tips.Controls.Add(Tip("Eyes, eyebrows and mouth", "The head has no face drawn on it: the game animates the face from the “Face animation” squares. Pupils, eye whites and mouth are the three lines on the right; the eyebrow shape is on the left."));
        tips.Controls.Add(Tip("Portrait and hat", "The portrait is the small face picture in the Locker: paint a whole face there. The hat layer is drawn over the head; leave squares empty (checkered) for no hat."));
        tips.Controls.Add(Tip("Save as PNG, don't resize", "Paint on a new layer if you like (Layers button). Never resize or crop the sheet. File › Save as › PNG picture."));
        Add(tips);
        Nav(true, Ui.Primary("I've saved my design sheet  ›", (_, _) => Go(3)));
    }

    Control Tip(string title, string text)
    {
        var col = Ui.Column();
        col.Controls.Add(Ui.Label(title, Ui.Bold));
        col.Controls.Add(Ui.Label(text, Ui.Small, Ui.Muted, 204));
        var c = Ui.Card(col);
        c.Margin = new Padding(0, 0, 12, 12);
        c.MinimumSize = new Size(236, 0);
        return c;
    }

    // -------------------------------------------------------------- 4 Upload
    void StepUpload()
    {
        var s = _skin!;
        Heading("Upload your design sheet", $"Drop the design sheet you painted (or a 64×64 texture), or browse for it. You'll see your {s.DisplayName} next to the original before anything is installed.");
        var next = Ui.Primary("Install  ›", (_, _) => Go(4));
        next.Enabled = _edited != null;

        var drop = new Panel { Size = new Size(Math.Min(W, 720), 120), BackColor = Ui.Surface, AllowDrop = true, Margin = new Padding(0, 0, 0, 14) };
        drop.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Accent, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, 1, 1, drop.Width - 3, drop.Height - 3);
        };
        var dropText = Ui.Label("Drop your design sheet here", Ui.H2, Ui.AccentDark);
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
                var up = SkinImport.Load(path, _original!, s.Key);
                if (up.SheetMatchesSkin == false &&
                    !_f.Ask($"This design sheet was made for a different hero. Use it for {s.DisplayName} anyway?", "Different hero"))
                    return;
                var img = up.Skin;
                if (SkinImport.Unchanged(_original!, img))
                {
                    _f.Warn("This is still exactly the original skin. Paint your changes, save the file, then upload it again.");
                    return;
                }
                if (SkinImport.LostTransparency(_original!, img) is uint lost &&
                    _f.Ask("The empty (checkered) squares, such as the hat layer, were filled with one colour. Some paint programs do that when they save.\n\n" +
                           "Make those squares empty again? (recommended)\n\nYes: they stay invisible in the game.\nNo: keep the colour (the hero gets a solid hat box).", "Empty squares"))
                    img = SkinImport.RestoreTransparency(_original!, img, lost);
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
        col.Controls.Add(Ui.Label(title + "  (front and back)", Ui.H2));
        var row = Ui.Row();
        row.WrapContents = false;
        row.Controls.Add(Ui.Picture(Ui.ToBitmap(SkinRender.Front(img), 8, checker: false)));
        row.Controls.Add(Ui.Picture(Ui.ToBitmap(SkinRender.Back(img), 8, checker: false)));
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
