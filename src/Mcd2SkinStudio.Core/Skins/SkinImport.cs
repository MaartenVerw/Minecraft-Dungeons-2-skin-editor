using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>A user-facing problem with an uploaded image; the message says what to do.</summary>
public sealed class SkinImageException(string message) : Exception(message);

public static class SkinImport
{
    /// <summary>Loads an edited skin: a 64×64 PNG, or a 512×512 exact 8× upscale (the preview size).</summary>
    public static RgbaImage Load(string path)
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
        return Normalise(img);
    }

    public static RgbaImage Normalise(RgbaImage img)
    {
        if (img.Width == 64 && img.Height == 64) return img;
        if (img.Width == 512 && img.Height == 512)
            return img.DownscaleExact(8) ?? throw new SkinImageException(
                "This 512×512 image isn't made of clean 8×8 blocks, so it can't be shrunk back without blurring. " +
                "Edit the 64×64 file instead, or paint with a pencil tool and no smoothing.");
        throw new SkinImageException(
            $"The image is {img.Width}×{img.Height} pixels, but it must be 64×64 (the downloaded texture) or 512×512 (the 8× preview). " +
            "Don't resize the image: download the texture again and paint over it.");
    }

    /// <summary>True when the edit changed the face-animation corner the game needs for eyes, brows and mouth.</summary>
    public static bool PaletteChanged(RgbaImage original, RgbaImage edited)
    {
        var p = SkinLayout.Palette;
        for (int y = p.Y; y < p.Y + p.H; y++)
            for (int x = p.X; x < p.X + p.W; x++)
                if (!RgbaImage.SameColour(original.Get(x, y), edited.Get(x, y))) return true;
        return false;
    }

    /// <summary>Copies the game's original face-animation corner back into the edited skin.</summary>
    public static RgbaImage RestorePalette(RgbaImage original, RgbaImage edited)
    {
        var o = edited.Clone();
        var p = SkinLayout.Palette;
        o.Paste(original.Crop(p.X, p.Y, p.W, p.H), p.X, p.Y);
        return o;
    }

    /// <summary>Nothing changed at all: installing would just reproduce the stock skin.</summary>
    public static bool Unchanged(RgbaImage original, RgbaImage edited)
    {
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                if (!RgbaImage.SameColour(original.Get(x, y), edited.Get(x, y))) return false;
        return true;
    }
}
