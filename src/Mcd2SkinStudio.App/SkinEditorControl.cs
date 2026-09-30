using System.Drawing.Drawing2D;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.App;

enum Tool { Brush, Bucket, Eraser }

/// <summary>
/// The built-in editor: tools and colours on the left, the body part being painted in the middle
/// (one page per part, unfolded like a paper model), and a live front/back/face preview on the right.
/// </summary>
sealed class SkinEditorControl : Control
{
    static readonly int LeftW = Ui.Px(226), RightW = Ui.Px(214), Gap = Ui.Px(18);

    static readonly (EditorView View, string Title, string Hint)[] Views =
    [
        (EditorView.Head, "Head", "The head, unfolded: the big middle square is the face. The eyes, eyebrows and mouth are drawn by the game on top: change them under “Face & portrait”."),
        (EditorView.Hat, "Hat", "The hat layer is drawn over the head, slightly bigger. Empty (checkered) squares show the head underneath; the eraser empties squares again."),
        (EditorView.Body, "Body", "The body, unfolded: the big middle part is the front. Right and left are the hero's own right and left."),
        (EditorView.Arms, "Arms", "Both arms, unfolded. The hero's right arm is on the left, as when you face the hero. Outside is the side away from the body."),
        (EditorView.Legs, "Legs", "Both legs, unfolded. The hero's right leg is on the left, as when you face the hero. Outside is the side away from the other leg."),
        (EditorView.Face, "Face & portrait", "The game draws and animates the face from these squares: pupils, eye whites and mouth are the three lines; the eyebrow shape is drawn above both eyes (mirrored for the other eye)."),
    ];

    static readonly Dictionary<Tool, string> ToolHints = new()
    {
        [Tool.Brush] = "Click or drag to paint squares.",
        [Tool.Bucket] = "Click to fill all touching squares of the same colour.",
        [Tool.Eraser] = "Hat and face squares become empty. Other squares get the game's colour back.",
    };

    readonly SkinEditor _ed;
    readonly PixelCanvas _canvas;
    readonly ColourPicker _picker = new() { Margin = new Padding(0, 0, 0, 10) };
    readonly PreviewBox _front = new(Ui.Px(6)), _back = new(Ui.Px(6)), _face = new(Ui.Px(12));
    readonly Label _hint, _toolHint, _pointing;
    readonly FlowLayoutPanel _left = Ui.Column(), _right = Ui.Column(), _tabs = Ui.Row();
    readonly Dictionary<Tool, ToolButton> _tools = [];
    readonly Dictionary<EditorView, Button> _tabButtons = [];
    readonly Button _undo, _redo;
    readonly ToolTip _tips = new();
    Tool _tool = Tool.Brush;
    EditorView _view = EditorView.Head;

