using System.Reflection;
using Mcd2SkinStudio.App;
using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Imaging;

// usage: UiShots <outDir> [edited.png]
// Opens the real MainForm off-screen with a throw-away data folder and saves each page as a PNG.
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "shots");
Directory.CreateDirectory(outDir);
var temp = Directory.CreateTempSubdirectory("mcd2shots").FullName;
AppPaths.OverrideDataDir(Path.Combine(temp, "data"));
AppPaths.OverrideExportDir(Path.Combine(temp, "export"));

var thread = new Thread(() =>
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    WindowsFormsSynchronizationContext.AutoInstall = false;
    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    var f = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0), ShowInTaskbar = false };
    f.Show();

    void Pump(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < until || f.IsBusy) { Application.DoEvents(); Thread.Sleep(10); }
    }
    void Shot(string name)
    {
        Pump(300);
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(Path.Combine(outDir, name + ".png"));
        Console.WriteLine(name);
    }

    Pump(1500);
    Shot("1-home-empty");
    f.ShowWizard();
    Shot("2-wizard-game");
    var wiz = f.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<Control>()).First(c => c.GetType().Name == "WizardPage");
    var go = wiz.GetType().GetMethod("Go", BindingFlags.NonPublic | BindingFlags.Instance)!;
    go.Invoke(wiz, [1]);
    Shot("3-wizard-hero");
    var skin = f.Studio.FindSkin("Tank");
    wiz.GetType().GetField("_skin", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wiz, skin);
    go.Invoke(wiz, [2]);
    Shot("4-wizard-download");
    var orig = f.Studio.Original(skin!);
    var edited = args.Length > 1 ? Png.Load(args[1]) : orig.Clone();
    if (args.Length < 2) edited.FillRect(20, 20, 8, 12, RgbaImage.Pack(40, 90, 230, 255));
    wiz.GetType().GetField("_edited", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wiz, edited);
    go.Invoke(wiz, [3]);
    Shot("5-wizard-upload");
    // home with a saved (not installed) skin, then help
    f.Studio.SaveSkin(skin!, edited);
    f.ShowHome();
    Shot("6-home-with-skin");
    f.ShowHelp();
    Shot("7-help");
    f.Close();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Directory.Delete(temp, true);
