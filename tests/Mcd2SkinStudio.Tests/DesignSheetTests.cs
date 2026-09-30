using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Tests;

public class DesignSheetTests
{
    static string Fx(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>A synthetic skin (not game art) with every texel distinct and a transparent hat area.</summary>
    static RgbaImage Skin()
    {
        var img = new RgbaImage(64, 64);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                img.Set(x, y, RgbaImage.Pack((byte)(x * 4), (byte)(y * 4), (byte)((x * 7 + y * 13) & 255), 255));
        img.FillRect(40, 0, 16, 8, 0);   // hat top/bottom transparent
        img.FillRect(32, 8, 32, 8, 0);   // hat sides transparent
        return img;
    }

    [Fact]
    public void Geometry_faces_never_overlap()
    {
        var seen = new HashSet<(int, int)>();
        foreach (var f in SkinGeometry.Faces)
            for (int r = 0; r < f.H; r++)
                for (int c = 0; c < f.W; c++)
                    Assert.True(seen.Add(f.Texel(c, r)), $"{f.Part} {f.Face} overlaps at {f.Texel(c, r)}");
        Assert.Equal(7 * 6, SkinGeometry.Faces.Count);
    }

    [Fact]
    public void Every_texel_the_game_reads_is_on_the_sheet_exactly_once()
    {
        var onSheet = DesignSheet.Cells.Select(c => (c.Tx, c.Ty)).ToList();
        Assert.Equal(onSheet.Count, onSheet.Distinct().Count());
        Assert.Equal(SkinImport.UsedTexels().ToHashSet(), onSheet.ToHashSet());
    }

    [Fact]
    public void Sheet_cells_do_not_overlap_and_fit()
    {
        var px = new HashSet<(int, int)>();
        foreach (var c in DesignSheet.Cells)
        {
            Assert.InRange(c.X + c.Size, 0, DesignSheet.Width);
            Assert.InRange(c.Y + c.Size, 0, DesignSheet.Height);
            Assert.True(px.Add((c.X / 4, c.Y / 4)) || c.Size < 8);
        }
    }

    [Fact]
    public void Unpainted_sheet_reads_back_to_the_same_skin()
    {
        var skin = Skin();
        var sheet = DesignSheet.Create(skin, "Test Hero", "Tank");
        Assert.True(DesignSheet.IsSheet(sheet));
        Assert.True(DesignSheet.Read(sheet, skin).SameAs(skin));
        // and through PNG, as the user saves it
        var back = Png.Decode(Png.Encode(sheet));
        Assert.True(DesignSheet.Read(back, skin).SameAs(skin));
    }

    [Fact]
    public void Paint_strokes_land_on_the_right_texels()
    {
        var skin = Skin();
        var sheet = DesignSheet.Create(skin, "Test Hero", "Tank");
        uint red = RgbaImage.Pack(255, 0, 0, 255);
        // paint the whole body front, overshooting into the grid lines like a sloppy brush would
        var bodyFront = SkinGeometry.Get(Part.Body, Face.Front);
        var cells = DesignSheet.Cells.Where(c => c.Tx >= bodyFront.U && c.Tx < bodyFront.U + bodyFront.W && c.Ty >= bodyFront.V && c.Ty < bodyFront.V + bodyFront.H).ToList();
        foreach (var c in cells) sheet.FillRect(c.X - 1, c.Y - 1, c.Size + 1, c.Size + 1, red);
        // one pupil with a small anti-aliased edge
        var pupil = DesignSheet.Cells.Single(c => (c.Tx, c.Ty) == SkinGeometry.PupilA);
        sheet.FillRect(pupil.X + 3, pupil.Y + 3, pupil.Size - 6, pupil.Size - 6, RgbaImage.Pack(0, 200, 0, 255));
        sheet.FillRect(pupil.X + 6, pupil.Y + 6, 2, 2, RgbaImage.Pack(10, 120, 10, 128));
        // make one hat square solid
        var hat = DesignSheet.Cells.First(c => c.Tx == 40 && c.Ty == 8);
        sheet.FillRect(hat.X, hat.Y, hat.Size, hat.Size, red);

        var o = DesignSheet.Read(sheet, skin);
        for (int y = bodyFront.V; y < bodyFront.V + bodyFront.H; y++)
            for (int x = bodyFront.U; x < bodyFront.U + bodyFront.W; x++)
                Assert.Equal(red, o.Get(x, y));
        Assert.Equal(RgbaImage.Pack(0, 200, 0, 255), o.Get(6, 5));
        Assert.Equal(red, o.Get(40, 8));
        Assert.Equal(0u, o.Get(41, 8));                   // untouched hat stays transparent
        Assert.Equal(skin.Get(16, 20), o.Get(16, 20));    // body side untouched
        Assert.Equal(skin.Get(20, 36), o.Get(20, 36));    // unused jacket texel comes from the original
    }

    [Fact]
    public void Marker_names_the_skin_it_was_made_for()
    {
        var sheet = DesignSheet.Create(Skin(), "Kellen", "Tank");
        Assert.True(DesignSheet.MadeFor(sheet, "Tank"));
        Assert.False(DesignSheet.MadeFor(sheet, "Healer"));
        sheet.Set(0, 0, RgbaImage.Pack(1, 2, 3, 255));
        Assert.Null(DesignSheet.MadeFor(sheet, "Tank"));
    }

    [Fact]
    public void Import_recognises_sheet_texture_and_preview()
    {
        var skin = Skin();
        Assert.Equal(UploadKind.DesignSheet, SkinImport.Convert(DesignSheet.Create(skin, "X", "Tank"), skin, "Tank").Kind);
        Assert.Equal(UploadKind.Texture, SkinImport.Convert(skin, skin, "Tank").Kind);
        Assert.Equal(UploadKind.Preview8x, SkinImport.Convert(skin.ScaleNearest(8), skin, "Tank").Kind);
        var a = SkinImport.Load(Fx("rgba64.png"), skin, "Tank");
        var b = SkinImport.Load(Fx("rgba512_exact.png"), skin, "Tank");
        Assert.True(a.Skin.SameAs(b.Skin));
    }

    [Fact]
    public void Resized_sheet_gets_a_clear_message()
    {
        var skin = Skin();
        var half = new RgbaImage(DesignSheet.Width / 2, DesignSheet.Height / 2);
        var e = Assert.Throws<SkinImageException>(() => SkinImport.Convert(half, skin, "Tank"));
        Assert.Contains("resized", e.Message);
    }

    [Theory]
    [InlineData("rgba512_blurry.png")]
    [InlineData("rgba100.png")]
    [InlineData("notapng.png")]
    [InlineData("missing.png")]
    public void Import_rejects_with_a_friendly_message(string name)
    {
        var e = Assert.Throws<SkinImageException>(() => SkinImport.Load(Fx(name), Skin(), "Tank"));
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
    }

    [Fact]
    public void Lost_transparency_is_detected_and_restored()
    {
        var skin = Skin();
        var flattened = skin.Clone();
        uint white = RgbaImage.Pack(255, 255, 255, 255);
        foreach (var (x, y) in SkinImport.UsedTexels())
            if (skin.Alpha(x, y) == 0) flattened.Set(x, y, white);
        flattened.Set(44, 10, RgbaImage.Pack(9, 9, 9, 255));   // one hat pixel really painted
        Assert.Equal(white, SkinImport.LostTransparency(skin, flattened));
        var fixedUp = SkinImport.RestoreTransparency(skin, flattened, white);
        Assert.Equal(0u, fixedUp.Get(41, 8));
        Assert.Equal(RgbaImage.Pack(9, 9, 9, 255), fixedUp.Get(44, 10));
        Assert.Null(SkinImport.LostTransparency(skin, skin));
    }

    [Fact]
    public void In_game_face_places_eyes_mouth_and_brows()
    {
        var skin = new RgbaImage(64, 64);
        skin.FillRect(8, 8, 8, 8, RgbaImage.Pack(200, 150, 120, 255));  // plain face
        uint pa = RgbaImage.Pack(1, 0, 0, 255), pb = RgbaImage.Pack(2, 0, 0, 255), wo = RgbaImage.Pack(3, 0, 0, 255),
             wi = RgbaImage.Pack(4, 0, 0, 255), ma = RgbaImage.Pack(5, 0, 0, 255), mb = RgbaImage.Pack(6, 0, 0, 255), brow = RgbaImage.Pack(7, 0, 0, 255);
        skin.Set(6, 5, pa); skin.Set(7, 5, pb); skin.Set(6, 6, wo); skin.Set(7, 6, wi); skin.Set(6, 7, ma); skin.Set(7, 7, mb);
        skin.Set(3, 7, brow);
        var f = SkinGeometry.FaceAsSeen(skin);
        Assert.Equal(wo, f.Get(1, 4)); Assert.Equal(pa, f.Get(2, 4)); Assert.Equal(pb, f.Get(5, 4)); Assert.Equal(wo, f.Get(6, 4));
        Assert.Equal(ma, f.Get(3, 6)); Assert.Equal(mb, f.Get(4, 6));
        Assert.Equal(brow, f.Get(1, 3)); Assert.Equal(brow, f.Get(6, 3));
        Assert.Equal(RgbaImage.Pack(200, 150, 120, 255), f.Get(0, 0));
    }

    [Fact]
    public void Front_and_back_views_have_expected_sizes()
    {
        var s = Skin();
        Assert.Equal((16, 32), (SkinRender.Front(s).Width, SkinRender.Front(s).Height));
        Assert.Equal((16, 32), (SkinRender.Back(s).Width, SkinRender.Back(s).Height));
    }
}
