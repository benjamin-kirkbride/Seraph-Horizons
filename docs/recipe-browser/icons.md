# Recipe browser icons

The recipe browser shows an icon for each item. A person renders the icons in the game
client with a small local mod, and `tools/icons.py` imports them into `icons/`. CI does not
render icons. It only warns about items that have none, and the site shows a placeholder for
those.

```
icons/index.json            { "schemaVersion": 1, "size": 64, "icons": { "game:stick": "<sha256>" } }
icons/<xx>/<sha256>.png     one file per distinct image, xx = first two hex digits of its sha256
```

Each image is stored once, under the sha256 of the stored PNG, and every pack version uses the
same files. The PNGs are in Git LFS. `index.json` is a normal file.

## Set up Git LFS

Do this once on each clone:

```sh
git lfs install --local     # hooks for this clone; `git lfs install` does it for every repository
git lfs pull                # replace the pointer files in icons/ with the images
```

If the hooks are missing, `git push` does not upload the images, and the site deploy cannot
fetch them. `git lfs env` shows whether the hooks are installed.

## Why a mod

The game has its own export, `.blockitempngexport`, but with the whole pack loaded it draws
almost every icon without its texture:

- A full export wrote 57,295 files. About 97% were white shapes: the right outline and face
  shading, three shades of grey, no colour. The only coloured files were blocks the game tints
  itself (leaves, water, berry bushes, sod roofing). `.exponepng hand` with the HUD hidden
  was white too.
- In a client with only two mods, `.exponepng` gave a correct, textured icon. So the export
  works, and something in the pack leaves it a bad state.
- The export draws with the GUI shader in whatever state the renderers before it left. In
  `gui.fsh`, a `noTexture` uniform above 0 draws the vertex colour alone: white, shaded, tint
  colours kept, which is exactly the symptom. The export never resets it. No mod in the pack
  replaces the GUI shader files.
