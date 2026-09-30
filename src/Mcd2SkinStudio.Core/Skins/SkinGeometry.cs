using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>Body parts, named from the hero's own point of view.</summary>
public enum Part { Head, Hat, Body, RightArm, LeftArm, RightLeg, LeftLeg }

/// <summary>Faces of a part's box. Right/Left are the hero's own right and left.</summary>
public enum Face { Front, Back, Right, Left, Top, Bottom }

/// <summary>
/// One face of a part: its rectangle on the 64×64 texture and how it is oriented when seen from
/// outside (sides upright; Top seen from above with the front edge down; Bottom seen from below
/// with the front edge up).
/// </summary>
public sealed record FaceMap(Part Part, Face Face, int U, int V, int W, int H, bool FlipX, bool FlipY)
{
    /// <summary>Texel shown at (col, row) of the face as seen from outside.</summary>
    public (int X, int Y) Texel(int col, int row) => (U + (FlipX ? W - 1 - col : col), V + (FlipY ? H - 1 - row : row));
}

/// <summary>
/// Where every texel sits on the hero. Traced from the game's player mesh (SK_Player_Master, game
/// 1.1.1.0) by tools/uv_layout.py: each texel's 3D position and normal decide its part, face and
/// orientation. The legs are mirrored and swapped compared to Minecraft; the head/hat backs are mirrored.
/// </summary>
public static class SkinGeometry
{
    public static IReadOnlyList<FaceMap> Faces { get; } =
    [
        new(Part.Head, Face.Front, 8, 8, 8, 8, false, false),
        new(Part.Head, Face.Back, 24, 8, 8, 8, true, false),
        new(Part.Head, Face.Right, 0, 8, 8, 8, false, false),
        new(Part.Head, Face.Left, 16, 8, 8, 8, false, false),
        new(Part.Head, Face.Top, 8, 0, 8, 8, false, false),
        new(Part.Head, Face.Bottom, 16, 0, 8, 8, false, false),

        new(Part.Hat, Face.Front, 40, 8, 8, 8, false, false),
        new(Part.Hat, Face.Back, 56, 8, 8, 8, true, false),
        new(Part.Hat, Face.Right, 32, 8, 8, 8, false, false),
        new(Part.Hat, Face.Left, 48, 8, 8, 8, false, false),
        new(Part.Hat, Face.Top, 40, 0, 8, 8, false, false),
        new(Part.Hat, Face.Bottom, 48, 0, 8, 8, false, false),

        new(Part.Body, Face.Front, 20, 20, 8, 12, false, false),
        new(Part.Body, Face.Back, 32, 20, 8, 12, false, false),
        new(Part.Body, Face.Right, 16, 20, 4, 12, false, false),
        new(Part.Body, Face.Left, 28, 20, 4, 12, false, false),
        new(Part.Body, Face.Top, 20, 16, 8, 4, false, false),
        new(Part.Body, Face.Bottom, 28, 16, 8, 4, false, false),

        new(Part.RightArm, Face.Front, 44, 20, 3, 12, false, false),
        new(Part.RightArm, Face.Back, 51, 20, 3, 12, false, false),
        new(Part.RightArm, Face.Right, 40, 20, 4, 12, false, false),
        new(Part.RightArm, Face.Left, 47, 20, 4, 12, false, false),
        new(Part.RightArm, Face.Top, 44, 16, 3, 4, false, false),
        new(Part.RightArm, Face.Bottom, 47, 16, 3, 4, false, false),

        new(Part.LeftArm, Face.Front, 36, 52, 3, 12, false, false),
        new(Part.LeftArm, Face.Back, 43, 52, 3, 12, false, false),
        new(Part.LeftArm, Face.Right, 32, 52, 4, 12, false, false),
        new(Part.LeftArm, Face.Left, 39, 52, 4, 12, false, false),
        new(Part.LeftArm, Face.Top, 36, 48, 3, 4, false, false),
        new(Part.LeftArm, Face.Bottom, 39, 48, 3, 4, false, false),

        new(Part.RightLeg, Face.Front, 20, 52, 4, 12, true, false),
        new(Part.RightLeg, Face.Back, 28, 52, 4, 12, true, false),
        new(Part.RightLeg, Face.Right, 24, 52, 4, 12, true, false),
        new(Part.RightLeg, Face.Left, 16, 52, 4, 12, true, false),
        new(Part.RightLeg, Face.Top, 24, 48, 4, 4, false, true),
        new(Part.RightLeg, Face.Bottom, 20, 48, 4, 4, true, false),

        new(Part.LeftLeg, Face.Front, 4, 20, 4, 12, true, false),
        new(Part.LeftLeg, Face.Back, 12, 20, 4, 12, true, false),
        new(Part.LeftLeg, Face.Right, 8, 20, 4, 12, true, false),
        new(Part.LeftLeg, Face.Left, 0, 20, 4, 12, true, false),
        new(Part.LeftLeg, Face.Top, 8, 16, 4, 4, true, false),
        new(Part.LeftLeg, Face.Bottom, 4, 16, 4, 4, true, false),
    ];

