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
        var dir = Directory.CreateTempSubdirectory("mcd2mod").FullName;
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