    public SkinEditorControl(SkinEditor editor)
    {
        _ed = editor;
        BackColor = Ui.Background;
        _canvas = new PixelCanvas(editor);

        // left: tools, colour, undo
        _left.AutoSize = false;
        _left.AutoScroll = true;
        _left.Controls.Add(Ui.Label("Tools", Ui.Bold));
        var toolRow = Ui.Row();
        toolRow.WrapContents = false;
        foreach (var (t, name, key) in new[] { (Tool.Brush, "Brush", "B"), (Tool.Bucket, "Fill", "F"), (Tool.Eraser, "Eraser", "E") })
        {
            var b = new ToolButton(name, PixelIcons.For(t));
            b.Click += (_, _) => SetTool(t);
            _tips.SetToolTip(b, $"{name} ({key})");
            _tools[t] = b;
            toolRow.Controls.Add(b);
        }
        _left.Controls.Add(toolRow);
        _toolHint = Ui.Label("", Ui.Small, Ui.Muted, LeftW - 12);
        _toolHint.MinimumSize = new Size(0, Ui.Px(34));
        _left.Controls.Add(_toolHint);
        _left.Controls.Add(Ui.Label("Colour", Ui.Bold));
        _left.Controls.Add(_picker);
        _picker.ColourChanged += _ => { if (_tool == Tool.Eraser) SetTool(Tool.Brush); };
        _left.Controls.Add(Ui.Label("Tip: right-click a square to pick up its colour.", Ui.Small, Ui.Muted, LeftW - 12));

        // middle: part tabs, hint, canvas
        foreach (var (v, title, _) in Views)
        {
            var b = Ui.Secondary(title, (_, _) => ShowView(v));
            b.Margin = new Padding(0, 0, 6, 6);
            b.UseMnemonic = false;
            _tabButtons[v] = b;
            _tabs.Controls.Add(b);
        }
        _hint = Ui.Label("", Ui.Small, Ui.Muted);
        _hint.AutoSize = false;
        _hint.UseMnemonic = false;

        // right: live preview
        _right.AutoSize = false;
        var undoRow = Ui.Row();
        undoRow.WrapContents = false;
        _undo = Ui.Secondary("Undo", (_, _) => _ed.Undo());
        _redo = Ui.Secondary("Redo", (_, _) => _ed.Redo());
        _tips.SetToolTip(_undo, "Undo (Ctrl+Z)");
        _tips.SetToolTip(_redo, "Redo (Ctrl+Y)");
        undoRow.Controls.AddRange([_undo, _redo]);
        _right.Controls.Add(undoRow);
        var startOver = Ui.Link("Start over from the game's look", () => _ed.Reset());
        startOver.Margin = new Padding(0, 0, 0, 14);
        _right.Controls.Add(startOver);
        _right.Controls.Add(Ui.Label("Live preview", Ui.Bold));
        var pair = Ui.Row();
        pair.WrapContents = false;
        pair.Controls.Add(Captioned(_front, "Front"));
        pair.Controls.Add(Captioned(_back, "Back"));
        _right.Controls.Add(pair);
        _right.Controls.Add(Ui.Label("In-game face", Ui.Bold));
        _right.Controls.Add(_face);
        _pointing = Ui.Label("", Ui.Small, Ui.Text, RightW - 8);
        _pointing.MinimumSize = new Size(0, Ui.Px(52));
        _right.Controls.Add(_pointing);
        _right.Controls.Add(Ui.Label("Click the preview to jump to that part.", Ui.Small, Ui.Muted, RightW - 8));
        _front.PixelClicked += (x, y) => JumpTo(SkinRender.PartAt(true, x, y));
        _back.PixelClicked += (x, y) => JumpTo(SkinRender.PartAt(false, x, y));
        _face.PixelClicked += (_, _) => ShowView(EditorView.Face);
        _face.Margin = new Padding(0, 0, 0, 8);

        Controls.AddRange([_left, _tabs, _hint, _canvas, _right]);

        _canvas.Stroke += OnStroke;
        _canvas.Pick += c =>
        {
            uint px = _ed.Skin.Get(c.Tx, c.Ty);
            if (px >> 24 == 0) return;
            _picker.Colour = px;
            if (_tool == Tool.Eraser) SetTool(Tool.Brush);
        };
        _canvas.HoverChanged += OnHover;
        _ed.Changed += Refresh;

        SetTool(Tool.Brush);
        ShowView(EditorView.Head);
        Refresh();
        OnHover(null);
    }

    static Control Captioned(Control c, string caption)
    {
        var col = Ui.Column();
        col.Margin = new Padding(0, 0, 12, 8);
        col.Controls.Add(c);
        col.Controls.Add(Ui.Label(caption, Ui.Small, Ui.Muted));
        return col;
    }

    public EditorView View => _view;

    public void ShowView(EditorView v)
    {
        _view = v;
        _canvas.Page = EditorLayout.Page(v);
        _hint.Text = Views.First(x => x.View == v).Hint;
        foreach (var (view, b) in _tabButtons)
        {
            bool on = view == v;
            b.BackColor = on ? Ui.Selected : Ui.Surface;
            b.FlatAppearance.BorderColor = on ? Ui.Accent : Ui.Border;
            b.Font = on ? Ui.Bold : Ui.Body;
        }
        PerformLayout();
    }

    void JumpTo(Part? p)
    {
        if (p == null) return;
        ShowView(p switch
        {
            Part.Head or Part.Hat => EditorView.Head,
            Part.Body => EditorView.Body,
            Part.RightArm or Part.LeftArm => EditorView.Arms,
            _ => EditorView.Legs,
        });
    }

    void SetTool(Tool t)
    {
        _tool = t;
        foreach (var (tool, b) in _tools) b.Selected = tool == t;
        _toolHint.Text = ToolHints[t];
        _canvas.Tool = t;
    }

    void OnStroke(EditorCell c, bool first)
    {
        switch (_tool)
        {
            case Tool.Brush: _ed.Paint(c.Tx, c.Ty, _picker.Colour); break;
            case Tool.Eraser: _ed.Erase(c.Tx, c.Ty); break;
            case Tool.Bucket when first: _ed.Fill(_canvas.Page, c, _picker.Colour); break;
        }
    }

