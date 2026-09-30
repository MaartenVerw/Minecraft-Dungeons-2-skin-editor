# MCD2 Skin Studio

Put your own look on the heroes of **Minecraft Dungeons II**. Pick a hero, paint over its texture in
Paint, upload it, done. Works with the **Xbox app / Microsoft Store / Minecraft Launcher** version, and
is built to work with the **Steam** version too (not tested on Steam yet).

- One `.exe`, nothing to install.
- The game's own files are never changed. Skins go in the game's `~mods` folder, and
  **Remove all custom skins** deletes only the files this app made.
- No game files are included. Everything is read from your own copy of the game.
- Custom skins are only visible on your PC. Other co-op players see the normal heroes.

## How to use

1. Download `MCD2SkinStudio.exe` from the Releases page and start it. It finds the game by itself.
2. **Make a new skin** → pick a hero → **Open design sheet in Paint**.
3. Paint inside the squares and save as PNG. Don't resize the sheet.
4. Upload the design sheet, check the front/back preview, press **Install**.
5. Start the game and pick the hero in the Locker.

After a game update, open the app and press **Repair** when it asks.

### Windows says "Windows protected your PC"

The exe isn't code-signed yet. Click **More info → Run anyway**. You can check the download against the
`.sha256` file on the release page:

```powershell
Get-FileHash .\MCD2SkinStudio.exe -Algorithm SHA256
```

## The design sheet

Every square on the design sheet is one pixel of the hero. Each body part is unfolded like a paper
model: the big middle square is the front, top and bottom sit above and below it, and the sides and back
are next to it, all as seen from outside. Right and left are the hero's own. Front and back previews on
the sheet show where everything ends up.

**Face animation.** The head's front has no eyes or mouth: the game draws and animates them from a few
pixels in the texture's top-left corner, shown on the sheet as three stacked lines plus a block:

| Sheet squares | Game uses them for |
|---|---|
| Top line (2 squares) | Pupils: the eye on your left, the eye on your right |
| Middle line (2 squares) | Eye whites: outer and inner pixel of each eye (mirrored); the pupil covers the inner one |
| Bottom line (2 squares) | Mouth: left and right pixel, in the middle of the face |
| Block on the left (4 × 2) | Eyebrow shape, drawn above both eyes (mirrored for the other eye) |

The **portrait** (8 × 8) is the small face picture in the Locker; paint a whole face there.

Experienced skin makers can upload the raw 64×64 texture instead. It is Minecraft's slim-arm layout,
except that the legs are mirrored with their left and right regions swapped, and the head and hat backs
are mirrored. The jacket, sleeve and pants layers aren't used by the game.

## Building

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

Pushing a tag `v*` builds the exe on GitHub Actions and attaches it plus its SHA-256 to a release.

### After a game update (maintainer)

The game's archive key can change with an update. When users report "the new key isn't known yet":
start the game, run the key finder from the maintainer tools, add an entry to `keys.json` (with the new
utoc size and container id), and push. Users then press **Repair**; the app fetches `keys.json` from
this repo.

## Disclaimer

Fan-made project, **not affiliated with, endorsed by or connected to Mojang Studios or Microsoft**.
*Minecraft* and *Minecraft Dungeons* are trademarks of Mojang Synergies AB. Use at your own risk.

## License

MIT, see [LICENSE](LICENSE). `vendor/OodleSharp` is MIT, © its authors.
