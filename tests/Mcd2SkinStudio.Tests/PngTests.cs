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

    [Fact]
    public void Import_accepts_64_and_exact_512()
    {
        var a = SkinImport.Load(Fx("rgba64.png"));
        var b = SkinImport.Load(Fx("rgba512_exact.png"));
        Assert.True(a.SameAs(b));
    }

    [Theory]
    [InlineData("rgba512_blurry.png")]
    [InlineData("rgba100.png")]
    [InlineData("notapng.png")]
    [InlineData("missing.png")]
    public void Import_rejects_with_a_friendly_message(string name)
    {
        var e = Assert.Throws<SkinImageException>(() => SkinImport.Load(Fx(name)));
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
    }

    [Fact]
    public void Palette_change_is_detected_and_restorable()
    {
        var orig = Png.Load(Fx("rgba64.png"));
        var edit = orig.Clone();
        edit.Set(30, 30, RgbaImage.Pack(1, 2, 3, 255));
        Assert.False(SkinImport.PaletteChanged(orig, edit));
        edit.Set(6, 5, RgbaImage.Pack(9, 9, 9, 255));
        Assert.True(SkinImport.PaletteChanged(orig, edit));
        var fixedUp = SkinImport.RestorePalette(orig, edit);
        Assert.False(SkinImport.PaletteChanged(orig, fixedUp));
        Assert.Equal(RgbaImage.Pack(1, 2, 3, 255), fixedUp.Get(30, 30));
    }

    [Fact]
    public void Guide_and_front_render_have_expected_sizes()
    {
        var s = Png.Load(Fx("rgba64.png"));
        Assert.Equal((16, 32), (SkinRender.Front(s).Width, SkinRender.Front(s).Height));
        var g = SkinRender.Guide(s);
        Assert.Equal(1024, g.Width);
        Assert.True(g.Height > 1024);
    }
}