    void OnHover(EditorCell? c)
    {
        if (c == null)
        {
            _pointing.Text = "Point at a square to see where it is on your hero.";
            _front.Marks = _back.Marks = _face.Marks = [];
        }
        else
        {
            var spots = SkinRender.Where(c.Tx, c.Ty).ToList();
            _front.Marks = [.. spots.Where(s => s.Front).Select(s => (s.X, s.Y))];
            _back.Marks = [.. spots.Where(s => !s.Front).Select(s => (s.X, s.Y))];
            _face.Marks = [.. spots.Where(s => s.Front && s.X >= 4 && s.X < 12 && s.Y < 8).Select(s => (s.X - 4, s.Y))];
            _pointing.Text = c.Name + (spots.Count == 0 ? "\n(on a side, top or bottom: not visible from the front or back)" : "");
        }
    }

    new void Refresh()
    {
        var s = _ed.Skin;
        _front.Image = SkinRender.Front(s);
        _back.Image = SkinRender.Back(s);
        _face.Image = SkinGeometry.FaceAsSeen(s);
        _undo.Enabled = _ed.CanUndo;
        _redo.Enabled = _ed.CanRedo;
        _canvas.Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_canvas == null) return;
        _left.Bounds = new Rectangle(0, 0, LeftW, Height);
        _right.Bounds = new Rectangle(Width - RightW, 0, RightW, Height);
        int x = LeftW + Gap, w = Math.Max(200, Width - LeftW - RightW - 2 * Gap);
        _tabs.MaximumSize = new Size(w, 0);
        _tabs.Location = new Point(x, 0);
        var hintSize = TextRenderer.MeasureText(_hint.Text, _hint.Font, new Size(w, 0), TextFormatFlags.WordBreak);
        _hint.Bounds = new Rectangle(x, _tabs.Bottom + 2, w, hintSize.Height + 4);
        int top = _hint.Bottom + 8;
        _canvas.Bounds = new Rectangle(x, top, w, Math.Max(120, Height - top));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveTextBox() == null)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: _ed.Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: _ed.Redo(); return true;
                case Keys.B: SetTool(Tool.Brush); return true;
                case Keys.F: SetTool(Tool.Bucket); return true;
                case Keys.E: SetTool(Tool.Eraser); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    TextBoxBase? ActiveTextBox()
    {
        Control? c = FindForm()?.ActiveControl;
        while (c is ContainerControl cc && cc.ActiveControl != null) c = cc.ActiveControl;
        return c as TextBoxBase;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _canvas.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ed.Changed -= Refresh;
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Draws one editor page and turns mouse input into strokes on its squares.</summary>
sealed class PixelCanvas : Control
{
    static readonly int MaxUnit = Ui.Px(40);
    static readonly Color Grid = Color.FromArgb(200, 200, 194);
    static readonly Color CheckLight = Color.FromArgb(252, 252, 250), CheckDark = Color.FromArgb(222, 222, 216);

    readonly SkinEditor _ed;
    EditorPage _page = EditorLayout.Page(EditorView.Head);
    EditorCell? _hover, _last;
    Point _lastPt;
    bool _drawing;
    int _u;
    Point _o;

    /// <summary>A square under the mouse while painting; first = the square the click started on.</summary>
    public event Action<EditorCell, bool>? Stroke;
    public event Action<EditorCell>? Pick;
    public event Action<EditorCell?>? HoverChanged;

    public Tool Tool { get; set; }

    public PixelCanvas(SkinEditor ed)
    {
        _ed = ed;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Ui.Background;
        TabStop = true;
    }

    public EditorPage Page
    {
        get => _page;
        set
        {
            _page = value;
            SetHover(null);
            Measure();
            Invalidate();
        }
    }

    void Measure()
    {
        _u = Math.Clamp(Math.Min((Width - 4) / _page.Width, (Height - 4) / _page.Height), 4, MaxUnit);
        _o = new Point(Math.Max(2, (Width - _u * _page.Width) / 2), 2);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Measure();
    }

    Rectangle Rect(int x, int y, int w, int h) => new(_o.X + x * _u, _o.Y + y * _u, w * _u, h * _u);

    EditorCell? CellAt(Point p) => _page.CellAt((p.X - _o.X) / (double)_u, (p.Y - _o.Y) / (double)_u);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var skin = _ed.Skin;
        using var light = new SolidBrush(CheckLight);
        using var dark = new SolidBrush(CheckDark);
        using var grid = new Pen(Grid);
        foreach (var c in _page.Cells)
        {
            var r = Rect(c.X, c.Y, c.Size, c.Size);
            var (cr, cg, cb, ca) = RgbaImage.Unpack(skin.Get(c.Tx, c.Ty));
            if (ca < 255)
            {
                g.FillRectangle(light, r);
                int h = r.Width / 2;
                g.FillRectangle(dark, r.X, r.Y, h, h);
                g.FillRectangle(dark, r.X + h, r.Y + h, r.Width - h, r.Height - h);
            }
            if (ca > 0)
                using (var br = new SolidBrush(Color.FromArgb(ca, cr, cg, cb))) g.FillRectangle(br, r);
            g.DrawRectangle(grid, r);
        }
        foreach (var o in _page.Outlines)
        {
            var colour = o.Kind switch
            {
                OutlineKind.Hat => Color.FromArgb(0, 140, 200),
                OutlineKind.Animation => Color.FromArgb(200, 60, 60),
                OutlineKind.Portrait => Color.FromArgb(200, 150, 0),
                _ => Color.FromArgb(60, 60, 66),
            };
            using var pen = new Pen(colour, 2);
            g.DrawRectangle(pen, Rect(o.X, o.Y, o.W, o.H));
        }
        foreach (var l in _page.Labels) DrawLabel(g, l);
        if (_hover != null)
        {
            var r = Rect(_hover.X, _hover.Y, _hover.Size, _hover.Size);
            using var outer = new Pen(Color.FromArgb(20, 20, 20), 3);
            g.DrawRectangle(outer, r);
            g.DrawRectangle(Pens.White, Rectangle.Inflate(r, -2, -2));
        }
    }

    void DrawLabel(Graphics g, EditorLabel l)
    {
        var box = Rect(l.X, l.Y, l.W, l.H);
        float size = Math.Clamp(_u * (l.Title ? 0.62f : 0.55f) / Ui.Scale, 7f, l.Title ? 12f : 10f);
        Size text;
        Font font;
        while (true)
        {
            font = new Font("Segoe UI" + (l.Title ? " Semibold" : ""), size, GraphicsUnit.Point);
            text = TextRenderer.MeasureText(g, l.Text, font, Size.Empty, TextFormatFlags.NoPadding);
            if (text.Width <= box.Width - 6 || size <= 6.5f) break;
            font.Dispose();
            size -= 0.5f;
        }
        int y = l.Bottom ? box.Bottom - text.Height - 4 : box.Top + 3;
        TextRenderer.DrawText(g, l.Text, font, new Point(box.X + 3, y), l.Title ? Ui.Text : Ui.Muted, TextFormatFlags.NoPadding);
        font.Dispose();
    }

    void SetHover(EditorCell? c)
    {
        if (c == _hover) return;
        _hover = c;
        Cursor = c != null ? Cursors.Cross : Cursors.Default;
        Invalidate();
        HoverChanged?.Invoke(c);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var c = CellAt(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (c != null) Pick?.Invoke(c);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        _drawing = true;
        Capture = true;
        _ed.BeginStroke();
        _lastPt = e.Location;
        _last = c;
        if (c != null) Stroke?.Invoke(c, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHover(CellAt(e.Location));
        if (!_drawing || Tool == Tool.Bucket) return;
        // walk from the last mouse position so fast drags don't leave gaps
        int dx = e.X - _lastPt.X, dy = e.Y - _lastPt.Y;
        int steps = Math.Max(1, Math.Max(Math.Abs(dx), Math.Abs(dy)) * 3 / Math.Max(1, _u));
        for (int i = 1; i <= steps; i++)
        {
            var p = new Point(_lastPt.X + dx * i / steps, _lastPt.Y + dy * i / steps);
            var c = CellAt(p);
            if (c != null && c != _last) Stroke?.Invoke(c, false);
            _last = c;
        }
        _lastPt = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_drawing) return;
        _drawing = false;
        Capture = false;
        _ed.EndStroke();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_drawing) SetHover(null);
    }
}

