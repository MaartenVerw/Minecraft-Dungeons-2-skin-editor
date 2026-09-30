using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>A user-facing problem with an uploaded image; the message says what to do.</summary>
public sealed class SkinImageException(string message) : Exception(message);

public enum UploadKind { Texture, Preview8x, DesignSheet }

public sealed record Upload(RgbaImage Skin, UploadKind Kind, bool? SheetMatchesSkin);

public static class SkinImport
{
    /// <summary>Turns an uploaded PNG into a 64×64 skin: a painted design sheet, the 64×64 texture,
    /// or a 512×512 exact 8× upscale. <paramref name="original"/> fills texels the sheet doesn't show.</summary>
    public static Upload Load(string path, RgbaImage original, string skinKey)
    {
        if (!File.Exists(path)) throw new SkinImageException("That file no longer exists. Pick it again.");
        RgbaImage img;
        try { img = Png.Decode(File.ReadAllBytes(path)); }
        catch (InvalidDataException)
        {
            throw new SkinImageException("This isn't a PNG image. In Paint, use File > Save as > PNG picture, then upload that file.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new SkinImageException("The file couldn't be opened. Close it in your paint program and try again.");
        }
        return Convert(img, original, skinKey);
    }

    public static Upload Convert(RgbaImage img, RgbaImage original, string skinKey)
    {
        if (DesignSheet.IsSheet(img))
            return new Upload(DesignSheet.Read(img, original), UploadKind.DesignSheet, DesignSheet.MadeFor(img, skinKey));
        if (img.Width == 64 && img.Height == 64) return new Upload(img, UploadKind.Texture, null);
        if (img.Width == 512 && img.Height == 512)
            return new Upload(img.DownscaleExact(8) ?? throw new SkinImageException(
                "This 512×512 image isn't made of clean 8×8 blocks, so it can't be shrunk back without blurring. " +
                "Paint on the design sheet instead."), UploadKind.Preview8x, null);
        if (Math.Abs(img.Width * DesignSheet.Height - img.Height * DesignSheet.Width) < img.Width * 4)
            throw new SkinImageException(
                $"This looks like a design sheet, but it was resized to {img.Width}×{img.Height}. It must stay {DesignSheet.Width}×{DesignSheet.Height}. " +
                "Download the design sheet again and paint without resizing.");
        throw new SkinImageException(
            $"The image is {img.Width}×{img.Height} pixels. Upload the design sheet ({DesignSheet.Width}×{DesignSheet.Height}) or the 64×64 texture. " +
            "Don't resize them: download them again and paint over them.");
    }

    /// <summary>
    /// Paint programs without transparency (older Windows Paint) fill transparent areas with white. If
    /// nearly all texels that were transparent in the original are now one opaque colour, returns that
    /// colour so the caller can offer to make them transparent again.
    /// </summary>
    public static uint? LostTransparency(RgbaImage original, RgbaImage edited)
    {
        var used = UsedTexels();
        var counts = new Dictionary<uint, int>();
        int total = 0;
        foreach (var (x, y) in used)
        {
            if (original.Alpha(x, y) != 0) continue;
            total++;
            uint c = edited.Get(x, y);
            if (c >> 24 == 255) counts[c] = counts.GetValueOrDefault(c) + 1;
        }
        if (total < 8 || counts.Count == 0) return null;
        var (colour, n) = counts.MaxBy(kv => kv.Value);
        return n >= total * 0.9 ? colour : null;
    }

    /// <summary>Makes every texel of exactly <paramref name="colour"/> that was transparent in the original transparent again.</summary>
    public static RgbaImage RestoreTransparency(RgbaImage original, RgbaImage edited, uint colour)
    {
        var o = edited.Clone();
        foreach (var (x, y) in UsedTexels())
            if (original.Alpha(x, y) == 0 && o.Get(x, y) == colour) o.Set(x, y, 0);
        return o;
    }

    /// <summary>Every texel the game reads: all faces, the face animation corner and the portrait.</summary>
    public static IEnumerable<(int X, int Y)> UsedTexels()
    {
        foreach (var f in SkinGeometry.Faces)
            for (int r = 0; r < f.H; r++)
                for (int c = 0; c < f.W; c++)
                    yield return f.Texel(c, r);
        foreach (var t in SkinGeometry.AnimationTexels()) yield return t;
        for (int y = 0; y < SkinGeometry.PortraitSize; y++)
            for (int x = 0; x < SkinGeometry.PortraitSize; x++)
                yield return (SkinGeometry.PortraitX + x, SkinGeometry.PortraitY + y);
    }

    /// <summary>Nothing the game reads changed: installing would just reproduce the stock skin.</summary>
    public static bool Unchanged(RgbaImage original, RgbaImage edited) =>
        UsedTexels().All(t => RgbaImage.SameColour(original.Get(t.X, t.Y), edited.Get(t.X, t.Y)));
}