    public static FaceMap Get(Part p, Face f) => Faces.First(x => x.Part == p && x.Face == f);

    /// <summary>Front width, height and depth of a part, in texels.</summary>
    public static (int W, int H, int D) Dims(Part p)
    {
        var front = Get(p, Face.Front);
        return (front.W, front.H, Get(p, Face.Right).W);
    }

    /// <summary>The part unfolded like a paper model (in squares): TOP above FRONT; RIGHT, FRONT, LEFT,
    /// BACK in a row; BOTTOM below FRONT. Strokes across an edge continue around the part.</summary>
    public static (int X, int Y) NetOrigin(Part p, Face f)
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

    public static (int W, int H) NetSize(Part p)
    {
        var (w, h, d) = Dims(p);
        return (2 * d + 2 * w, 2 * d + h);
    }

    // ---- face animation: the top-left corner of the texture (traced from the mesh's eye/brow/mouth quads)
    /// <summary>Pupil of the eye on the viewer's left (the hero's right eye).</summary>
    public static readonly (int X, int Y) PupilA = (6, 5);
    /// <summary>Pupil of the eye on the viewer's right (the hero's left eye).</summary>
    public static readonly (int X, int Y) PupilB = (7, 5);
    /// <summary>Eye white, outer pixel of each eye (mirrored for the other eye).</summary>
    public static readonly (int X, int Y) WhiteOuter = (6, 6);
    /// <summary>Eye white, inner pixel of each eye; the pupil is drawn over it.</summary>
    public static readonly (int X, int Y) WhiteInner = (7, 6);
    /// <summary>Mouth, left and right pixel as seen from the front.</summary>
    public static readonly (int X, int Y) MouthA = (6, 7), MouthB = (7, 7);
    /// <summary>Eyebrow shape: 4×2 texels at (2..5, 6..7), drawn above both eyes, mirrored on the viewer's right.</summary>
    public const int BrowX = 2, BrowY = 6, BrowW = 4, BrowH = 2;

    /// <summary>The portrait picture used in the Locker/HUD.</summary>
    public const int PortraitX = 56, PortraitY = 20, PortraitSize = 8;

    /// <summary>Every face-animation texel the game reads.</summary>
    public static IEnumerable<(int X, int Y)> AnimationTexels()
    {
        yield return PupilA; yield return PupilB; yield return WhiteOuter; yield return WhiteInner; yield return MouthA; yield return MouthB;
        for (int y = 0; y < BrowH; y++) for (int x = 0; x < BrowW; x++) yield return (BrowX + x, BrowY + y);
    }

    /// <summary>Where the game draws each face-animation texel on the 8×8 face, in drawing order.</summary>
    public static IReadOnlyList<(int Col, int Row, (int X, int Y) Texel)> FaceOverlay { get; } = BuildFaceOverlay();

    static List<(int, int, (int, int))> BuildFaceOverlay()
    {
        // eyes on row 4: whites 2 px per eye, pupils on the inner pixel; mouth on row 6
        List<(int, int, (int, int))> o =
        [
            (1, 4, WhiteOuter), (2, 4, WhiteInner), (6, 4, WhiteOuter), (5, 4, WhiteInner),
            (2, 4, PupilA), (5, 4, PupilB),
            (3, 6, MouthA), (4, 6, MouthB),
        ];
        // eyebrows on rows 2..3: viewer's left as painted, viewer's right mirrored
        for (int y = 0; y < BrowH; y++)
            for (int x = 0; x < BrowW; x++)
            {
                o.Add((x, 2 + y, (BrowX + x, BrowY + y)));
                o.Add((7 - x, 2 + y, (BrowX + x, BrowY + y)));
            }
        return o;
    }

    /// <summary>The 8×8 front of the head as it looks in the game: face texture, then eye whites,
    /// pupils, mouth and eyebrows from the animation corner, then the hat on top.</summary>
    public static RgbaImage FaceAsSeen(RgbaImage skin, bool withHat = true)
    {
        var o = FaceImage(skin, Get(Part.Head, Face.Front));
        foreach (var (col, row, t) in FaceOverlay)
            o.Set(col, row, RgbaImage.Blend(o.Get(col, row), skin.Get(t.X, t.Y)));
        if (withHat) o.Composite(FaceImage(skin, Get(Part.Hat, Face.Front)), 0, 0);
        return o;
    }

    /// <summary>A face as seen from outside.</summary>
    public static RgbaImage FaceImage(RgbaImage skin, FaceMap f)
    {
        var o = new RgbaImage(f.W, f.H);
        for (int r = 0; r < f.H; r++)
            for (int c = 0; c < f.W; c++)
            {
                var (x, y) = f.Texel(c, r);
                o.Set(c, r, skin.Get(x, y));
            }
        return o;
    }
}