/// <summary>A picture of the hero, scaled up, with highlighted pixels; reports which pixel was clicked.</summary>
sealed class PreviewBox : Control
{
    readonly int _scale;
    Bitmap? _bmp;
    RgbaImage? _image;
    IReadOnlyList<(int X, int Y)> _marks = [];

    public event Action<int, int>? PixelClicked;

    public PreviewBox(int scale)
    {
        _scale = scale;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Ui.Surface;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
    }

    public RgbaImage? Image
    {
        get => _image;
        set
        {
            _image = value;
            _bmp?.Dispose();
            _bmp = value == null ? null : Ui.ToBitmap(value, _scale, checker: false);
            if (value != null) Size = new Size(value.Width * _scale, value.Height * _scale);
            Invalidate();
        }
    }

    public IReadOnlyList<(int X, int Y)> Marks
    {
        get => _marks;
        set { _marks = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_bmp != null) g.DrawImageUnscaled(_bmp, 0, 0);
        using var outer = new Pen(Color.FromArgb(20, 20, 20), 3);
        using var inner = new Pen(Color.FromArgb(255, 214, 0), 1);
        foreach (var (x, y) in _marks)
        {
            var r = new Rectangle(x * _scale, y * _scale, _scale, _scale);
            g.DrawRectangle(outer, Rectangle.Inflate(r, 1, 1));
            g.DrawRectangle(inner, Rectangle.Inflate(r, -1, -1));
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_image != null) PixelClicked?.Invoke(e.X / _scale, e.Y / _scale);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bmp?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>A square tool button with a pixel-art icon; green when selected.</summary>
sealed class ToolButton : Button
{
    bool _selected;

    public ToolButton(string text, Image icon)
    {
        Text = text;
        Image = icon;
        TextImageRelation = TextImageRelation.ImageAboveText;
        Size = new Size(Ui.Px(66), Ui.Px(60));
        FlatStyle = FlatStyle.Flat;
        Font = Ui.Small;
        Margin = new Padding(0, 0, 4, 6);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        Selected = false;
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            BackColor = value ? Ui.Selected : Ui.Surface;
            ForeColor = value ? Ui.AccentDark : Ui.Text;
            FlatAppearance.BorderColor = value ? Ui.Accent : Ui.Border;
            FlatAppearance.BorderSize = value ? 2 : 1;
        }
    }
}

/// <summary>12×12 pixel-art tool icons, drawn at 2× so they match the blocky look.</summary>
static class PixelIcons
{
    static readonly Dictionary<char, Color> Palette = new()
    {
        ['K'] = Color.FromArgb(40, 40, 44),
        ['N'] = Color.FromArgb(150, 100, 50),
        ['G'] = Color.FromArgb(160, 160, 166),
        ['R'] = Color.FromArgb(214, 48, 48),
        ['B'] = Color.FromArgb(50, 120, 220),
        ['P'] = Color.FromArgb(236, 120, 150),
        ['W'] = Color.White,
    };

