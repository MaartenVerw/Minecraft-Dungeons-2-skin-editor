using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.App;

/// <summary>Current colour swatch, hex code box, colour wheel (hue and saturation) with a brightness
/// slider, and preset colours.</summary>
sealed class ColourPicker : Control
{
    const int PresetCols = 8;
    static readonly int Wheel = Ui.Px(128), SliderW = Ui.Px(18), Swatch = Ui.Px(20), Pitch = Ui.Px(25);
    static readonly int WheelY = Ui.Px(50), PresetY = WheelY + Wheel + Ui.Px(26);

    static readonly string[] Presets =
    [
        "000000", "3B3B3B", "6B6B6B", "9E9E9E", "C8C8C8", "FFFFFF", "5A3A22", "8B5A2B",
        "3B2219", "6B4226", "A0673C", "C68642", "E0AC69", "F1C27D", "FFDBAC", "D9A066",
        "7A1E1E", "C62828", "F44336", "FF7043", "FF9800", "FFC107", "FFEB3B", "FFF59D",
        "1B5E20", "43A047", "8BC34A", "00897B", "00BCD4", "1E88E5", "0D47A1", "3949AB",
        "4A148C", "7E57C2", "AB47BC", "EC407A", "F48FB1", "FCE4EC", "B0BEC5", "37474F",
    ];

    readonly TextBox _hex;
    readonly Bitmap _wheel = RenderWheel();
    double _h, _s = 1, _v = 1;
    uint _colour;
    bool _updatingHex;
    enum Drag { None, Wheel, Slider }
    Drag _drag;

    public event Action<uint>? ColourChanged;