- A mod that resets the GUI shader's uniforms just before the export fixed it: `.exponepng
  hand 100` then wrote a textured icon (2,771 of 2,833 visible pixels coloured), and the mod
  logged that the GUI shader was the active program. So the fault is the shader's uniform
  state, not the program.

Which renderer leaves the state behind is not known yet. The pack's mods were searched for
code that sets `noTexture` without setting it back, and nothing was found that runs on every
frame; `.seraphicons probe` (below) records the shader's state across a frame to find it.

The export also names files without the mod's domain (`icons/block/<path>.png`), so
`game:crate` and `materialneeds:crate` overwrite each other, and it renders about 30,000
block variants the site never shows.

The mod, `seraphiconfix` in `tools/icon-export/`, fixes both. It is a local tool: it is not
in `pack/pack.toml`, no release or Cairn pack includes it (`packtool.py assemble` builds from
`pack/lock.json` alone, and a test checks that), and players never install it. Install it
for an export and remove it afterwards.

## Build the mod

You need the .NET 10 SDK and the game (the client or the dedicated server: the mod uses only
`VintagestoryAPI.dll`) at the pack's version (`game_version` in `pack/pack.toml`):

```sh
VINTAGE_STORY=~/.cairn/games/1.22.7 dotnet build tools/icon-export -c Release
```

This writes `build/seraphiconfix.zip`, with `SeraphIconExport.dll` and `modinfo.json` at the
top level.

## Install it

The game client needs all of the pack's mods and this one. With the Cairn launcher, the pack's
own mods are in `~/.cairn/packs/seraphhorizons-next/Mods/` (Cairn manages that folder; leave
it alone), and `~/.cairn/packs/seraphhorizons-next/data/Mods/` is an extra mod folder the
client also loads, empty otherwise:

```sh
mkdir -p ~/.cairn/packs/seraphhorizons-next/data/Mods
cp build/seraphiconfix.zip ~/.cairn/packs/seraphhorizons-next/data/Mods/
```

Keep one copy only: a newer build replaces the old zip of the same name.

Start the pack from Cairn and open a creative world. The client log
(`~/.cairn/packs/seraphhorizons-next/data/Logs/client-main.log`) then has the line
`[seraphiconfix] loaded: .seraphicons is available`.

Without Cairn, use a separate data folder so your normal game setup stays as it is:

```sh
python3 tools/packtool.py fetch                       # locked mods, verified, into build/mods
mkdir -p ~/vs-icons/Mods
cp -r build/mods/* build/seraphiconfix.zip ~/vs-icons/Mods/
cd /path/to/vintagestory                              # the client install folder, with run.sh
./run.sh --dataPath ~/vs-icons --openWorld seraph-icons --playStyle creativebuilding
```

## Export

Open chat (`T`) and type the commands with the leading dot. First one icon, to check:

```
.seraphicons one game:pickaxe-copper
```

Then everything the site shows, from the recipe export of the pack version you are exporting
(the release asset `seraphhorizons_<version>_recipes.json`, or the `recipe-export` artifact of
a CI run), saved somewhere on the same machine:

```
.seraphicons list /home/me/Downloads/seraphhorizons_0.1.0_recipes.json
```

| command | renders |
|---|---|
| `one <code> [size]` | one item or block, by its full code (`game:pickaxe-copper`). A code that is both an item and a block gives both. |
| `hand [size]` | the stack in the selected hotbar slot, with its attributes (a filled bucket, a block whose type is an attribute). |
| `list <file> [size]` | exactly the codes in the file: a recipe export (the keys of `items`, with their kind), a JSON array of codes, or a text file with one code per line, `#` comments and blank lines allowed. A path with spaces goes in double quotes. |
| `all [size] [domain]` | every registered item and block, or one mod's. A fallback: it renders many blocks the site never shows. |
| `stop` | stops the running export. Running the same command again resumes it. |
| `status` | progress of the running export. |
| `probe` | writes the GUI shader's state at points across one frame to the client log (see below). |

`size` is the width and height in pixels, 16 to 1024, default 128. The import scales icons to
64, so 128 gives smoother edges. Options, anywhere after the subcommand:

- `--force` renders files that already exist. Without it, `list` and `all` skip them, so a
  second run resumes where the last one stopped. `one` and `hand` always overwrite.
- `--out <dir>` writes somewhere else.
- `--reset <uniform|all|none>` is for debugging only: it resets just that GUI shader uniform
  before each draw (default `all`).

The export runs a few icons per frame, so the game keeps responding, and prints progress in
chat every five seconds, with an estimate of the time left. At the end it
prints the totals: written, skipped as existing, failed, fully transparent, and
untextured-looking (no colour and at most 16 shades of grey). If more than a tenth look
untextured it says so with a WARNING: that is the bug above, and those files should not be
imported. It warns after the first 50 icons too, so you can `.seraphicons stop` early.

### Where the files go

Into `seraph-icons/` in the game's data folder, printed in chat when the export starts. With
Cairn that is `~/.cairn/packs/seraphhorizons-next/data/seraph-icons/`.

```
seraph-icons/manifest.json
seraph-icons/<domain>/<item|block>/<path>.png
```

Every file name is the full code: `game:crate` is `game/block/crate.png` and
`materialneeds:crate` is `materialneeds/block/crate.png`, so nothing overwrites anything. A
`/` in the code's path is a subdirectory (`game/block/clutter-art/bottle.png`). Every other
character outside `a-z 0-9 _ -` is written as `%` and its UTF-8 bytes in hex, dots and
capitals included: `tankardsandgoblets:t&g-winebottle-blue` is
`tankardsandgoblets/item/t%26g-winebottle-blue.png`. So the code can be read back from the
path alone, and the name is safe on every file system.

`manifest.json` maps every file to its code and kind, with the check result, and lists the
codes that could not be rendered with the reason (not registered in this game, the render
threw, the game crashed during it). If the game crashes during an export, the next run finds
out which item it was rendering, skips it and records it as failed; `--force` tries it again.

## Import

From the repository root, with Git LFS set up and the images pulled:

```sh
uv run tools/icons.py import ~/.cairn/packs/seraphhorizons-next/data/seraph-icons
```

`uv run` installs Pillow for the script. `python3 tools/icons.py import ...` also works if
Pillow is installed. The tool finds `manifest.json` and takes every code from it; it also
works out each file's path from its code and skips any entry where the two disagree. It
scales each image to fit the index's size (64), keeping its aspect ratio and transparency, and
centres it. It writes a PNG with no metadata, stores it under its hash and updates
`icons/index.json`. It prints something like this:

```
imported 5210, unchanged 0, distinct images 4630 (4630 new file(s)), unmapped 2
  unmapped examplemod/block/marker.png: fully transparent: nothing was drawn
  unmapped game/item/widget.png: the manifest says item game:gadget, whose file would be game/item/gadget.png
```

`imported` counts codes that are new or have a new image. `unchanged` counts codes that
already had the same image. Running the tool again on the same folder changes nothing. When a
code gets a new image, the old file stays until you prune it. Fully transparent images are
never imported.

Before it writes anything, import checks every image. If more than half look untextured, it
imports nothing and stops with an error: 26,000 white shapes would replace real icons. If they
really are right (a mod whose items are plain grey), `--allow-untextured` imports them anyway.
Fewer than half are imported with a warning that lists them.

If a code has both an item and a block icon, pass `--items <recipes.json>`, and the kind the
recipe export gives decides.

## Exports from the game's own command

`import` still reads the folder the game's own `.blockitempngexport` and `.exponepng` write
(`icons/` in the client install folder, next to `Vintagestory.dll`), with the untextured check
as well. Those names lack the domain, so the import needs either `--items recipes.json` to
look each name up (a name that matches codes in two domains is skipped as ambiguous) or
`--domain <modid>` for an export of one domain. With the mod installed these commands draw
textures too, because the mod resets the shader just before them.

## If the export is still white

Send the `[seraphiconfix]` lines of the client log (`client-main.log` in the data folder's
`Logs/`). At the start of every export the mod writes a diagnostics block: the render stage,
the active shader, the framebuffer, the values of the GUI shader's uniforms as it found them
and which of those differ from its defaults, the OpenGL state, the renderers of the Ortho
stage in order with the assembly of each, and the loaded mods.

`.seraphicons probe` writes the shader's state at the start of each render stage and at many
points of the Ortho stage, for one frame. A uniform that changes between two points was set
by a renderer between them, and the renderer list says which renderers those are. That is how
to find the mod that causes the problem, and it is worth reporting upstream.

## How the mod draws, and why there

The game draws icons in the Ortho render stage, where the engine makes the GUI shader active
and sets an orthographic projection; its own export and inventory slots both draw there. The
mod's export is a renderer in that stage, at order 0.49, before the game's own export (0.5),
so its diagnostics see the state other renderers left. For each frame it binds its own
framebuffer and sets scissor, colour and depth masks, depth test, culling and blending the way
the game's export does; for each icon it makes the GUI shader the active program and sets
every uniform the GUI shaders read to a known value (`Core/GuiUniforms.cs`), then calls the
game's own `RenderItemstackToGui` and reads the pixels back. After the frame it restores the
framebuffer, viewport, projection and the active shader. The uniforms are left at the values
the game's own GUI drawing expects.

Drawing from a dialog or HUD element instead would not help: dialogs draw in the same stage,
with the same shader and the same inherited uniforms, just later (order 1.0). What made the
game's export fail is state, not place, so the fix is to set the state, every time.

A few collectibles draw their GUI icon through a mod's own renderer
(`RegisterItemstackRenderer`); the manifest marks those with `customRenderer`, since that code
may set state the reset does not cover. An item that throws while rendering is recorded as
failed and the export goes on.

None of this rendering is covered by automated tests; only trying it in the game shows that
it works. The tests (`dotnet test tools/icon-export/tests`) cover the parts that do not need
the game: file names, list files, the manifest, the frame scheduling and resume, and the
untextured check.

## Remove the mod afterwards

```sh
rm ~/.cairn/packs/seraphhorizons-next/data/Mods/seraphiconfix.zip
```

It changes nothing in worlds or settings, so removing it leaves nothing behind but the
`seraph-icons/` folder, which you can delete once imported.

## Check

```sh
python3 tools/icons.py verify                               # every entry has a file, every file matches its hash
python3 tools/icons.py drift --export seraphhorizons_0.1.0_recipes.json   # items with no icon, by mod
python3 tools/icons.py prune                                # lists images nothing references
python3 tools/icons.py prune --delete                       # deletes them
```

Open a few of the new PNGs to check them.

## Commit

```sh
git add icons/
git lfs status              # the new PNGs should be listed as LFS objects, not as git blobs
git commit -m "Update item icons"
```

Push as usual. The pre-push hook uploads the images to LFS.

## CI

- Pull requests and CI jobs check out without LFS. `icons/` then contains pointer files. `verify`
  and `drift` accept pointers: they count as present, and `verify` checks the pointer's
  sha256 against the file name. Both need only `python3`.
- CI runs `drift` against the export built from the pack. It prints a warning annotation and
  a job summary when items have no icon. It never fails the build for missing icons. It fails
  only if the index is corrupt or refers to a file that is not in the tree.
- Only the site deploy fetches LFS objects. It copies `icons/` to the site.

## Rights and removal

The icons are renders of the base game's assets and of the pack's third-party mods. The site
hosts them and says that it is unofficial and fan-made. It credits the game and every mod,
and gives a contact for removal requests. To remove a mod's icons, delete its entries from
the index and prune the files it no longer references:

```sh
jq '.icons |= with_entries(select(.key | startswith("somemod:") | not))' icons/index.json > index.tmp
mv index.tmp icons/index.json
python3 tools/icons.py prune --delete
git add -A icons/ && git commit -m "Remove somemod's icons"
```

The next deploy removes the images from the site. They stay in the Git history and in LFS
storage. From then on, drift lists the mod's items as missing, and the site shows
placeholders for them.
