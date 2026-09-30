# Minecraft Dungeons II Skin Editor

[![Latest release](https://img.shields.io/github/v/release/maartenverw06/Minecraft-Dungeons-2-skin-editor?label=download)](https://github.com/maartenverw06/Minecraft-Dungeons-2-skin-editor/releases/latest)
![Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d4)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Put your own look on the heroes of **Minecraft Dungeons II**. Pick a hero, paint it square by square in
the built-in editor, press **Install**, and play. No paint program, no modding tools, no setup.

The app is called **MCD2 Skin Studio**.

- **One `.exe`, nothing to install.** It finds the game by itself.
- **Safe for your game.** The game's own files are never changed. Skins go in the game's `~mods`
  folder, and **Remove all custom skins** deletes only the files this app made.
- **No game files included.** Everything is read from your own copy of the game.
- **Only you see it.** Custom skins show up on your PC; other co-op players see the normal heroes.

## Download

**[Download the latest `MCD2SkinStudio.exe`](https://github.com/maartenverw06/Minecraft-Dungeons-2-skin-editor/releases/latest)**
(about 50 MB, Windows 10 or 11, 64-bit).

| Game version | Status |
|---|---|
| Xbox app / Microsoft Store / Minecraft Launcher | Works (tested) |
| Steam | Built to work, not tested yet. [Let us know](../../issues) how it goes. |
| Mac, Linux, consoles | Not supported, see the [FAQ](#faq) |

## How to use

1. Start `MCD2SkinStudio.exe`.
2. Click **Make a new skin** and pick a hero.
3. Paint. Choose a body part with the tabs at the top, pick a colour and click or drag over the squares.
   The live preview shows your hero from the front and back while you paint.
4. Press **Install**. Windows may ask for permission once, because the game lives in a protected folder.
5. Start the game and pick the hero in the Locker.

After a game update, open the app and press **Repair** when it asks.

### "Windows protected your PC"

The app isn't code-signed yet, so Windows SmartScreen doesn't know it. Click **More info → Run anyway**.
Each release has a `.sha256` file so you can check your download:

```powershell
Get-FileHash .\MCD2SkinStudio.exe -Algorithm SHA256
```

## The editor

Every square is one pixel of the hero. Each body part is unfolded like a paper model: the big middle
square is the front, top and bottom sit above and below it, and the sides and back are next to it, all
as seen from the outside. Right and left are the hero's own.

| Tool | What it does |
|---|---|
| Brush (B) | Click or drag to paint squares |
| Fill (F) | Colours every touching square of the same colour |
| Eraser (E) | Empties hat and face-animation squares; on other parts it puts the game's colour back, so the hero never gets holes |

- **Colours:** 40 presets, a colour wheel with a light/dark slider, or type a hex code.
- **Eyedropper:** right-click a square to pick up its colour.
- **Find a square:** point at a square and it's marked on the live preview; click the preview to jump to
  that body part.
- **Undo / redo:** Ctrl+Z / Ctrl+Y (one drag is one step).

### Face and portrait

The front of the head has no eyes or mouth: the game draws and animates them from a few pixels,
shown under **Face & portrait**:

| Squares | What the game uses them for |
|---|---|
| Top line (2 squares) | Pupils: the eye on your left, the eye on your right |
| Middle line (2 squares) | Eye whites: outer and inner pixel of each eye (mirrored); the pupil covers the inner one |
| Bottom line (2 squares) | Mouth: left and right pixel, in the middle of the face |
| Block on the left (4 × 2) | Eyebrow shape, drawn above both eyes (mirrored for the other eye) |

The **portrait** (8 × 8) is the small face picture in the Locker; paint a whole face there.

## FAQ

**Does it work on a Mac?**
No. The app is a Windows program, and it edits the files of the Windows PC versions of the game
(Xbox app / Microsoft Store and Steam).

**Does it work on Xbox, PlayStation or Switch?**
No. Custom skins need access to the game's files, which consoles don't allow.

**Can I get banned?**
Skins are only visible on your own PC and don't change gameplay. Still, this is an unofficial mod: use
it at your own risk.

**How do I get rid of everything?**
In the app, click **Remove all custom skins**. Then delete `MCD2SkinStudio.exe` and, if you like, the
folders `%APPDATA%\MCD2SkinStudio` (your saved skins and settings) and `Documents\MCD2 Skin Studio`.

**Something didn't work.**
Open **Help** in the app: it can run Repair, and it opens a GitHub issue with the details already filled
in. The app's log is at `%APPDATA%\MCD2SkinStudio\log.txt`.

## For developers

Needs the .NET 10 SDK on Windows.

```powershell
dotnet test tests/Mcd2SkinStudio.Tests        # game tests run only when the game is installed
dotnet publish src/Mcd2SkinStudio.App -c Release -o publish
```

| Project | What it does |
|---|---|
| `src/Mcd2SkinStudio.Core` | Reads the game's IoStore/pak archives (read-only), exports textures, builds and installs the mod container. No UI. |
| `src/Mcd2SkinStudio.App` | The Windows app (WinForms). |
| `src/Mcd2SkinStudio.Cli` | `mcd2skin` command line for testing and maintenance (`info`, `list`, `export`, `install`, `repair`, …). |
| `tests/Mcd2SkinStudio.Tests` | xunit tests. |
| `tools/UiShots` | Dev tool: renders every app page to a PNG. |
| `vendor/OodleSharp` | Pure C# Oodle decoder (MIT), see its README. |

Pushing a tag `v*` builds the exe on GitHub Actions and publishes it with its SHA-256 as a release.

**How skins are stored.** The raw texture is Minecraft's 64 × 64 slim-arm layout, except that the legs
are mirrored with their left and right regions swapped, and the head and hat backs are mirrored. The
jacket, sleeve and pants layers aren't used by the game. The CLI can still export a printable design
sheet and install a painted PNG.

**After a game update.** The game's archive key can change with an update. When users report "the new
key isn't known yet", add an entry to [`keys.json`](keys.json) (with the new utoc size and container id)
and push. Users then press **Repair**; the app downloads `keys.json` from this repo.

## Disclaimer

Fan-made project, **not affiliated with, endorsed by or connected to Mojang Studios or Microsoft**.
*Minecraft* and *Minecraft Dungeons* are trademarks of Mojang Synergies AB. Use at your own risk.

## License

MIT, see [LICENSE](LICENSE). `vendor/OodleSharp` is MIT, © its authors.
