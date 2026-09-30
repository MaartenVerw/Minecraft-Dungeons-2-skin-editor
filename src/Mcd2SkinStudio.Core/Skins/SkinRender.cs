using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>Pictures made from a 64×64 skin.</summary>
public static class SkinRender
{
    /// <summary>16×32 flat front view as it looks in the game: face with its animated eyes, brows and
    /// mouth, the hat, body, slim arms and legs (the hero's right side is on the viewer's left).</summary>
    public static RgbaImage Front(RgbaImage s)
    {
        var o = new RgbaImage(16, 32);
        o.Composite(SkinGeometry.FaceAsSeen(s), 4, 0);
        void Put(Part p, int dx, int dy) => o.Composite(SkinGeometry.FaceImage(s, SkinGeometry.Get(p, Face.Front)), dx, dy);
        Put(Part.Body, 4, 8);
        Put(Part.RightArm, 1, 8);
        Put(Part.LeftArm, 12, 8);
        Put(Part.RightLeg, 4, 20);
        Put(Part.LeftLeg, 8, 20);
        return o;
    }

    /// <summary>16×32 flat back view (the hero's left side is on the viewer's left).</summary>
    public static RgbaImage Back(RgbaImage s)
    {
        var o = new RgbaImage(16, 32);
        void Put(Part p, int dx, int dy) => o.Composite(SkinGeometry.FaceImage(s, SkinGeometry.Get(p, Face.Back)), dx, dy);
        Put(Part.Head, 4, 0);
        Put(Part.Hat, 4, 0);
        Put(Part.Body, 4, 8);
        Put(Part.LeftArm, 1, 8);
        Put(Part.RightArm, 12, 8);
        Put(Part.LeftLeg, 4, 20);
        Put(Part.RightLeg, 8, 20);
        return o;
    }
}
