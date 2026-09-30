using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Tests;

public class PngTests
{
    static string Fx(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("rgba64.png", "rgba64.raw")]
    [InlineData("rgba64_interlaced.png", "rgba64.raw")]
    [InlineData("rgb64.png", "rgb64.raw")]
    [InlineData("gray64.png", "gray64.raw")]
    [InlineData("graya64.png", "graya64.raw")]
    [InlineData("pal64.png", "pal64.raw")]
    [InlineData("pal64_trns.png", "pal64_trns.raw")]
    public void Decodes_every_colour_type_like_pillow(string png, string raw)
    {
        var img = Png.Load(Fx(png));
        Assert.Equal(64, img.Width);
        Assert.Equal(File.ReadAllBytes(Fx(raw)), img.ToRgbaBytes());
    }

    [Fact]
    public void Decodes_16_bit_grey_using_high_byte()
    {
        var img = Png.Load(Fx("gray16_64.png"));
        Assert.Equal(64, img.Width);
        Assert.Equal(255, img.Alpha(3, 3));
    }

    [Fact]
    public void Encode_then_decode_is_lossless()
    {
        var img = Png.Load(Fx("rgba64.png"));
        var back = Png.Decode(Png.Encode(img));
        Assert.True(img.SameAs(back));
    }

    [Fact]
    public void Rejects_non_png()
    {
        Assert.Throws<InvalidDataException>(() => Png.Load(Fx("notapng.png")));
    }
}
