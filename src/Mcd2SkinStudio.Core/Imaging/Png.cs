using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Mcd2SkinStudio.Core.Imaging;

/// <summary>Minimal PNG codec: decodes every standard PNG (all colour types, bit depths, Adam7)
/// to RGBA8, and encodes RGBA8. No dependencies beyond the BCL.</summary>
public static class Png
{
    static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static RgbaImage Load(string path) => Decode(File.ReadAllBytes(path));

    public static void Save(RgbaImage img, string path) => File.WriteAllBytes(path, Encode(img));

    public static RgbaImage Decode(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("This file is not a PNG image.");
        int p = 8, w = 0, h = 0, depth = 0, ctype = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        var idat = new MemoryStream();
        while (p + 8 <= data.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p));
            string type = Encoding.ASCII.GetString(data, p + 4, 4);
            if (len < 0 || p + 12 + len > data.Length) throw new InvalidDataException("The PNG file is damaged.");
            var body = data.AsSpan(p + 8, len);
            switch (type)
            {
                case "IHDR":
                    w = BinaryPrimitives.ReadInt32BigEndian(body); h = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    depth = body[8]; ctype = body[9]; interlace = body[12];
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "tRNS": trns = body.ToArray(); break;
                case "IDAT": idat.Write(body); break;
            }
            p += 12 + len;
            if (type == "IEND") break;
        }
        if (w <= 0 || h <= 0 || w > 8192 || h > 8192) throw new InvalidDataException("The PNG has an unsupported size.");
        int channels = ctype switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException("Unsupported PNG colour type.") };
        if (depth is not (1 or 2 or 4 or 8 or 16)) throw new InvalidDataException("Unsupported PNG bit depth.");
        if (ctype == 3 && palette == null) throw new InvalidDataException("The PNG palette is missing.");

        idat.Position = 0;
        byte[] raw;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        using (var ms = new MemoryStream())
        {
            z.CopyTo(ms);
            raw = ms.ToArray();
        }

        var img = new RgbaImage(w, h);
        int bpp = Math.Max(1, channels * depth / 8);   // bytes per complete pixel, for filtering
        int pos = 0;
        if (interlace == 0)
        {
            pos = DecodePass(raw, pos, w, h, channels, depth, bpp, (x, y) => (x, y), img, palette, trns, ctype);
        }
        else
        {
            int[] sx = [0, 4, 0, 2, 0, 1, 0], sy = [0, 0, 4, 0, 2, 0, 1], dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
            for (int pass = 0; pass < 7; pass++)
            {
                int pw = (w - sx[pass] + dx[pass] - 1) / dx[pass], ph = (h - sy[pass] + dy[pass] - 1) / dy[pass];
                if (pw <= 0 || ph <= 0) continue;
                int ps = pass;
                pos = DecodePass(raw, pos, pw, ph, channels, depth, bpp, (x, y) => (sx[ps] + x * dx[ps], sy[ps] + y * dy[ps]), img, palette, trns, ctype);
            }
        }
        return img;
    }

    static int DecodePass(byte[] raw, int pos, int w, int h, int channels, int depth, int bpp,
        Func<int, int, (int, int)> map, RgbaImage img, byte[]? palette, byte[]? trns, int ctype)
    {
        int stride = (w * channels * depth + 7) / 8;
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            if (pos + 1 + stride > raw.Length) throw new InvalidDataException("The PNG file is damaged.");
            byte filter = raw[pos++];
            Array.Copy(raw, pos, cur, 0, stride);
            pos += stride;
            Unfilter(filter, cur, prev, bpp);
            for (int x = 0; x < w; x++)
            {
                var (tx, ty) = map(x, y);
                img.Set(tx, ty, Pixel(cur, x, channels, depth, palette, trns, ctype));
            }
            (prev, cur) = (cur, prev);
        }
        return pos;
    }

    static void Unfilter(byte f, byte[] cur, byte[] prev, int bpp)
    {
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
            cur[i] = f switch
            {
                0 => cur[i],
                1 => (byte)(cur[i] + a),
                2 => (byte)(cur[i] + b),
                3 => (byte)(cur[i] + ((a + b) >> 1)),
                4 => (byte)(cur[i] + Paeth(a, b, c)),
                _ => throw new InvalidDataException("The PNG file is damaged."),
            };
        }
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static int Sample(byte[] row, int index, int depth)
    {
        switch (depth)
        {
            case 8: return row[index];
            case 16: return row[2 * index];   // high byte
            default:
                int bit = index * depth;
                int v = (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                return v;
        }
    }

    static int Scale(int v, int depth) => depth switch { 1 => v * 255, 2 => v * 85, 4 => v * 17, _ => v };

    static uint Pixel(byte[] row, int x, int channels, int depth, byte[]? palette, byte[]? trns, int ctype)
    {
        int b0 = x * channels;
        switch (ctype)
        {
            case 0:
            {
                int raw = Sample(row, b0, depth), g = Scale(raw, depth);
                byte a = 255;
                if (trns is { Length: >= 2 })
                {
                    int t = BinaryPrimitives.ReadUInt16BigEndian(trns);
                    int full = depth == 16 ? (row[2 * b0] << 8 | row[2 * b0 + 1]) : raw;
                    if (full == t) a = 0;
                }
                return RgbaImage.Pack((byte)g, (byte)g, (byte)g, a);
            }
            case 2:
            {
                byte r = (byte)Sample(row, b0, depth), g = (byte)Sample(row, b0 + 1, depth), b = (byte)Sample(row, b0 + 2, depth);
                byte a = 255;
                if (trns is { Length: >= 6 })
                {
                    if (depth == 16)
                    {
                        if (Rd16(row, b0) == Rd16(trns, 0, true) && Rd16(row, b0 + 1) == Rd16(trns, 1, true) && Rd16(row, b0 + 2) == Rd16(trns, 2, true)) a = 0;
                    }
                    else if (r == trns[1] && g == trns[3] && b == trns[5]) a = 0;
                }
                return RgbaImage.Pack(r, g, b, a);
            }
            case 3:
            {
                int i = Sample(row, x, depth);
                if (3 * i + 2 >= palette!.Length) return 0;
                byte a = trns != null && i < trns.Length ? trns[i] : (byte)255;
                return RgbaImage.Pack(palette[3 * i], palette[3 * i + 1], palette[3 * i + 2], a);
            }
            case 4:
            {
                byte g = (byte)Sample(row, b0, depth), a = (byte)Sample(row, b0 + 1, depth);
                return RgbaImage.Pack(g, g, g, a);
            }
            default:
                return RgbaImage.Pack((byte)Sample(row, b0, depth), (byte)Sample(row, b0 + 1, depth), (byte)Sample(row, b0 + 2, depth), (byte)Sample(row, b0 + 3, depth));
        }
    }

    static int Rd16(byte[] row, int sampleIndex) => row[2 * sampleIndex] << 8 | row[2 * sampleIndex + 1];
    static int Rd16(byte[] trns, int i, bool _) => trns[2 * i] << 8 | trns[2 * i + 1];

    public static byte[] Encode(RgbaImage img)
    {
        var o = new MemoryStream();
        o.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, img.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), img.Height);
        ihdr[8] = 8; ihdr[9] = 6;   // 8-bit RGBA, no interlace
        WriteChunk(o, "IHDR", ihdr);
        var raw = new MemoryStream();
        var px = img.ToRgbaBytes();
        int stride = img.Width * 4;
        for (int y = 0; y < img.Height; y++)
        {
            raw.WriteByte(0);
            raw.Write(px, y * stride, stride);
        }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.SmallestSize, leaveOpen: true)) { raw.Position = 0; raw.CopyTo(zs); }
        WriteChunk(o, "IDAT", z.ToArray());
        WriteChunk(o, "IEND", []);
        return o.ToArray();
    }

    static void WriteChunk(Stream o, string type, byte[] body)
    {
        Span<byte> b4 = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b4, body.Length); o.Write(b4);
        var t = Encoding.ASCII.GetBytes(type);
        o.Write(t); o.Write(body);
        uint crc = Crc32(t, 0xFFFFFFFF);
        crc = Crc32(body, crc) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(b4, crc); o.Write(b4);
    }

    static readonly uint[] CrcTable = BuildCrc();

    static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    static uint Crc32(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