    static readonly string[] Brush =
    [
        "..........KK",
        ".........KNK",
        "........KNK.",
        ".......KNK..",
        "......KNK...",
        ".....KGK....",
        "....KGK.....",
        "..KKGK......",
        ".KRRK.......",
        ".KRRK.......",
        "KRRK........",
        "KK..........",
    ];

    static readonly string[] Bucket =
    [
        "............",
        "....KKKK....",
        "...K....K...",
        "..KKKKKKKK..",
        "..KBBBBBBK..",
        "..KBBBBBBKB.",
        "..KBBBBBBKB.",
        "..KBBBBBBK.B",
        "...KBBBBK...",
        "...KBBBBK...",
        "....KKKK....",
        "............",
    ];

    static readonly string[] Eraser =
    [
        "............",
        "......KKKK..",
        ".....KPPPPK.",
        "....KPPPPPPK",
        "...KWKPPPPK.",
        "..KWWWKPPK..",
        ".KWWWWWKK...",
        "KWWWWWWK....",
        ".KWWWWK.....",
        "..KWWK......",
        "...KK.......",
        "............",
    ];

    public static Image For(Tool t)
    {
        var rows = t switch { Tool.Brush => Brush, Tool.Bucket => Bucket, _ => Eraser };
        int S = Ui.Px(2);
        var bmp = new Bitmap(12 * S, 12 * S);
        using var g = Graphics.FromImage(bmp);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (Palette.TryGetValue(rows[y][x], out var c))
                    using (var b = new SolidBrush(c)) g.FillRectangle(b, x * S, y * S, S, S);
        return bmp;
    }
}
