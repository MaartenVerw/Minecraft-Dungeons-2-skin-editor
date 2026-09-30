using System.Reflection;
using Mcd2SkinStudio.App;
using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Imaging;
using Mcd2SkinStudio.Core.Skins;

// usage: UiShots <outDir>
// Opens the real MainForm off-screen with a throw-away data folder and saves each page as a PNG,
// at the screen's real scaling (like the app).
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "shots");
Directory.CreateDirectory(outDir);
var temp = Directory.CreateTempSubdirectory("mcd2shots").FullName;
AppPaths.OverrideDataDir(Path.Combine(temp, "data"));
AppPaths.OverrideExportDir(Path.Combine(temp, "export"));

var thread = new Thread(() =>
{
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.SetDefaultFont(new Font("Segoe UI", 9.75f));
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
        Console.WriteLine($"{name} ({f.Width}x{f.Height}, scale {Ui.Scale})");
    }
    T Find<T>(Control root) where T : Control =>
        root is T t ? t : root.Controls.Cast<Control>().Select(Find<T>).FirstOrDefault(x => x != null)!;

    Pump(1500);
    Shot("1-home-empty");
    f.ShowWizard();
    var wiz = Find<WizardPage>(f);
    var go = typeof(WizardPage).GetMethod("Go", BindingFlags.NonPublic | BindingFlags.Instance)!;
    go.Invoke(wiz, [1]);
    Shot("2-wizard-hero");
    var skin = f.Studio.FindSkin("Tank");
    typeof(WizardPage).GetField("_skin", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wiz, skin);
    go.Invoke(wiz, [2]);
    Pump(500);
    var ed = (SkinEditor)typeof(WizardPage).GetField("_editor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wiz)!;
    // paint a little so the previews show a change: a blue shirt front and green pupils
    var front = SkinGeometry.Get(Part.Body, Face.Front);
    var page = EditorLayout.Page(EditorView.Body);
    ed.Fill(page, page.Cells.First(c => (c.Tx, c.Ty) == front.Texel(3, 3)), RgbaImage.Pack(40, 90, 230, 255));
    ed.Paint(SkinGeometry.PupilA.X, SkinGeometry.PupilA.Y, RgbaImage.Pack(30, 160, 60, 255));
    ed.Paint(SkinGeometry.PupilB.X, SkinGeometry.PupilB.Y, RgbaImage.Pack(30, 160, 60, 255));
    var editor = Find<SkinEditorControl>(f);
    foreach (var v in new[] { EditorView.Head, EditorView.Body, EditorView.Arms, EditorView.Face, EditorView.Hat })
    {
        editor.ShowView(v);
        Shot($"3-paint-{v.ToString().ToLowerInvariant()}");
    }
    // mouse: drag a brush line across the body front, pick up a colour, then hover a leg square
    editor.ShowView(EditorView.Body);
    Pump(200);
    var canvas = Find<PixelCanvas>(f);
    int u = (int)typeof(PixelCanvas).GetField("_u", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(canvas)!;
    var o = (Point)typeof(PixelCanvas).GetField("_o", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(canvas)!;
    void Mouse(string what, MouseButtons b, EditorCell c, double fx = 0.5)
    {
        var m = typeof(PixelCanvas).GetMethod("OnMouse" + what, BindingFlags.NonPublic | BindingFlags.Instance)!;
        m.Invoke(canvas, [new MouseEventArgs(b, 1, o.X + (int)((c.X + fx) * u), o.Y + (int)((c.Y + 0.5) * u), 0)]);
    }
    EditorCell At(EditorView v, Part p, Face fc, int col, int row)
    {
        var t = SkinGeometry.Get(p, fc).Texel(col, row);
        return EditorLayout.Page(v).Cells.Single(c => (c.Tx, c.Ty) == t);
    }
    var bodyPage = EditorLayout.Page(EditorView.Body);
    Mouse("Down", MouseButtons.Right, At(EditorView.Body, Part.Body, Face.Front, 0, 11));   // pick a colour
    Console.WriteLine($"picked {Find<ColourPicker>(f).Colour:X8} = texel {ed.Skin.Get(SkinGeometry.Get(Part.Body, Face.Front).Texel(0, 11).X, SkinGeometry.Get(Part.Body, Face.Front).Texel(0, 11).Y):X8}");
    Find<ColourPicker>(f).Colour = RgbaImage.Pack(255, 200, 0, 255);
    Mouse("Down", MouseButtons.Left, At(EditorView.Body, Part.Body, Face.Right, 0, 9));
    Mouse("Move", MouseButtons.Left, At(EditorView.Body, Part.Body, Face.Back, 7, 9));     // one fast move across 3 faces
    Mouse("Up", MouseButtons.Left, At(EditorView.Body, Part.Body, Face.Back, 7, 9));
    int painted = bodyPage.Cells.Count(c => ed.Skin.Get(c.Tx, c.Ty) == RgbaImage.Pack(255, 200, 0, 255));
    Console.WriteLine($"brush drag painted {painted} squares (expect 24)");
    Mouse("Move", MouseButtons.None, At(EditorView.Body, Part.Body, Face.Front, 2, 4));
    Shot("3-paint-drag-and-hover");
    // eraser on a solid square gives the game's colour back
    typeof(SkinEditorControl).GetMethod("SetTool", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [Tool.Eraser]);
    var belt = At(EditorView.Body, Part.Body, Face.Front, 3, 9);
    Mouse("Down", MouseButtons.Left, belt);
    Mouse("Up", MouseButtons.Left, belt);
    Console.WriteLine($"eraser restored original: {ed.Skin.Get(belt.Tx, belt.Ty) == ed.Original.Get(belt.Tx, belt.Ty)}");
    ed.Undo();
    Console.WriteLine($"undo brought the paint back: {ed.Skin.Get(belt.Tx, belt.Ty) == RgbaImage.Pack(255, 200, 0, 255)}");

    // home with a saved (not installed) skin, then help
    f.Studio.SaveSkin(skin!, ed.Skin.Clone());
    typeof(WizardPage).GetField("_installed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wiz, true);
    f.ShowHome();
    Shot("4-home-with-skin");
    f.ShowHelp();
    Shot("5-help");
    f.Close();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Directory.Delete(temp, true);
