using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>Pictures made from a 64×64 skin: a flat front view for tiles, and the editing guide.</summary>
public static class SkinRender
{
    /// <summary>16×32 flat front view: head+hat, body, slim arms, legs (front faces only).
    /// <paramref name="portraitFace"/> uses the portrait square instead, which has the eyes and mouth
    /// drawn in (the head texture has none: the game animates them).</summary>
    public static RgbaImage Front(RgbaImage s, bool portraitFace = false)
    {
        var o = new RgbaImage(16, 32);
        void Put(int sx, int sy, int w, int h, int dx, int dy) => o.Composite(s.Crop(sx, sy, w, h), dx, dy);
        if (portraitFace)
            Put(SkinLayout.Portrait.X, SkinLayout.Portrait.Y, 8, 8, 4, 0);
        else
        {
            Put(8, 8, 8, 8, 4, 0);     // head front
            Put(40, 8, 8, 8, 4, 0);    // hat front
        }
        Put(20, 20, 8, 12, 4, 8);  // body front
        Put(44, 20, 3, 12, 1, 8);  // right arm (viewer's left)
        Put(36, 52, 3, 12, 12, 8); // left arm
        Put(4, 20, 4, 12, 4, 20);  // right leg
        Put(20, 52, 4, 12, 8, 20); // left leg
        return o;
    }

    static readonly uint White = RgbaImage.Pack(255, 255, 255, 255);
    static readonly uint Black = RgbaImage.Pack(0, 0, 0, 255);
    static uint ColourOf(RegionKind k) => k switch
    {
        RegionKind.Base => RgbaImage.Pack(255, 255, 255, 255),
        RegionKind.Overlay => RgbaImage.Pack(80, 220, 255, 255),
        RegionKind.Unused => RgbaImage.Pack(140, 140, 140, 255),
        RegionKind.Locked => RgbaImage.Pack(255, 70, 70, 255),
        _ => RgbaImage.Pack(255, 210, 40, 255),
    };

    const int GuideScale = 16;

    /// <summary>
    /// 1024×1184 guide: the skin at 16× (dimmed), every face outlined and labelled, plus a legend.
    /// Meant to sit next to the texture while editing.
    /// </summary>
    public static RgbaImage Guide(RgbaImage skin)
    {
        const int S = GuideScale;
        int legendH = 160;
        var g = new RgbaImage(64 * S, 64 * S + legendH);
        g.FillRect(0, 0, g.Width, g.Height, RgbaImage.Pack(24, 24, 28, 255));
        // checkerboard for transparency, then the skin, dimmed so labels stay readable
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                uint bg = ((x + y) & 1) == 0 ? RgbaImage.Pack(52, 52, 58, 255) : RgbaImage.Pack(44, 44, 50, 255);
                uint c = RgbaImage.Blend(bg, skin.Get(x, y));
                c = RgbaImage.Blend(c, RgbaImage.Pack(0, 0, 0, 110));
                g.FillRect(x * S, y * S, S, S, c);
            }

        foreach (var r in SkinLayout.All)
        {
            uint col = ColourOf(r.Kind);
            int x = r.X * S, y = r.Y * S, w = r.W * S, h = r.H * S;
            if (r.Kind is RegionKind.Unused or RegionKind.Locked)
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                        if (((i + j) / 6) % 4 == 0) g.Set(x + i, y + j, RgbaImage.Blend(g.Get(x + i, y + j), (col & 0x00FFFFFF) | 0x50000000));
            g.StrokeRect(x, y, w, h, col, 2);
            Label(g, r, col);
        }

        // legend
        int ly = 64 * S + 16;
        (RegionKind Kind, string Text)[] legend =
        [
            (RegionKind.Base, "WHITE: SHOWN ON THE HERO. PAINT HERE."),
            (RegionKind.Overlay, "BLUE: HAT LAYER, DRAWN OVER THE HEAD. TRANSPARENT = NOTHING."),
            (RegionKind.Unused, "GRAY: NOT USED BY THE GAME."),
            (RegionKind.Locked, "RED: FACE ANIMATION (EYES, BROWS, MOUTH). LEAVE AS IT IS."),
            (RegionKind.Portrait, "YELLOW: PORTRAIT PICTURE. EDIT IT TO MATCH YOUR NEW FACE."),
        ];
        foreach (var (kind, text) in legend)
        {
            g.FillRect(24, ly, 20, 20, ColourOf(kind));
            PixelFont.Draw(g, text, 56, ly + 3, 2, White);
            ly += 28;
        }
        return g;
    }

    static void Label(RgbaImage g, Region r, uint col)
    {
        const int S = GuideScale;
        int x = r.X * S, y = r.Y * S, w = r.W * S, h = r.H * S;
        string[] lines = r.Kind is RegionKind.Locked or RegionKind.Portrait ? r.Label.Split(' ') : [r.Part, r.Label];
        foreach (int scale in new[] { 2, 1 })
        {
            int lh = PixelFont.Height(scale) + 2 * scale;
            if (lines.All(l => PixelFont.Width(l, scale) <= w - 6) && lines.Length * lh <= h - 4)
            {
                int ty = y + 5;
                foreach (var l in lines)
                {
                    PixelFont.Draw(g, l, x + 5, ty, scale, col, Black);
                    ty += lh;
                }
                return;
            }
        }
        // too narrow for words: first letter of the face name
        PixelFont.Draw(g, r.Label[..1], x + 4, y + 4, 1, col, Black);
    }
}
