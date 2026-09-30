using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Game;
using Mcd2SkinStudio.Core.Keys;
using Mcd2SkinStudio.Core.Mods;
using Mcd2SkinStudio.Core.Skins;

namespace Mcd2SkinStudio.Tests;

public class CatalogTests
{
    const string Root = "../../../Dungeons/Content/Spicewood/Art/Characters/Player/Skins/";

    static readonly string[] Paths =
    [
        Root + "Tank/T_Tank_Skin.uasset", Root + "Tank/T_Tank_MRES.uasset", Root + "Tank/T_Tank_Skin_Icon.uasset",
        Root + "Tank/T_Tank_Skin_Deluxe.uasset", Root + "Tank/T_Tank_Skin_Deluxe_Icon.uasset", Root + "Tank/MI_Tank_Skin.uasset",
        Root + "Alex/T_Alex_Skin_PreOrder.uasset", Root + "Alex/T_Alex_Preorder_Skin_MRES.uasset",
        Root + "_Green/T_Green_Skin.uasset",
        Root + "Tank/Sub/T_Nested.uasset",
        "../../../Dungeons/Content/Other/T_Unrelated.uasset",
    ];

    [Fact]
    public void Finds_skin_textures_by_pattern()
    {
        var c = SkinCatalog.Build(Paths);
        Assert.Equal(["Alex_PreOrder", "Green", "Tank", "Tank_Deluxe"], c.Skins.Select(s => s.Key).Order().ToArray());
        var tank = c.Find("Tank")!;
        Assert.EndsWith("T_Tank_MRES.uasset", tank.MresPath);
        Assert.EndsWith("T_Tank_Skin_Icon.uasset", tank.IconPath);
        Assert.EndsWith("T_Tank_Skin_Deluxe_Icon.uasset", c.Find("Tank_Deluxe")!.IconPath);
    }

    [Fact]
    public void Applies_in_game_names_from_the_skin_table()
    {
        const string json = """
        [
          { "Name": "Tank", "AvatarMaterial": "/Game/Spicewood/Art/Characters/Player/Skins/Tank/MI_Tank_Skin.MI_Tank_Skin",
            "TypeTag": { "TagName": "SW.Skin.Tank" },
            "TypeTextData": { "Texts": { "x": { "Text": "LOCTABLE(\"DT_SkinDefinition.csv\", \"SW_Skin_Tank_Name\")" } } } },
          { "Name": "TankDeluxe", "AvatarMaterial": "/Game/Spicewood/Art/Characters/Player/Skins/Tank/MI_Tank_Skin_Deluxe.MI_Tank_Skin_Deluxe",
            "TypeTextData": { "Texts": { "x": { "Text": "LOCTABLE(\"DT_SkinDefinition.csv\", \"SW_Skin_TankDeluxe_Name\")" } } } },
          { "Name": "AlexPreorder", "AvatarMaterial": "/Game/Spicewood/Art/Characters/Player/Skins/Alex/MI_Alex_Skin_PreOrder.MI_Alex_Skin_PreOrder" }
        ]
        """;
        const string csv = "\"Key\",\"SourceString\",\"ContextDescription\"\r\n\"SW_Skin_Tank_Name\",\"Kellen Skin\",\"x\"\r\n\"SW_Skin_TankDeluxe_Name\",\"Fancy \"\"Kellen\"\" Skin\",\"x\"\r\n";
        var rows = SkinNames.Parse(json, SkinNames.ParseCsv(csv));
        var c = SkinCatalog.Build(Paths).WithNames(rows);
        Assert.Equal("Kellen", c.Find("Tank")!.DisplayName);
        Assert.Equal("Tank", c.Find("Tank")!.InternalName);
        Assert.Equal("Fancy \"Kellen\"", c.Find("Tank_Deluxe")!.DisplayName);
        Assert.Equal("AlexPreorder", c.Find("Alex_PreOrder")!.DisplayName);   // no string: row name
        Assert.Equal("Green (starter)", c.Find("Green")!.DisplayName);       // no row: folder name
        Assert.Same(c.Find("Kellen"), c.Find("Tank"));
    }
}

public class KeyTests
{
    [Fact]
    public void Parses_entries_and_matches_fingerprint()
    {
        var e = new KeyEntry { Edition = "WinGDK", UtocSize = 17308430, ContainerId = "0x829066CF1C97CDE0", Key = "0x" + new string('A', 64) };
        Assert.Equal(32, e.KeyBytes()!.Length);
        Assert.True(e.Matches(new Fingerprint(Edition.Xbox, 17308430, 0x829066CF1C97CDE0, DateTime.UtcNow)));
        Assert.False(e.Matches(new Fingerprint(Edition.Steam, 17308430, 0x829066CF1C97CDE0, DateTime.UtcNow)));
        Assert.Null(new KeyEntry { Key = "0x1234" }.KeyBytes());
    }

    [Fact]
    public void Embedded_keys_json_loads()
    {
        var p = new KeyProvider();
        Assert.NotEmpty(p.Entries);
        Assert.All(p.Entries, e => Assert.NotNull(e.KeyBytes()));
    }
}

public class InstallerTests : IDisposable
{
    readonly string _root;
    readonly GameInstall _game;

