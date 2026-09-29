# Recipe browser icons

The recipe browser shows an icon for each item. A person renders the icons in the game
client, and `tools/icons.py` imports them into `icons/`. CI does not render icons. It only
warns about items that have none, and the site shows a placeholder for those.

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

## Export icons from the game

You need the game client at the pack's version (`game_version` in `pack/pack.toml`) with all
of the pack's mods. A separate data folder keeps your normal game setup unchanged:

```sh
python3 tools/packtool.py fetch                       # locked mods, verified, into build/mods
mkdir -p ~/vs-icons/Mods
cp -r build/mods/* ~/vs-icons/Mods/
cd /path/to/vintagestory                              # the client install folder, with run.sh
./run.sh --dataPath ~/vs-icons --openWorld seraph-icons --playStyle creativebuilding
```

`--openWorld` opens the world named `seraph-icons`, and creates it the first time. It is a
creative world, so you can take any item from the creative inventory. Log in if the game asks
you to.

You can also open the release's `.cairn.json` in the Cairn launcher and start the pack from
there. Cairn keeps the game in `~/.cairn/games/<game version>/` and the pack's mods and data
in `~/.cairn/packs/<pack>/`. Create a creative world. The icons are then written to
`~/.cairn/games/<game version>/icons/`, not to the pack folder. The recipe export mod does not need to
be installed either way.

Before an export, delete any `icons/` folder left in the client install folder. The game
overwrites files with the same name but does not delete old ones.

In game, open chat (`T`) and run:

```
.blockitempngexport all 128
```

- The arguments are `[inv|all] [size] [domain]`. `all` renders every block and item. `inv`
  renders only those that have a creative inventory tab. `size` is the width and height of
  the render in pixels. The default is 100. The tool scales the icons down to 64, so a
  128 px render gives smoother edges. `domain` renders one mod only (see below). To give a
  size you must also give `all` or `inv`.
- The game renders everything in one frame, so it stops responding until the export is
  finished. With the whole pack this can take several minutes.
- When it is done, chat shows `Ok, exported to <path>`. That path is the `icons/` folder
  inside the client install folder: the folder that contains `Vintagestory.dll` and
  `run.sh`. The client sets its working directory to that folder at startup and writes there,
  not to the data folder. The folder must be writable.

The files are named like this:

| file | code |
|---|---|
| `icons/block/<path>.png` | block `<domain>:<path>` |
| `icons/item/<path>.png` | item `<domain>:<path>`. A `/` in the path becomes `-`. |
| `icons/lod2/...` | written by `.exportlod2`. Not used here. |

The domain is not in the name. `game:foo` and `somemod:foo` both write `foo.png`, and the
one rendered last wins. The tool never guesses which domain a file belongs to:

- `--items recipes.json` looks each name up in a recipe export. Use the one for the pack
  version you exported, which is the release asset `seraphhorizons_<version>_recipes.json`
  or the `recipe-export` artifact of a CI run. A name that matches codes in two domains is
  listed as ambiguous and skipped. So is a name that matches no code.
- `--domain <modid>` assigns every file to one domain. Use it with an export of that domain
  only: move the `icons/` folder away, run `.blockitempngexport all 128 <modid>`, and import
  that folder with `--domain <modid>`. This is how you fill in the names that `--items`
  reports as ambiguous.

## Import

From the repository root, with Git LFS set up and the images pulled:

```sh
uv run tools/icons.py import /path/to/vintagestory/icons --items seraphhorizons_0.1.0_recipes.json
```

`uv run` installs Pillow for the script. `python3 tools/icons.py import ...` also works if
Pillow is installed. The tool scales each image to fit the index's size (64), keeping its
aspect ratio and transparency, and centres it. It writes a PNG with no metadata, stores it
under its hash and updates `icons/index.json`. It prints this:

```
imported 5210, unchanged 0, distinct images 4630 (4630 new file(s)), unmapped 42
  unmapped item/widget.png: ambiguous: examplemod:widget, game:widget
```

`imported` counts codes that are new or have a new image. `unchanged` counts codes that
already had the same image. Running the tool again on the same folder changes nothing. When a
code gets a new image, the old file stays until you prune it.

## Add single icons

Some items render wrongly or blank in the bulk export because their look depends on stack
attributes. Examples are a filled container and a block whose type is stored in attributes.
To render one of these, hold the stack in the selected hotbar slot and run:

```
.exponepng hand 128
```

For an item or block without attributes, you do not have to hold it:
`.exponepng code item game:metalplate-copper 128` or
`.exponepng code block game:ladder-wood-north 128`.

These commands also write to `icons/` in the client install folder, but the name is the full
code path, with `/` kept. `.exponepng hand` writes into `icons/block/` even for items, unless
the last `.exponepng code` was for an item. The import handles both. Each export has the same
name as the bulk icon for the same code and overwrites it. So import the bulk export first,
then move `icons/` away and do the single exports in an empty folder. Import that folder the
same way (`--items` or `--domain`). The new image replaces the code's mapping. An icon covers
a code, not a particular set of attributes.

## Check

```sh
python3 tools/icons.py verify                               # every entry has a file, every file matches its hash
python3 tools/icons.py drift --export seraphhorizons_0.1.0_recipes.json   # items with no icon, by mod
python3 tools/icons.py prune                                # lists images nothing references
python3 tools/icons.py prune --delete                       # deletes them
```

Open a few of the new PNGs to check them. Icons that are blank or cut off usually need a
single export.

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
