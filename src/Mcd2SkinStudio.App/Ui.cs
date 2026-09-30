using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.App;

/// <summary>Colours, fonts and small control factories shared by every page.</summary>
static class Ui
{
    public static readonly Color Background = Color.FromArgb(246, 246, 243);
    public static readonly Color Surface = Color.White;
    public static readonly Color Header = Color.FromArgb(32, 33, 36);
    public static readonly Color Text = Color.FromArgb(32, 33, 36);
    public static readonly Color Muted = Color.FromArgb(105, 108, 112);
    public static readonly Color Border = Color.FromArgb(222, 222, 218);
    public static readonly Color Accent = Color.FromArgb(60, 133, 39);        // grass green
    public static readonly Color AccentDark = Color.FromArgb(44, 102, 28);
    public static readonly Color Good = Color.FromArgb(226, 243, 220);
    public static readonly Color Warn = Color.FromArgb(255, 240, 205);
    public static readonly Color Bad = Color.FromArgb(252, 225, 222);
    public static readonly Color Selected = Color.FromArgb(214, 236, 204);

    public static Font Title => new("Segoe UI Semibold", 18f);
    public static Font H2 => new("Segoe UI Semibold", 13f);
    public static Font Body => new("Segoe UI", 10f);
    public static Font Small => new("Segoe UI", 9f);
    public static Font Bold => new("Segoe UI Semibold", 10f);

    public static Button Primary(string text, EventHandler onClick)
    {
        var b = Secondary(text, onClick);
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderColor = AccentDark;
        b.FlatAppearance.MouseOverBackColor = AccentDark;
        b.Font = Bold;
        return b;
    }

    public static Button Secondary(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Text,
            Font = Body,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 0, 8, 8),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 238, 234);
        b.Click += onClick;
        return b;
    }

    public static Label Label(string text, Font? font = null, Color? colour = null, int maxWidth = 0) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Body,
        ForeColor = colour ?? Text,
        MaximumSize = new Size(maxWidth, 0),
        Margin = new Padding(0, 0, 0, 6),
    };

    public static LinkLabel Link(string text, Action onClick)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, Font = Body, LinkColor = AccentDark, ActiveLinkColor = Accent, Margin = new Padding(0, 0, 12, 6) };
        l.LinkClicked += (_, _) => onClick();
        return l;
    }

    public static FlowLayoutPanel Column(Padding? padding = null) => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = padding ?? Padding.Empty,
        Margin = Padding.Empty,
    };

    public static FlowLayoutPanel Row() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = Padding.Empty,
    };

    /// <summary>A white rounded-looking card with a thin border.</summary>
    public static Panel Card(Control content)
    {
        var p = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Surface, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 12) };
        p.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, Border, ButtonBorderStyle.Solid);
        content.Location = new Point(p.Padding.Left, p.Padding.Top);
        p.Controls.Add(content);
        return p;
    }

    /// <summary>Crisp (nearest-neighbour) bitmap of a pixel image, scaled up, on a light checkerboard.</summary>
    public static Bitmap ToBitmap(RgbaImage img, int scale, bool checker = true)
    {
        var up = img.ScaleNearest(scale);
        var bmp = new Bitmap(up.Width, up.Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, up.Width, up.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var buf = new byte[up.Width * up.Height * 4];
        int cell = Math.Max(4, scale * 2);
        for (int y = 0; y < up.Height; y++)
            for (int x = 0; x < up.Width; x++)
            {
                uint c = up.Get(x, y);
                if (checker)
                {
                    uint bg = ((x / cell + y / cell) & 1) == 0 ? RgbaImage.Pack(236, 236, 232, 255) : RgbaImage.Pack(248, 248, 245, 255);
                    c = RgbaImage.Blend(bg, c);
                }
                var (r, g, b, a) = RgbaImage.Unpack(c);
                int i = (y * up.Width + x) * 4;
                buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = a;
            }
        for (int y = 0; y < up.Height; y++)
            Marshal.Copy(buf, y * up.Width * 4, data.Scan0 + y * data.Stride, up.Width * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    public static PictureBox Picture(Image img) => new()
    {
        Image = img,
        SizeMode = PictureBoxSizeMode.AutoSize,
        Margin = new Padding(0, 0, 12, 6),
    };

    public static Icon? AppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { return null; }
    }

    public static Image? Logo(int size)
    {
        using var s = typeof(Ui).Assembly.GetManifestResourceStream("icon256.png");
        if (s == null) return null;
        using var src = Image.FromStream(s);
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(src, 0, 0, size, size);
        return bmp;
    }
}
