namespace Mcd2SkinStudio.Core.Skins;

/// <summary>The pages of the built-in editor, one per body part plus the face animation and portrait.</summary>
public enum EditorView { Head, Hat, Body, Arms, Legs, Face }

/// <summary>One paintable square, in grid units. Squares of the same <paramref name="Group"/> that touch
/// on the page are neighbours for the fill bucket.</summary>
public sealed record EditorCell(int Group, int X, int Y, int Size, int Tx, int Ty, string Name);

public enum OutlineKind { Face, Hat, Animation, Portrait }

public sealed record EditorOutline(int X, int Y, int W, int H, OutlineKind Kind);

/// <summary>Text drawn inside a box (grid units), against its top or bottom edge.</summary>
public sealed record EditorLabel(string Text, int X, int Y, int W, int H, bool Bottom, bool Title);

public sealed class EditorPage
{
    public EditorView View { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<EditorCell> Cells { get; }
    public IReadOnlyList<EditorOutline> Outlines { get; }
    public IReadOnlyList<EditorLabel> Labels { get; }
    readonly Dictionary<(int, int, int), EditorCell> _at = [];

    internal EditorPage(EditorView view, int width, int height, List<EditorCell> cells, List<EditorOutline> outlines, List<EditorLabel> labels)
    {
        (View, Width, Height, Cells, Outlines, Labels) = (view, width, height, cells, outlines, labels);
        foreach (var c in cells) _at[(c.Group, c.X, c.Y)] = c;
    }

    /// <summary>The square at grid position (x, y), or null.</summary>
    public EditorCell? CellAt(double x, double y) =>
        Cells.FirstOrDefault(c => x >= c.X && x < c.X + c.Size && y >= c.Y && y < c.Y + c.Size);

    /// <summary>Squares of the same group sharing an edge with <paramref name="c"/>.</summary>
    public IEnumerable<EditorCell> Neighbours(EditorCell c)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (_at.TryGetValue((c.Group, c.X + dx * c.Size, c.Y + dy * c.Size), out var n) && n.Size == c.Size)
                yield return n;
    }
}

/// <summary>
/// Lays out the editor pages. Body parts are unfolded like paper models (see
/// <see cref="SkinGeometry.NetOrigin"/>), so neighbouring squares are neighbours on the hero too.
/// Every texel the game reads is on exactly one page, once.
/// </summary>
public static class EditorLayout
{
    const int TitleH = 2, NetGap = 4;

    static readonly Lazy<Dictionary<EditorView, EditorPage>> Pages = new(() =>
        Enum.GetValues<EditorView>().ToDictionary(v => v, Build));

    public static EditorPage Page(EditorView v) => Pages.Value[v];

    public static IEnumerable<EditorCell> AllCells => Pages.Value.Values.SelectMany(p => p.Cells);

    public static string PartName(Part p) => p switch
    {
        Part.Head => "Head",
        Part.Hat => "Hat layer",
        Part.Body => "Body",
        Part.RightArm => "Right arm",
        Part.LeftArm => "Left arm",
        Part.RightLeg => "Right leg",
        Part.LeftLeg => "Left leg",
        _ => p.ToString(),
    };

    /// <summary>Side names as the user thinks of them: arms and legs have an outside and an inside.</summary>
    static (string Right, string Left) SideNames(Part p) => p switch
    {
        Part.RightArm or Part.RightLeg => ("Outside", "Inside"),
        Part.LeftArm or Part.LeftLeg => ("Inside", "Outside"),
        _ => ("Right side", "Left side"),
    };

    static string FaceName(Part p, Face f)
    {
        var (right, left) = SideNames(p);
        return f switch { Face.Right => right, Face.Left => left, _ => f.ToString() };
    }

    static EditorPage Build(EditorView v)
    {
        var cells = new List<EditorCell>();
        var outlines = new List<EditorOutline>();
        var labels = new List<EditorLabel>();
        int w, h;
        switch (v)
        {
            case EditorView.Head:
            case EditorView.Hat:
            case EditorView.Body:
            {
                var p = v switch { EditorView.Head => Part.Head, EditorView.Hat => Part.Hat, _ => Part.Body };
                AddNet(p, 0, 0, 0, null, cells, outlines, labels);
                (w, h) = SkinGeometry.NetSize(p);
                break;
            }
            case EditorView.Arms:
            case EditorView.Legs:
            {
                var (right, left) = v == EditorView.Arms ? (Part.RightArm, Part.LeftArm) : (Part.RightLeg, Part.LeftLeg);
                var (nw, nh) = SkinGeometry.NetSize(right);
                // the hero's right on the left of the screen, as when facing the hero
                AddNet(right, 0, TitleH, 0, PartName(right), cells, outlines, labels);
                AddNet(left, nw + NetGap, TitleH, 1, PartName(left), cells, outlines, labels);
                (w, h) = (2 * nw + NetGap, TitleH + nh);
                break;
            }
            default:
                (w, h) = BuildFace(cells, outlines, labels);
                break;
        }
        return new EditorPage(v, w, h, cells, outlines, labels);
    }

