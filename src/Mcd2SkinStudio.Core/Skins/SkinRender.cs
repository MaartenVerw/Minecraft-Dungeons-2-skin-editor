using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>Pictures made from a 64×64 skin.</summary>
public static class SkinRender
{
    public const int Width = 16, Height = 32;

    // where each part's front/back face sits in the 16×32 views (the head front is the in-game face)
    static readonly (Part Part, int X, int Y)[] FrontParts =
        [(Part.Body, 4, 8), (Part.RightArm, 1, 8), (Part.LeftArm, 12, 8), (Part.RightLeg, 4, 20), (Part.LeftLeg, 8, 20)];
    static readonly (Part Part, int X, int Y)[] BackParts =
        [(Part.Head, 4, 0), (Part.Hat, 4, 0), (Part.Body, 4, 8), (Part.LeftArm, 1, 8), (Part.RightArm, 12, 8), (Part.LeftLeg, 4, 20), (Part.RightLeg, 8, 20)];
    const int FaceX = 4, FaceY = 0;

    /// <summary>16×32 flat front view as it looks in the game: face with its animated eyes, brows and
    /// mouth, the hat, body, slim arms and legs (the hero's right side is on the viewer's left).</summary>
    public static RgbaImage Front(RgbaImage s)
    {
        var o = new RgbaImage(Width, Height);
        o.Composite(SkinGeometry.FaceAsSeen(s), FaceX, FaceY);
        foreach (var (p, x, y) in FrontParts) o.Composite(SkinGeometry.FaceImage(s, SkinGeometry.Get(p, Face.Front)), x, y);
        return o;
    }

    /// <summary>16×32 flat back view (the hero's left side is on the viewer's left).</summary>
    public static RgbaImage Back(RgbaImage s)
    {
        var o = new RgbaImage(Width, Height);
        foreach (var (p, x, y) in BackParts) o.Composite(SkinGeometry.FaceImage(s, SkinGeometry.Get(p, Face.Back)), x, y);
        return o;
    }

    /// <summary>Every spot where texel (tx, ty) shows up in <see cref="Front"/> (front = true) or <see cref="Back"/>.</summary>
    public static IEnumerable<(bool Front, int X, int Y)> Where(int tx, int ty)
    {
        foreach (var (p, x, y) in Placed(front: true))
            if (Locate(SkinGeometry.Get(p, Face.Front), tx, ty) is var (c, r)) yield return (true, x + c, y + r);
        foreach (var (col, row, t) in SkinGeometry.FaceOverlay)
            if (t == (tx, ty)) yield return (true, FaceX + col, FaceY + row);
        foreach (var (p, x, y) in Placed(front: false))
            if (Locate(SkinGeometry.Get(p, Face.Back), tx, ty) is var (c, r)) yield return (false, x + c, y + r);
    }

    /// <summary>The part shown at pixel (x, y) of the front or back view, or null for empty space.</summary>
    public static Part? PartAt(bool front, int x, int y)
    {
        foreach (var (p, px, py) in Placed(front).Reverse())
        {
            var f = SkinGeometry.Get(p, front ? Face.Front : Face.Back);
            if (x >= px && x < px + f.W && y >= py && y < py + f.H) return p == Part.Hat ? Part.Head : p;
        }
        return null;
    }

    static IEnumerable<(Part Part, int X, int Y)> Placed(bool front) =>
        front ? [(Part.Head, FaceX, FaceY), (Part.Hat, FaceX, FaceY), .. FrontParts] : BackParts;

    static (int Col, int Row)? Locate(FaceMap f, int tx, int ty)
    {
        if (tx < f.U || tx >= f.U + f.W || ty < f.V || ty >= f.V + f.H) return null;
        int c = tx - f.U, r = ty - f.V;
        return (f.FlipX ? f.W - 1 - c : c, f.FlipY ? f.H - 1 - r : r);
    }
}
