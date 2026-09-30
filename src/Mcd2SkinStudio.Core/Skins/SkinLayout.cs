namespace Mcd2SkinStudio.Core.Skins;

public enum RegionKind { Base, Overlay, Unused, Locked, Portrait }

/// <summary>A labelled rectangle on the 64×64 skin texture.</summary>
public sealed record Region(string Label, int X, int Y, int W, int H, RegionKind Kind, string Part);

/// <summary>
/// The hero texture layout: Minecraft's 64×64 slim-arm layout, plus the game's two extras. The
/// top-left 8×8 is the face-animation palette (eyes, brows, mouth), and the 8×8 at (56,20) is the
/// portrait picture. Phase 0: every stock skin leaves the jacket/sleeve/pants layers empty.
/// </summary>
public static class SkinLayout
{
    public static readonly Region Palette = new("FACE ANIM", 0, 0, 8, 8, RegionKind.Locked, "palette");
    public static readonly Region Portrait = new("PORTRAIT", 56, 20, 8, 8, RegionKind.Portrait, "portrait");

    public static IReadOnlyList<Region> All { get; } = Build();

    static List<Region> Build()
    {
        var r = new List<Region> { Palette, Portrait };
        void Box(string part, int u, int v, int w, int h, int d, RegionKind kind)
        {
            r.Add(new("TOP", u + d, v, w, d, kind, part));
            r.Add(new("BOTTOM", u + d + w, v, w, d, kind, part));
            r.Add(new("RIGHT", u, v + d, d, h, kind, part));
            r.Add(new("FRONT", u + d, v + d, w, h, kind, part));
            r.Add(new("LEFT", u + d + w, v + d, d, h, kind, part));
            r.Add(new("BACK", u + 2 * d + w, v + d, w, h, kind, part));
        }
        Box("HEAD", 0, 0, 8, 8, 8, RegionKind.Base);
        Box("HAT", 32, 0, 8, 8, 8, RegionKind.Overlay);
        Box("BODY", 16, 16, 8, 12, 4, RegionKind.Base);
        Box("ARM", 40, 16, 3, 12, 4, RegionKind.Base);
        Box("ARM", 32, 48, 3, 12, 4, RegionKind.Base);
        Box("LEG", 0, 16, 4, 12, 4, RegionKind.Base);
        Box("LEG", 16, 48, 4, 12, 4, RegionKind.Base);
        Box("JACKET", 16, 32, 8, 12, 4, RegionKind.Unused);
        Box("SLEEVE", 40, 32, 3, 12, 4, RegionKind.Unused);
        Box("SLEEVE", 48, 48, 3, 12, 4, RegionKind.Unused);
        Box("PANTS", 0, 32, 4, 12, 4, RegionKind.Unused);
        Box("PANTS", 0, 48, 4, 12, 4, RegionKind.Unused);
        return r;
    }
}
