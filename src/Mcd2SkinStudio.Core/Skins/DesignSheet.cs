using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>
/// The editable design sheet: every body part unfolded like a paper model (front in the middle,
/// faces oriented as seen from outside, so strokes continue across edges on the hero), the face
/// animation squares, the portrait and a read-only in-game face preview. Every square is one texel;
/// labels only sit outside the squares. <see cref="Read"/> turns a painted sheet back into the 64×64 skin.
/// </summary>
public static class DesignSheet
{
    public const int Width = 1200;
    const int S = 16;              // body part squares
    const int Big = 32;            // face animation squares
    const int PortraitCell = 24;
    const int PreviewCell = 12;
    const int M = 40;
    const int Gap = 96;

    /// <summary>One editable square: top-left pixel, size, and the texel it becomes.</summary>
    public sealed record Cell(int X, int Y, int Size, int Tx, int Ty);

    sealed record Net(Part Part, string Title, int X, int Y);

    sealed class LayoutData
    {
        public readonly List<Net> Nets = [];
        public readonly List<Cell> Cells = [];
        public int Row4Y, AnimX, AnimY, PortraitX, PreviewX, Height;
    }

    static readonly Lazy<LayoutData> Layout = new(BuildLayout);

    public static int Height => Layout.Value.Height;
    public static IReadOnlyList<Cell> Cells => Layout.Value.Cells;

    static (int W, int H, int D) Dims(Part p)
    {
        var front = SkinGeometry.Get(p, Face.Front);
        return (front.W, front.H, SkinGeometry.Get(p, Face.Right).W);
    }

    /// <summary>Face rectangles inside a net, in squares: TOP above FRONT; RIGHT, FRONT, LEFT, BACK in a row; BOTTOM below FRONT.</summary>
    static (int X, int Y) FaceOrigin(Part p, Face f)
    {
        var (w, h, d) = Dims(p);
        return f switch
        {
            Face.Top => (d, 0),
            Face.Right => (0, d),
            Face.Front => (d, d),
            Face.Left => (d + w, d),
            Face.Back => (2 * d + w, d),
            _ => (d, d + h),
        };
    }

    static LayoutData BuildLayout()
    {
        var L = new LayoutData();
        int y1 = 254, y2 = 712, y3 = 1106;
        L.Nets.Add(new(Part.Head, "HEAD", M, y1));
        L.Nets.Add(new(Part.Hat, "HAT LAYER (DRAWN OVER THE HEAD)", M + 32 * S + Gap, y1));
        L.Nets.Add(new(Part.RightArm, "RIGHT ARM", 88, y2));
        L.Nets.Add(new(Part.Body, "BODY", 88 + 14 * S + Gap, y2));
        L.Nets.Add(new(Part.LeftArm, "LEFT ARM", 88 + 14 * S + Gap + 24 * S + Gap, y2));
        L.Nets.Add(new(Part.RightLeg, "RIGHT LEG", 600 - 48 - 16 * S, y3));
        L.Nets.Add(new(Part.LeftLeg, "LEFT LEG", 600 + 48, y3));
        foreach (var n in L.Nets)
            foreach (var f in Enum.GetValues<Face>())
            {
                var map = SkinGeometry.Get(n.Part, f);
                var (fx, fy) = FaceOrigin(n.Part, f);
                for (int r = 0; r < map.H; r++)
                    for (int c = 0; c < map.W; c++)
                    {
                        var (tx, ty) = map.Texel(c, r);
                        L.Cells.Add(new(n.X + (fx + c) * S, n.Y + (fy + r) * S, S, tx, ty));
                    }
            }

        L.Row4Y = 1466;
        L.AnimX = M;
        L.AnimY = L.Row4Y + 34;
        // face animation block = texture area x 2..7, y 5..7 (row 5, x 2..5 is unused)
        for (int gy = 0; gy < 3; gy++)
            for (int gx = 0; gx < 6; gx++)
            {
                int tx = 2 + gx, ty = 5 + gy;
                if (ty == 5 && tx < 6) continue;
                L.Cells.Add(new(L.AnimX + gx * Big, L.AnimY + gy * Big, Big, tx, ty));
            }
        L.PortraitX = 700;
        for (int r = 0; r < SkinGeometry.PortraitSize; r++)
            for (int c = 0; c < SkinGeometry.PortraitSize; c++)
                L.Cells.Add(new(L.PortraitX + c * PortraitCell, L.AnimY + r * PortraitCell, PortraitCell, SkinGeometry.PortraitX + c, SkinGeometry.PortraitY + r));
        L.PreviewX = 980;
        L.Height = 1850;
        return L;
    }

