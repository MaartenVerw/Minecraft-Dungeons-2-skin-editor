using Mcd2SkinStudio.Core;
using Mcd2SkinStudio.Core.Game;

namespace Mcd2SkinStudio.App;

/// <summary>Help: how it works, editing tips, "It didn't work?" with Repair and a pre-filled issue.</summary>
sealed class HelpPage : FlowLayoutPanel
{
    public const string RepoUrl = "https://github.com/maartenverw06/Minecraft-Dungeons-2-skin-editor";

    public HelpPage(MainForm f)
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        int w = f.ContentWidth;

        Controls.Add(Ui.Label("Help", Ui.Title));

        Section("How it works",
            "MCD2 Skin Studio copies a hero's look out of your own game, lets you paint it in the built-in editor, and puts your version in the game's “~mods” folder. " +
            "The game's own files are never changed. “Remove all custom skins” deletes only the files this app made.");
        Section("The editor",
            "•  Every square is one pixel of your hero. Pick a part with the tabs above the squares, or click that part on the preview.\n" +
            "•  Each part is unfolded like a paper model: the big middle square is the front, the top and bottom are above and below it, " +
            "and the sides and back are next to it. Right and left are the hero's own right and left.\n" +
            "•  Brush (B): click or drag to paint squares. Fill (F): colours all touching squares of the same colour. " +
            "Eraser (E): empties hat and face squares; on other parts it puts the game's colour back.\n" +
            "•  Pick a colour from the presets, the colour wheel (with the light/dark slider) or type a hex code like #3C8527. " +
            "Right-click a square to pick up its colour.\n" +
            "•  The live preview shows your hero from the front and back while you paint. Point at a square and it's marked on the preview.\n" +
            "•  Undo with Ctrl+Z, redo with Ctrl+Y. “Start over” brings back the game's look.");
        Section("Eyes, eyebrows and mouth",
            "The head's front has no face on it: the game draws and animates the face from the squares under “Face & portrait”.\n" +
            "•  Top line: the pupils (left square = the eye on your left, right square = the eye on your right).\n" +
            "•  Middle line: the eye whites, two pixels per eye (outer and inner; the pupil covers the inner one).\n" +
            "•  Bottom line: the mouth, two pixels in the middle of the face.\n" +
            "•  The block on the left is the eyebrow shape (4 × 2). It's drawn above both eyes, mirrored for the other eye. Empty squares = no eyebrow there.\n" +
            "•  The portrait is the small face picture in the Locker. Paint a whole face there, with eyes and mouth.");
        Section("After a game update",
            "Game updates can remove or break custom skins. Open MCD2 Skin Studio: it notices the update and offers Repair, which rebuilds your skins for the new version.");

        Controls.Add(Ui.Label("It didn't work?", Ui.H2));
        Controls.Add(Ui.Label("1.  Close the game completely.\n2.  Press Repair below.\n3.  Start the game and look at the hero in the Locker.\nStill the normal look? Report it, and the details of your game are filled in for you.", null, null, w));
        var row = Ui.Row();
        row.Controls.Add(Ui.Primary("Repair", async (_, _) =>
        {
            string msg = "";
            if (await f.Busy("Repairing…", async () => msg = await f.Studio.RepairAsync())) f.Info(msg);
        }));
        row.Controls.Add(Ui.Secondary("Report a problem", (_, _) => MainForm.Open(IssueUrl(f.Studio))));
        row.Controls.Add(Ui.Secondary("Open log folder", (_, _) => MainForm.Open(AppPaths.DataDir)));
        Controls.Add(row);

        Controls.Add(Ui.Label("About", Ui.H2));
        Controls.Add(Ui.Label($"MCD2 Skin Studio {Program.Version}. Free and open source (MIT). Fan-made: not affiliated with, endorsed by or connected to Mojang Studios or Microsoft. " +
                              "Minecraft is a trademark of Mojang Synergies AB. No game files are included; everything is read from your own copy of the game. " +
                              "Custom skins are only visible on your PC.", Ui.Small, Ui.Muted, w));
        Controls.Add(Ui.Link(RepoUrl, () => MainForm.Open(RepoUrl)));

        Controls.Add(Ui.Secondary("‹  Back", (_, _) => f.ShowHome()));

        void Section(string title, string text)
        {
            Controls.Add(Ui.Label(title, Ui.H2));
            Controls.Add(Ui.Label(text, null, null, w));
            Controls[^1].Margin = new Padding(0, 0, 0, 14);
        }
    }

    static string IssueUrl(Studio s)
    {
        string edition = s.Game?.EditionName ?? "not found", fp = "unknown", state = "unknown";
        try
        {
            if (s.Game != null)
            {
                fp = Fingerprint.Of(s.Game).ToString();
                state = s.Check().Install.ToString();
            }
        }
        catch (IOException) { }
        var body = $"**What happened?**\n\n\n**Which hero?**\n\n\n---\nApp version: {Program.Version}\nEdition: {edition}\nGame fingerprint: {fp}\nInstall state: {state}\nKey known: {(s.Key != null ? "yes" : "no")}\nSkins: {string.Join(", ", s.State.Skins.Select(x => x.Key))}\n";
        return $"{RepoUrl}/issues/new?title={Uri.EscapeDataString("Skin didn't work")}&body={Uri.EscapeDataString(body)}";
    }
}
