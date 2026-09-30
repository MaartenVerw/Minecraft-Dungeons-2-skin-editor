namespace Mcd2SkinStudio.Core.Imaging;

/// <summary>A small RGBA8 image. Pixels are packed as 0xAABBGGRR (R in the low byte).</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    readonly uint[] _px;

    public RgbaImage(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; _px = new uint[width * height];
    }

    public static uint Pack(byte r, byte g, byte b, byte a) => (uint)(r | g << 8 | b << 16 | a << 24);
    public static (byte R, byte G, byte B, byte A) Unpack(uint c) => ((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));

    public uint Get(int x, int y) => _px[y * Width + x];
    public void Set(int x, int y, uint c) => _px[y * Width + x] = c;
    public byte Alpha(int x, int y) => (byte)(_px[y * Width + x] >> 24);

    public RgbaImage Clone()
    {
        var c = new RgbaImage(Width, Height);
        _px.CopyTo(c._px, 0);
        return c;
    }

    public bool SameAs(RgbaImage o) => o.Width == Width && o.Height == Height && _px.AsSpan().SequenceEqual(o._px);

    /// <summary>From UE's PF_B8G8R8A8 bytes.</summary>
    public static RgbaImage FromBgra(ReadOnlySpan<byte> bgra, int w, int h)
    {
        var img = new RgbaImage(w, h);
        for (int i = 0; i < w * h; i++) img._px[i] = Pack(bgra[4 * i + 2], bgra[4 * i + 1], bgra[4 * i], bgra[4 * i + 3]);
        return img;
    }

    public byte[] ToBgra()
    {
        var o = new byte[_px.Length * 4];
        for (int i = 0; i < _px.Length; i++)
        {
            var (r, g, b, a) = Unpack(_px[i]);
            o[4 * i] = b; o[4 * i + 1] = g; o[4 * i + 2] = r; o[4 * i + 3] = a;
        }
        return o;
    }

    public byte[] ToRgbaBytes()
    {
        var o = new byte[_px.Length * 4];
        for (int i = 0; i < _px.Length; i++)
        {
            var (r, g, b, a) = Unpack(_px[i]);
            o[4 * i] = r; o[4 * i + 1] = g; o[4 * i + 2] = b; o[4 * i + 3] = a;
        }
        return o;
    }

    public RgbaImage Crop(int x, int y, int w, int h)
    {
        var c = new RgbaImage(w, h);
        for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) c.Set(i, j, Get(x + i, y + j));
        return c;
    }

    public void Paste(RgbaImage src, int x, int y)
    {
        for (int j = 0; j < src.Height; j++) for (int i = 0; i < src.Width; i++) Set(x + i, y + j, src.Get(i, j));
    }

    /// <summary>Source-over blend of <paramref name="src"/> onto this image at (x, y).</summary>
    public void Composite(RgbaImage src, int x, int y)
    {
        for (int j = 0; j < src.Height; j++)
            for (int i = 0; i < src.Width; i++)
                Set(x + i, y + j, Blend(Get(x + i, y + j), src.Get(i, j)));
    }

    public static uint Blend(uint dst, uint src)
    {
        var (sr, sg, sb, sa) = Unpack(src);
        if (sa == 255) return src;
        if (sa == 0) return dst;
        var (dr, dg, db, da) = Unpack(dst);
        float a = sa / 255f, ida = da / 255f * (1 - a), oa = a + ida;
        byte Mix(byte s, byte d) => (byte)Math.Round((s * a + d * ida) / oa);
        return Pack(Mix(sr, dr), Mix(sg, dg), Mix(sb, db), (byte)Math.Round(oa * 255));
    }

    public RgbaImage ScaleNearest(int factor)
    {
        var o = new RgbaImage(Width * factor, Height * factor);
        for (int y = 0; y < o.Height; y++) for (int x = 0; x < o.Width; x++) o.Set(x, y, Get(x / factor, y / factor));
        return o;
    }

    /// <summary>If this image is an exact nearest-neighbour upscale by <paramref name="factor"/>, the original; else null.</summary>
    public RgbaImage? DownscaleExact(int factor)
    {
        if (Width % factor != 0 || Height % factor != 0) return null;
        var o = new RgbaImage(Width / factor, Height / factor);
        for (int y = 0; y < o.Height; y++)
            for (int x = 0; x < o.Width; x++)
            {
                uint c = Get(x * factor, y * factor);
                for (int j = 0; j < factor; j++)
                    for (int i = 0; i < factor; i++)
                        if (!SameColour(Get(x * factor + i, y * factor + j), c)) return null;
                o.Set(x, y, c);
            }
        return o;
    }

    /// <summary>Equal, treating all fully transparent pixels as the same.</summary>
    public static bool SameColour(uint a, uint b) => a == b || (a >> 24 == 0 && b >> 24 == 0);

    public void FillRect(int x, int y, int w, int h, uint c)
    {
        for (int j = Math.Max(0, y); j < Math.Min(Height, y + h); j++)
            for (int i = Math.Max(0, x); i < Math.Min(Width, x + w); i++)
                Set(i, j, c);
    }

    public void BlendRect(int x, int y, int w, int h, uint c)
    {
        for (int j = Math.Max(0, y); j < Math.Min(Height, y + h); j++)
            for (int i = Math.Max(0, x); i < Math.Min(Width, x + w); i++)
                Set(i, j, Blend(Get(i, j), c));
    }

    public void StrokeRect(int x, int y, int w, int h, uint c, int t = 1)
    {
        FillRect(x, y, w, t, c); FillRect(x, y + h - t, w, t, c);
        FillRect(x, y, t, h, c); FillRect(x + w - t, y, t, h, c);
    }
}