    static void AddNet(Part p, int ox, int oy, int group, string? title, List<EditorCell> cells, List<EditorOutline> outlines, List<EditorLabel> labels)
    {
        var (w, h, d) = SkinGeometry.Dims(p);
        var (nw, _) = SkinGeometry.NetSize(p);
        if (title != null) labels.Add(new(title.ToUpperInvariant(), ox, oy - TitleH, nw, TitleH, false, true));
        foreach (var f in Enum.GetValues<Face>())
        {
            var map = SkinGeometry.Get(p, f);
            var (fx, fy) = SkinGeometry.NetOrigin(p, f);
            string name = $"{PartName(p)} · {FaceName(p, f)}";
            for (int r = 0; r < map.H; r++)
                for (int c = 0; c < map.W; c++)
                {
                    var (tx, ty) = map.Texel(c, r);
                    cells.Add(new(group, ox + fx + c, oy + fy + r, 1, tx, ty, name));
                }
            outlines.Add(new(ox + fx, oy + fy, map.W, map.H, p == Part.Hat ? OutlineKind.Hat : OutlineKind.Face));
        }
        // labels in the empty corners of the cross
        var (right, left) = SideNames(p);
        labels.Add(new("TOP →", ox, oy, d, d, false, false));
        labels.Add(new(right.ToUpperInvariant() + " ↓", ox, oy, d, d, true, false));
        labels.Add(new(left.ToUpperInvariant() + " ↓", ox + d + w, oy, d, d, true, false));
        labels.Add(new("BACK ↓", ox + 2 * d + w, oy, w, d, true, false));
        labels.Add(new("BOTTOM →", ox, oy + d + h, d, d, false, false));
    }

    /// <summary>Face animation (big squares) with the portrait below it.</summary>
    static (int W, int H) BuildFace(List<EditorCell> cells, List<EditorOutline> outlines, List<EditorLabel> labels)
    {
        const int A = 4;       // face animation square size
        const int P = 2;       // portrait square size
        // the animation corner of the texture: x 2..7, y 5..7 (x 2..5 of row 5 is unused)
        string[,] names =
        {
            { "", "", "", "", "Pupil · the eye on your left", "Pupil · the eye on your right" },
            { "", "", "", "", "Eye white · outer pixel", "Eye white · inner pixel (under the pupil)" },
            { "", "", "", "", "Mouth · left half", "Mouth · right half" },
        };
        for (int gy = 0; gy < 3; gy++)
            for (int gx = 0; gx < 6; gx++)
            {
                if (gy == 0 && gx < 4) continue;
                bool brow = gx < 4;
                string name = brow ? "Eyebrow shape · drawn above the eye on your left, mirrored for the other eye" : names[gy, gx];
                // separate fill groups: eyebrows, pupils, eye whites, mouth
                cells.Add(new(brow ? 0 : 1 + gy, gx * A, gy * A, A, 2 + gx, 5 + gy, name));
            }
        outlines.Add(new(0, A, 4 * A, 2 * A, OutlineKind.Animation));
        outlines.Add(new(4 * A, 0, 2 * A, 3 * A, OutlineKind.Animation));
        labels.Add(new("EYEBROWS ↓", 0, 0, 4 * A, A, true, true));
        int lx = 6 * A + 1;
        labels.Add(new("← PUPILS", lx, 0, 18, A, false, true));
        labels.Add(new("← EYE WHITES", lx, A, 18, A, false, true));
        labels.Add(new("← MOUTH", lx, 2 * A, 18, A, false, true));

        int py = 3 * A + 2 + TitleH;
        labels.Add(new("PORTRAIT: THE FACE PICTURE IN THE LOCKER", 0, py - TitleH, 6 * A + 18, TitleH, false, true));
        for (int r = 0; r < SkinGeometry.PortraitSize; r++)
            for (int c = 0; c < SkinGeometry.PortraitSize; c++)
                cells.Add(new(4, c * P, py + r * P, P, SkinGeometry.PortraitX + c, SkinGeometry.PortraitY + r, "Portrait"));
        outlines.Add(new(0, py, SkinGeometry.PortraitSize * P, SkinGeometry.PortraitSize * P, OutlineKind.Portrait));
        return (lx + 18, py + SkinGeometry.PortraitSize * P);
    }
}
