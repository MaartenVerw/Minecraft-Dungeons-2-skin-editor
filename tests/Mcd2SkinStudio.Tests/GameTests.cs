using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.IoStore;
using Mcd2SkinStudio.Core.Keys;
using Mcd2SkinStudio.Core.Mods;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Tests;

/// <summary>Runs only on a machine with the game installed (or MCD2_GAME set). Reads the real game
/// read-only and writes nothing outside a temp folder. No game data is committed.</summary>
public sealed class GameFactAttribute : FactAttribute
{
    public GameFactAttribute()
    {
        if (GameTests.Install == null) Skip = "Minecraft Dungeons II is not installed here (set MCD2_GAME to its folder).";
    }
}

public class GameTests
{
    internal static readonly GameInstall? Install =
        GameInstall.FromFolder(Environment.GetEnvironmentVariable("MCD2_GAME")) ?? GameLocator.FindAll().FirstOrDefault();

    static GameArchive Open()
    {
        var k = new KeyProvider().Resolve(Install!) ?? throw new Exception("no key");
        return GameArchive.Open(Install!, k.Key);
    }

    [GameFact]
    public void Every_skin_texture_decompresses_and_has_a_64x64_mip()
    {
        using var a = Open();
        Assert.True(a.Catalog.Skins.Count >= 20);
        foreach (var s in a.Catalog.Skins)
        {
            var pkg = a.ReadPackage(s.TexturePath);
            var img = TextureIO.ExtractSkin(pkg);
            Assert.Equal(64, img.Width);
            Assert.True(a.Header.TryGetEntry(BitConverter.ToUInt64(a.ChunkIdOf(s.TexturePath)), out _), s.Key);
        }
    }

    [GameFact]
    public void Skins_have_in_game_names()
    {
        using var a = Open();
        Assert.Contains(a.Catalog.Skins, s => s.InternalName == "Tank" && s.DisplayName != "Tank");
    }

    [GameFact]
    public void Built_mod_reads_back_with_the_new_pixels()
    {
        using var a = Open();
        var s = a.Catalog.Find("Tank")!;
        var img = TextureIO.ExtractSkin(a.ReadPackage(s.TexturePath));
        img.FillRect(20, 20, 8, 12, RgbaImage.Pack(255, 0, 0, 255));
        var files = ModBuilder.Build(a, [new SkinReplacement(s, img)]);
        var dir = TestData.NewDir("mod");
        foreach (var (n, d) in files) File.WriteAllBytes(Path.Combine(dir, n), d);
        using var r = IoStoreReader.Open(Path.Combine(dir, ModBuilder.ModName + ".utoc"), a.Key);
        Assert.True(r.Encrypted);
        var back = r.ReadChunk(r.Files[ContainerWriter.MountPoint + a.RelativePath(s.TexturePath)]);
        Assert.True(TextureIO.ExtractSkin(back).SameAs(img));
        var original = a.ReadPackage(s.TexturePath);
        Assert.Equal(original.Length, back.Length);   // only pixel bytes differ
        Directory.Delete(dir, true);
    }
}

public class GameGeometryTests
{
    /// <summary>The in-game face built from SkinGeometry must match the game's own hand-drawn portraits
    /// better than its mirror image: proves the face-animation mapping and left/right orientation.</summary>
    [GameFact]
    public void In_game_face_matches_the_games_portraits()
    {
        var k = new KeyProvider().Resolve(GameTests.Install!)!;
        using var a = GameArchive.Open(GameTests.Install!, k.Key);
        int closer = 0, exact = 0;
        foreach (var s in a.Catalog.Skins)
        {
            var img = TextureIO.ExtractSkin(a.ReadPackage(s.TexturePath));
            var face = SkinGeometry.FaceAsSeen(img);
            long d = 0, dm = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    var p = RgbaImage.Unpack(img.Get(SkinGeometry.PortraitX + x, SkinGeometry.PortraitY + y));
                    var f = RgbaImage.Unpack(face.Get(x, y));
                    var m = RgbaImage.Unpack(face.Get(7 - x, y));
                    d += Math.Abs(p.R - f.R) + Math.Abs(p.G - f.G) + Math.Abs(p.B - f.B);
                    dm += Math.Abs(p.R - m.R) + Math.Abs(p.G - m.G) + Math.Abs(p.B - m.B);
                }
            if (d <= dm) closer++;
            if (d == 0) exact++;
        }
        Assert.Equal(a.Catalog.Skins.Count, closer);
        Assert.True(exact >= 5, $"only {exact} exact portrait matches");
    }

    [GameFact]
    public void Every_stock_skin_survives_a_design_sheet_round_trip()
    {
        var k = new KeyProvider().Resolve(GameTests.Install!)!;
        using var a = GameArchive.Open(GameTests.Install!, k.Key);
        foreach (var s in a.Catalog.Skins)
        {
            var img = TextureIO.ExtractSkin(a.ReadPackage(s.TexturePath));
            var sheet = Png.Decode(Png.Encode(DesignSheet.Create(img, s.DisplayName, s.Key)));
            Assert.True(DesignSheet.Read(sheet, img).SameAs(img), s.Key);
            Assert.True(SkinImport.Unchanged(img, DesignSheet.Read(sheet, img)));
            Assert.Null(SkinImport.LostTransparency(img, img));
        }
    }
}