    // ------------------------------------------------------------------ colours
    static readonly uint Paper = RgbaImage.Pack(242, 240, 234, 255);
    static readonly uint Ink = RgbaImage.Pack(40, 40, 44, 255);
    static readonly uint Muted = RgbaImage.Pack(110, 110, 116, 255);
    static readonly uint Grid = RgbaImage.Pack(188, 188, 182, 255);
    static readonly uint Edge = RgbaImage.Pack(60, 60, 66, 255);
    static readonly uint HatEdge = RgbaImage.Pack(0, 140, 200, 255);
    static readonly uint AnimEdge = RgbaImage.Pack(200, 60, 60, 255);
    static readonly uint PortraitEdge = RgbaImage.Pack(200, 150, 0, 255);

    // marker in the top-left pixels: "MCD" "2SS", version, FNV hash of the skin key
    static readonly uint Magic1 = RgbaImage.Pack(0x4D, 0x43, 0x44, 255), Magic2 = RgbaImage.Pack(0x32, 0x53, 0x53, 255);
    const byte Version = 1;

    public static RgbaImage Create(RgbaImage skin, string heroName, string skinKey)
    {
        var L = Layout.Value;
        var g = new RgbaImage(Width, L.Height);
        g.FillRect(0, 0, Width, L.Height, Paper);
        uint h = Hash(skinKey);
        g.Set(0, 0, Magic1); g.Set(1, 0, Magic2); g.Set(2, 0, RgbaImage.Pack(Version, 0, 0, 255));
        g.Set(3, 0, RgbaImage.Pack((byte)h, (byte)(h >> 8), (byte)(h >> 16), 255));
        g.Set(4, 0, RgbaImage.Pack((byte)(h >> 24), 0, 0, 255));

        Text(g, heroName.ToUpperInvariant() + " - DESIGN SHEET", M, 28, 3, Ink);
        string[] intro =
        [
            "1  PAINT INSIDE THE SQUARES. EVERY SQUARE IS ONE PIXEL OF YOUR HERO.",
            "2  EACH BODY PART IS UNFOLDED LIKE A PAPER MODEL. THE BIG MIDDLE SQUARE IS THE FRONT.",
            "3  RIGHT AND LEFT ARE THE HERO'S OWN RIGHT AND LEFT, AS IF YOU WERE THE HERO.",
            "4  DON'T RESIZE, CROP OR MOVE ANYTHING. SAVE AS PNG AND UPLOAD THIS FILE IN THE APP.",
        ];
        for (int i = 0; i < intro.Length; i++) Text(g, intro[i], M, 82 + 26 * i, 2, Ink);

        // squares
        foreach (var c in L.Cells) DrawCell(g, c, skin.Get(c.Tx, c.Ty));

        // nets: outlines and labels
        foreach (var n in L.Nets)
        {
            var (w, hh, d) = Dims(n.Part);
            uint edge = n.Part == Part.Hat ? HatEdge : Edge;
            Text(g, n.Title, n.X, n.Y - 26, 2, n.Part == Part.Hat ? HatEdge : Ink);
            foreach (var f in Enum.GetValues<Face>())
            {
                var map = SkinGeometry.Get(n.Part, f);
                var (fx, fy) = FaceOrigin(n.Part, f);
                g.StrokeRect(n.X + fx * S, n.Y + fy * S, map.W * S, map.H * S, edge, 2);
            }
            // labels in the empty corners of the cross
            int cs = d * S;
            int sc = cs >= 96 ? 2 : 1;
            int lh = PixelFont.Height(sc);
            (string right, string left) = n.Part switch
            {
                Part.RightArm or Part.RightLeg => ("OUTSIDE", "INSIDE"),
                Part.LeftArm or Part.LeftLeg => ("INSIDE", "OUTSIDE"),
                _ => ("RIGHT", "LEFT"),
            };
            Text(g, "TOP →", n.X + 4, n.Y + 6, sc, Muted);
            Text(g, right + " ↓", n.X + 4, n.Y + cs - lh - 6, sc, Ink);
            Text(g, left + " ↓", n.X + (d + w) * S + 4, n.Y + cs - lh - 6, sc, Ink);
            Text(g, "BACK ↓", n.X + (2 * d + w) * S + 4, n.Y + cs - lh - 6, sc, Ink);
            Text(g, "BOTTOM →", n.X + 4, n.Y + (d + hh) * S + 6, sc, Muted);
        }

        // whole-hero previews beside the legs (not read back)
        Preview(g, SkinRender.Front(skin), "FRONT VIEW", M, 1106 - 26);
        Preview(g, SkinRender.Back(skin), "BACK VIEW", Width - M - 16 * 8, 1106 - 26);

        // face animation
        int ax = L.AnimX, ay = L.AnimY;
        Text(g, "FACE ANIMATION", ax, L.Row4Y, 2, AnimEdge);
        Text(g, "EYEBROWS ↓", ax + 4, ay + 9, 2, Ink);
        g.StrokeRect(ax, ay + Big, 4 * Big, 2 * Big, AnimEdge, 2);
        g.StrokeRect(ax + 4 * Big, ay, 2 * Big, 3 * Big, AnimEdge, 2);
        int lx = ax + 6 * Big + 14;
        (string Title, string Detail)[] rows =
        [
            ("← PUPILS", "LEFT SQUARE: EYE ON YOUR LEFT. RIGHT SQUARE: EYE ON YOUR RIGHT."),
            ("← EYE WHITES", "LEFT SQUARE: OUTER PIXEL. RIGHT SQUARE: INNER PIXEL (UNDER THE PUPIL)."),
            ("← MOUTH", "LEFT HALF AND RIGHT HALF OF THE MOUTH."),
        ];
        for (int i = 0; i < rows.Length; i++)
        {
            Text(g, rows[i].Title, lx, ay + i * Big + 3, 2, Ink);
            Text(g, rows[i].Detail, lx, ay + i * Big + 21, 1, Muted);
        }
        string[] animNotes =
        [
            "THE HEAD'S FRONT HAS NO EYES OR MOUTH: THE GAME DRAWS AND ANIMATES THEM FROM THESE SQUARES.",
            "EYEBROWS: A 4 x 2 SHAPE, DRAWN ABOVE BOTH EYES (MIRRORED FOR THE OTHER EYE). EMPTY SQUARES = NOTHING.",
            "EYE WHITES: 2 PIXELS PER EYE, MIRRORED FOR THE OTHER EYE. THE PUPIL COVERS THE INNER ONE.",
            "MOUTH: 2 PIXELS IN THE MIDDLE OF THE FACE, TWO ROWS BELOW THE EYES.",
        ];
        for (int i = 0; i < animNotes.Length; i++) Text(g, animNotes[i], ax, ay + 3 * Big + 18 + 14 * i, 1, Muted);

        // portrait
        Text(g, "PORTRAIT", L.PortraitX, L.Row4Y, 2, PortraitEdge);
        g.StrokeRect(L.PortraitX, ay, 8 * PortraitCell, 8 * PortraitCell, PortraitEdge, 2);
        Text(g, "THE SMALL FACE PICTURE IN THE LOCKER.", L.PortraitX, ay + 8 * PortraitCell + 10, 1, Muted);
        Text(g, "PAINT A WHOLE FACE HERE, WITH EYES AND MOUTH.", L.PortraitX, ay + 8 * PortraitCell + 24, 1, Muted);

        // preview (not read back)
        Text(g, "IN-GAME FACE", L.PreviewX, L.Row4Y, 2, Muted);
        var face = SkinGeometry.FaceAsSeen(skin);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                g.FillRect(L.PreviewX + x * PreviewCell, ay + y * PreviewCell, PreviewCell, PreviewCell, RgbaImage.Blend(Paper, face.Get(x, y)));
        g.StrokeRect(L.PreviewX - 2, ay - 2, 8 * PreviewCell + 4, 8 * PreviewCell + 4, Muted, 1);
        Text(g, "PREVIEW ONLY:", L.PreviewX, ay + 8 * PreviewCell + 10, 1, Muted);
        Text(g, "PAINTING HERE", L.PreviewX, ay + 8 * PreviewCell + 24, 1, Muted);
        Text(g, "CHANGES NOTHING.", L.PreviewX, ay + 8 * PreviewCell + 38, 1, Muted);

        // footer
        int footY = L.Height - 90;
        Text(g, "CHECKERED SQUARES ARE TRANSPARENT. THE HAT LAYER AND EYEBROWS MAY STAY EMPTY.", M, footY, 2, Ink);
        Text(g, "KEEP HEAD, BODY, ARMS AND LEGS SOLID: NO EMPTY SQUARES THERE.", M, footY + 24, 2, Ink);
        Text(g, "MADE WITH MCD2 SKIN STUDIO", M, footY + 54, 1, Muted);
        return g;
    }

