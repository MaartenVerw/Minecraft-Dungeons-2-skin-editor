using System.Buffers.Binary;
using Mcd2SkinStudio.Core.Imaging;

namespace Mcd2SkinStudio.Core.Skins;

/// <summary>Finds and swaps the 64×64 BGRA8 mip inside a hero texture package.</summary>
public static class TextureIO
{
    public const int Size = 64;
    public const int PixelBytes = Size * Size * 4;

    /// <summary>Offset of the mip's pixel data: the bytes that end right before the last (w, h, 1) int32 triple.</summary>
    public static int FindMip(ReadOnlySpan<byte> pkg, int w, int h, int nbytes)
    {
        Span<byte> tail = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(tail, w);
        BinaryPrimitives.WriteInt32LittleEndian(tail[4..], h);
        BinaryPrimitives.WriteInt32LittleEndian(tail[8..], 1);
        int o = pkg.LastIndexOf(tail);
        if (o < nbytes) throw new InvalidDataException($"Could not locate the {w}x{h} texture data in this package.");
        return o - nbytes;
    }

    public static RgbaImage ExtractSkin(ReadOnlySpan<byte> pkg)
    {
        int o = FindMip(pkg, Size, Size, PixelBytes);
        return RgbaImage.FromBgra(pkg.Slice(o, PixelBytes), Size, Size);
    }

    public static byte[] PatchSkin(byte[] pkg, RgbaImage skin)
    {
        if (skin.Width != Size || skin.Height != Size) throw new ArgumentException("Skin must be 64x64");
        int o = FindMip(pkg, Size, Size, PixelBytes);
        var outPkg = (byte[])pkg.Clone();
        skin.ToBgra().CopyTo(outPkg, o);
        return outPkg;
    }
}