    public ColourPicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(PresetCols * Pitch, PresetY + Ui.Px(18) + Presets.Length / PresetCols * Pitch);
        BackColor = Ui.Background;
        _hex = new TextBox { Location = new Point(Ui.Px(52), Ui.Px(20)), Width = Ui.Px(100), Font = Ui.Body, MaxLength = 7 };
        _hex.TextChanged += (_, _) =>
        {
            if (!_updatingHex && ParseHex(_hex.Text) is uint c) SetColour(c, fromHex: true);
        };
        _hex.Leave += (_, _) => ShowHex();
        _hex.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ShowHex();
            e.SuppressKeyPress = true;
        };
        Controls.Add(_hex);
        Colour = RgbaImage.Pack(0xC6, 0x28, 0x28, 255);
    }

    /// <summary>The chosen colour (always opaque).</summary>
    public uint Colour
    {
        get => _colour;
        set => SetColour(value, fromHex: false);
    }

    void SetColour(uint c, bool fromHex)
    {
        var (r, g, b, _) = RgbaImage.Unpack(c);
        _colour = RgbaImage.Pack(r, g, b, 255);
        var (h, s, v) = ToHsv(r, g, b);
        if (v > 0 && s > 0) _h = h;       // keep the hue on greys and black so the wheel doesn't jump
        if (v > 0) _s = s;
        _v = v;
        if (!fromHex) ShowHex();
        Invalidate();
        ColourChanged?.Invoke(_colour);
    }

    void SetFromHsv()
    {
        var (r, g, b) = FromHsv(_h, _s, _v);
        _colour = RgbaImage.Pack(r, g, b, 255);
        ShowHex();
        Invalidate();
        ColourChanged?.Invoke(_colour);
    }

    void ShowHex()
    {
        var (r, g, b, _) = RgbaImage.Unpack(_colour);
        _updatingHex = true;
        _hex.Text = $"#{r:X2}{g:X2}{b:X2}";
        _updatingHex = false;
    }

    internal static uint? ParseHex(string text)
    {
        var t = text.Trim().TrimStart('#');
        if (t.Length == 3) t = string.Concat(t.Select(ch => $"{ch}{ch}"));
        if (t.Length != 6 || !uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        return RgbaImage.Pack((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
    }

    // ------------------------------------------------------------------ drawing
    static Rectangle WheelRect => new(0, WheelY, Wheel, Wheel);
    static Rectangle SliderRect => new(Wheel + Ui.Px(14), WheelY, SliderW, Wheel);
    static Rectangle PresetRect(int i) => new(i % PresetCols * Pitch, PresetY + Ui.Px(18) + i / PresetCols * Pitch, Swatch, Swatch);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using var border = new Pen(Ui.Border);
        var (r, gg, b, _) = RgbaImage.Unpack(_colour);
        using (var br = new SolidBrush(Color.FromArgb(r, gg, b))) g.FillRectangle(br, 0, 2, Ui.Px(42), Ui.Px(40));
        g.DrawRectangle(border, 0, 2, Ui.Px(42), Ui.Px(40));
        TextRenderer.DrawText(g, "Hex code", Ui.Small, new Point(Ui.Px(49), 0), Ui.Muted);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(_wheel, WheelRect.Location);
        double a = _h * Math.PI / 180, rad = _s * (Wheel / 2.0 - 1);
        float mx = (float)(Wheel / 2.0 + Math.Cos(a) * rad), my = (float)(WheelY + Wheel / 2.0 - Math.Sin(a) * rad);
        using (var dark = new Pen(Color.FromArgb(30, 30, 30), 2.5f)) g.DrawEllipse(dark, mx - 6, my - 6, 12, 12);
        using (var light = new Pen(Color.White, 1.5f)) g.DrawEllipse(light, mx - 4.5f, my - 4.5f, 9, 9);
        g.SmoothingMode = SmoothingMode.None;

        var sr = SliderRect;
        var (tr, tg, tb) = FromHsv(_h, _s, 1);
        using (var grad = new LinearGradientBrush(sr, Color.FromArgb(tr, tg, tb), Color.Black, LinearGradientMode.Vertical))
            g.FillRectangle(grad, sr);
        g.DrawRectangle(border, sr);
        int sy = sr.Top + (int)Math.Round((1 - _v) * (sr.Height - 1));
        using (var dark = new Pen(Color.FromArgb(30, 30, 30), 3)) g.DrawRectangle(dark, sr.Left - 3, sy - 2, sr.Width + 6, 4);
        using (var light = new Pen(Color.White, 1)) g.DrawRectangle(light, sr.Left - 2, sy - 1, sr.Width + 4, 2);
        TextRenderer.DrawText(g, "Light", Ui.Small, new Point(sr.Right + 4, sr.Top - 2), Ui.Muted);
        TextRenderer.DrawText(g, "Dark", Ui.Small, new Point(sr.Right + 4, sr.Bottom - Ui.Px(16)), Ui.Muted);

        TextRenderer.DrawText(g, "Presets", Ui.Small, new Point(0, PresetY), Ui.Muted);
        for (int i = 0; i < Presets.Length; i++)
        {
            var c = ParseHex(Presets[i])!.Value;
            var (pr, pg, pb, _) = RgbaImage.Unpack(c);
            var rc = PresetRect(i);
            using (var br = new SolidBrush(Color.FromArgb(pr, pg, pb))) g.FillRectangle(br, rc);
            g.DrawRectangle(c == _colour ? Pens.Black : border, rc);
            if (c == _colour) g.DrawRectangle(Pens.White, Rectangle.Inflate(rc, -1, -1));
        }
    }

    // -------------------------------------------------------------------- mouse
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (Rectangle.Inflate(WheelRect, 4, 4).Contains(e.Location)) _drag = Drag.Wheel;
        else if (Rectangle.Inflate(SliderRect, 6, 4).Contains(e.Location)) _drag = Drag.Slider;
        else
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Rectangle.Inflate(PresetRect(i), 2, 2).Contains(e.Location)) Colour = ParseHex(Presets[i])!.Value;
            return;
        }
        Capture = true;
        DragTo(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag != Drag.None) DragTo(e.Location);
        else
        {
            bool hot = WheelRect.Contains(e.Location) || SliderRect.Contains(e.Location) ||
                       Enumerable.Range(0, Presets.Length).Any(i => PresetRect(i).Contains(e.Location));
            Cursor = hot ? Cursors.Hand : Cursors.Default;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = Drag.None;
        Capture = false;
    }

    void DragTo(Point p)
    {
        if (_drag == Drag.Wheel)
        {
            double dx = p.X - Wheel / 2.0, dy = WheelY + Wheel / 2.0 - p.Y, R = Wheel / 2.0 - 1;
            _h = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            _s = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / R);
            if (_v < 0.05) _v = 1;      // picking a colour on the wheel while black is chosen: make it visible
        }
        else
        {
            var sr = SliderRect;
            _v = Math.Clamp(1 - (p.Y - sr.Top) / (double)(sr.Height - 1), 0, 1);
        }
        SetFromHsv();
    }

    // --------------------------------------------------------------- colour math
    static Bitmap RenderWheel()
    {
        var bmp = new Bitmap(Wheel, Wheel, PixelFormat.Format32bppArgb);
        var buf = new byte[Wheel * Wheel * 4];
        double c = Wheel / 2.0, R = c - 1;
        for (int y = 0; y < Wheel; y++)
            for (int x = 0; x < Wheel; x++)
            {
                double dx = x + 0.5 - c, dy = c - (y + 0.5), r = Math.Sqrt(dx * dx + dy * dy);
                double alpha = Math.Clamp(R + 0.5 - r, 0, 1);
                if (alpha <= 0) continue;
                var (cr, cg, cb) = FromHsv((Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360, Math.Min(1, r / R), 1);
                int i = (y * Wheel + x) * 4;
                buf[i] = cb; buf[i + 1] = cg; buf[i + 2] = cr; buf[i + 3] = (byte)(alpha * 255);
            }
        var data = bmp.LockBits(new Rectangle(0, 0, Wheel, Wheel), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < Wheel; y++) Marshal.Copy(buf, y * Wheel * 4, data.Scan0 + y * data.Stride, Wheel * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    internal static (byte R, byte G, byte B) FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static byte B(double d) => (byte)Math.Round(Math.Clamp(d, 0, 1) * 255);
        return (B(r + m), B(g + m), B(b + m));
    }

    internal static (double H, double S, double V) ToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), d = max - min;
        double h = d == 0 ? 0 : max == rf ? 60 * ((gf - bf) / d % 6) : max == gf ? 60 * ((bf - rf) / d + 2) : 60 * ((rf - gf) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _wheel.Dispose();
        base.Dispose(disposing);
    }
}