    static void Preview(RgbaImage g, RgbaImage img, string title, int x, int y)
    {
        const int P = 8;
        Text(g, title, x, y, 2, Muted);
        for (int j = 0; j < img.Height; j++)
            for (int i = 0; i < img.Width; i++)
                g.FillRect(x + i * P, y + 26 + j * P, P, P, RgbaImage.Blend(Paper, img.Get(i, j)));
        Text(g, "PREVIEW ONLY", x, y + 26 + img.Height * P + 8, 1, Muted);
    }

    static void DrawCell(RgbaImage g, Cell c, uint colour)
    {
        g.FillRect(c.X, c.Y, c.Size, c.Size, colour);   // transparent texels stay transparent
        g.FillRect(c.X, c.Y, c.Size, 1, Grid);
        g.FillRect(c.X, c.Y, 1, c.Size, Grid);
        g.FillRect(c.X, c.Y + c.Size - 1, c.Size, 1, Grid);
        g.FillRect(c.X + c.Size - 1, c.Y, 1, c.Size, Grid);
    }

    static void Text(RgbaImage g, string s, int x, int y, int scale, uint colour) => PixelFont.Draw(g, s, x, y, scale, colour);

    static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char ch in s.ToLowerInvariant()) { h ^= ch; h *= 16777619; }
        return h;
    }

    public static bool IsSheet(RgbaImage img) => img.Width == Width && img.Height == Height;

    /// <summary>True/false when the sheet's marker says which skin it was made for; null when the marker was painted over.</summary>
    public static bool? MadeFor(RgbaImage sheet, string skinKey)
    {
        if (!IsSheet(sheet) || sheet.Get(0, 0) != Magic1 || sheet.Get(1, 0) != Magic2) return null;
        var (a, b, c, _) = RgbaImage.Unpack(sheet.Get(3, 0));
        var (d, _, _, _) = RgbaImage.Unpack(sheet.Get(4, 0));
        uint h = (uint)(a | b << 8 | c << 16 | d << 24);
        return h == Hash(skinKey);
    }

    /// <summary>The 64×64 skin a painted sheet describes. Texels not on the sheet keep <paramref name="original"/>'s pixels.</summary>
    public static RgbaImage Read(RgbaImage sheet, RgbaImage original)
    {
        if (!IsSheet(sheet)) throw new ArgumentException("Not a design sheet");
        var o = original.Clone();
        foreach (var c in Layout.Value.Cells) o.Set(c.Tx, c.Ty, Sample(sheet, c));
        return o;
    }

    /// <summary>Most common colour inside the square, ignoring its edges (grid lines, stray brush edges).</summary>
    static uint Sample(RgbaImage img, Cell c)
    {
        int m = c.Size >= 24 ? 5 : 3;
        var counts = new Dictionary<uint, int>();
        uint best = 0; int bestN = -1;
        for (int y = c.Y + m; y < c.Y + c.Size - m; y++)
            for (int x = c.X + m; x < c.X + c.Size - m; x++)
            {
                uint p = img.Get(x, y);
                if (p >> 24 == 0) p = 0;
                int n = counts.TryGetValue(p, out var k) ? k + 1 : 1;
                counts[p] = n;
                if (n > bestN) { bestN = n; best = p; }
            }
        return best;
    }
}