    public InstallerTests()
    {
        _root = TestData.NewDir("game");
        var paks = Path.Combine(_root, "Dungeons", "Content", "Paks");
        Directory.CreateDirectory(paks);
        var utoc = new byte[0x90];
        BitConverter.GetBytes(0x1122334455667788UL).CopyTo(utoc, 0x38);
        File.WriteAllBytes(Path.Combine(paks, "Dungeons-WinGDK.utoc"), utoc);
        _game = GameInstall.FromFolder(paks)!;
    }

    public void Dispose() => Directory.Delete(_root, true);

    static Dictionary<string, byte[]> Files(byte fill) => new()
    {
        [ModBuilder.ModName + ".utoc"] = [fill, 1],
        [ModBuilder.ModName + ".ucas"] = [fill, 2],
        [ModBuilder.ModName + ".pak"] = [fill, 3],
    };

    [Fact]
    public void Detects_install_from_any_folder_inside_it()
    {
        Assert.Equal(Edition.Xbox, _game.Edition);
        Assert.Equal(_root, GameInstall.FromFolder(_root)!.Root);
        Assert.Equal(_root, GameInstall.FromFolder(Path.Combine(_root, "Dungeons"))!.Root);
        Assert.Null(GameInstall.FromFolder(Path.GetTempPath() + "nope-" + Guid.NewGuid()));
    }

    [Fact]
    public void Install_check_uninstall_touches_only_our_files()
    {
        Assert.Equal(InstallState.NotInstalled, Installer.Check(_game));
        var files = Files(7);
        Installer.Install(_game, files, Installer.NewMarker(_game, ["Kellen"], files, "test"));
        Assert.Equal(InstallState.Ok, Installer.Check(_game));

        // somebody else's mod in the same folder must survive our uninstall
        var foreign = Path.Combine(_game.ModsDir, "zzz_SomeoneElse_P.pak");
        File.WriteAllBytes(foreign, [1]);

        File.WriteAllBytes(Path.Combine(_game.ModsDir, ModBuilder.ModName + ".ucas"), [9]);
        Assert.Equal(InstallState.FilesChanged, Installer.Check(_game));
        File.Delete(Path.Combine(_game.ModsDir, ModBuilder.ModName + ".ucas"));
        Assert.Equal(InstallState.FilesMissing, Installer.Check(_game));

        Assert.Equal(2, Installer.Uninstall(_game));
        Assert.True(File.Exists(foreign));
        Assert.True(Directory.Exists(_game.ModsDir));   // not empty, so it stays
        Assert.Equal(InstallState.NotInstalled, Installer.Check(_game));
    }

    [Fact]
    public void Uninstall_removes_mods_folder_only_if_we_created_it_and_it_is_empty()
    {
        var files = Files(1);
        Installer.Install(_game, files, Installer.NewMarker(_game, ["A"], files, "test"));
        Installer.Uninstall(_game);
        Assert.False(Directory.Exists(_game.ModsDir));

        Directory.CreateDirectory(_game.ModsDir);
        Installer.Install(_game, files, Installer.NewMarker(_game, ["A"], files, "test"));
        Installer.Uninstall(_game);
        Assert.True(Directory.Exists(_game.ModsDir));
    }

    [Fact]
    public void Game_update_is_detected_from_the_fingerprint()
    {
        var files = Files(2);
        Installer.Install(_game, files, Installer.NewMarker(_game, ["A"], files, "test"));
        File.WriteAllBytes(_game.UtocPath, new byte[0x100]);   // different size and container id
        Assert.Equal(InstallState.GameUpdated, Installer.Check(_game));
    }

    [Fact]
    public void Staged_install_applies_after_verifying_hashes()
    {
        var files = Files(3);
        var stage = Installer.Stage(files, Installer.NewMarker(_game, ["A"], files, "test"));
        Installer.ApplyStage(_game, stage);
        Assert.Equal(InstallState.Ok, Installer.Check(_game));
        File.WriteAllBytes(Path.Combine(stage, ModBuilder.ModName + ".pak"), [0]);
        Assert.Throws<InvalidDataException>(() => Installer.ApplyStage(_game, stage));
        Installer.DeleteStage(stage);
        Assert.False(Directory.Exists(stage));
        Installer.DeleteStage(stage);   // already gone: no error
    }

    [Theory]
    [InlineData("zzz_MCD2SkinStudio_P.utoc", true)]
    [InlineData("zzz_MCD2SkinStudio_P.sig", false)]
    [InlineData("Dungeons-WinGDK.pak", false)]
    [InlineData("..\\zzz_MCD2SkinStudio_P.pak", false)]
    [InlineData("zzz_MCD2SkinStudio_P.pak\\..\\..\\x.pak", false)]
    public void Only_our_own_file_names_are_accepted(string name, bool ok) => Assert.Equal(ok, Installer.IsOurFile(name));

    [Fact]
    public void Refuses_to_write_foreign_names()
    {
        var files = new Dictionary<string, byte[]> { ["Dungeons-WinGDK.pak"] = [1] };
        Assert.Throws<ArgumentException>(() => Installer.Install(_game, files, Installer.NewMarker(_game, [], files, "t")));
    }
}
