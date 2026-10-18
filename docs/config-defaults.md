# Config defaults follow the pack

Vintage Story mods keep their settings in `ModConfig/` (JSON through the game's `LoadModConfig`,
YAML through ConfigKit, and a few of their own). A mod writes its file once, with its defaults,
and from then on keeps every value already in it: only a key the file lacks gets the current
default. So when a mod (ours or anyone's) changes a default, every existing install keeps the old
one forever. The case that started this: `TrunkEntitiesSettings.CarrySpeedAtMaxLogs` in
`seraphhorizons.json` shipped at 0.02, then 0.2, 0.5 and 0.1, and an install made at 0.02 stayed
there; `BuckingSawmillSettings.BladeWearPerStoredLog` went from 0.25 to 1 the same way.

The pack's own mod fixes that for every mod in the pack (switch `FollowPackDefaults`, on by
default). Code: `mods-src/seraphhorizons/ConfigDefaults/` (`Core/` is game-independent and unit
tested in `tests/ConfigDefaults/`, `Game/ConfigDefaultsSystem.cs` runs it), `tools/configdefaults.py`
(the snapshots), `pack/config-defaults.toml` (the hand-written part) and `pack/config/ModConfig/`
(the values the pack sets itself).

## The model

The files stay whole, as their mods write them. The pack records, for each pack version, what a
fresh install of that version has in `ModConfig` (the **snapshot**), and each install records, for
each file, the defaults it was last brought to: that file as the snapshot of the build that last
ran had it (its **baseline**, under `ModConfig/seraphhorizons-configdefaults/`). At start, for each
setting that is in both a file's baseline and the current snapshot's file:

- the file still has the baseline's default, and the current default differs: the file gets the
  current default;
- anything else (a value someone set, or a default that did not change): kept.

A value equal to the default counts as not chosen, and follows the default. Values compare as
values: `0.10` is `0.1`, `True` is `true` in YAML, objects key by key in any order, arrays item by
item. Nested objects (and dictionaries, such as `MachineOilSettings.OilLumps`) are walked key by
key; an array is one value. Only the changed value's text is replaced: comments, layout, key order
and the rest of the file stay as they are.

Left alone:

- a setting only one of the two versions has (a new one is the mod's to add; a dropped one the
  mod's to drop);
- a setting the file lacks (the mod fills it in with its current default anyway);
- a top-level `version` key: a mod's own marker of its file's shape. ConfigKit starts the file
  over when it differs, and the pack does not second-guess that;
- a file not in the current snapshot: client-only state (Tidy Variants' open groups, the pack
  version check's dismissed findings), a client-only mod's config (the snapshot comes from a
  server), a mod that is not in the pack. `pack/config-defaults.toml`'s `[[ignore]]` leaves out
  anything else a fresh server writes that is not a setting to follow.

This compares defaults, not version numbers, so it works within a pack version as across one: the
snapshot of a version not yet released keeps changing on main, and every `next` build between two
releases has the same version, so a default changed on main reaches an install following `next` on
its next start. The version only names the defaults in the log.

The first start with this system (no record yet, which is every install made before it, and every
fresh install) records the current pack version and the current snapshot's files as the baselines,
and changes nothing: there is no baseline to tell a chosen value from a stale default.

**A record without baselines.** Builds before the baselines were kept recorded only the pack
version (`{"PackVersion": "0.1.0", "Files": {}}`). A file with no baseline:

- whose recorded version is another one: is brought up from that version's shipped snapshot, as
  those builds did (released snapshots are frozen, so that is what the install had, unless it
  followed `next`), and then gets its baseline;
- whose recorded version is the running one: gets the current snapshot's file as its baseline and
  is not changed. The defaults it had are whichever build of that version wrote the record, which
  nothing says: guessing (say, from older revisions of the snapshot) would move values a player
  set. One log line lists these files. From then on, defaults changed within the version follow. A
  stale default from before (a tester's `OreProcessing: false`, flipped to true while 0.1.0 was on
  `next`) stays until someone sets it, or deletes the setting (or the file) so its mod writes its
  current default.

## Snapshots

`mods-src/seraphhorizons/assets/seraphhorizons/config/configdefaults/<pack version>/`, shipped in
the mod's zip, so the mod carries the whole history:

- `files/<name>`: every file a fresh server of the pack writes into `ModConfig`, byte for byte
  (`.gitattributes` keeps git from touching their line endings; Yang's Transport Tycoon writes
  CRLF);
- `index.json`: the pack and game version, every mod and version that wrote them (the lock's, and
  the pack's own mod at its version), each file's format and owners (below), the pack's own values
  and the renames.

The version is `pack/pack.toml`'s `[pack] version`, not any mod's. Snapshots start at 0.1.0, the
first version recorded; there is no backfill of earlier ones, and an install whose baseline has no
snapshot (none can, today) is left as it is with a warning.

**Making one.** `VINTAGE_STORY=<server> python3 tools/packtool.py smoke --config-defaults write`
(after `packtool fetch`) boots a dedicated server with every locked mod and this tree's build of
the pack's own mod on an empty data folder, as CI's smoke test does, with
`SERAPH_CONFIG_DEFAULTS=capture`: in capture mode the pack's mod changes and writes nothing in
`ModConfig`, so what is there afterwards is exactly what the mods write. `tools/configdefaults.py`
then writes the current version's snapshot from it. Two runs with different seeds write the same
bytes.

**CI.** The smoke job runs `--config-defaults check`, the same capture, and fails when the
committed snapshot for the current version differs, with a diff and the command above. So any
change that moves a default (a mod bump in the lock, a default changed in this repo's mods, a pack
value) has to come with the regenerated snapshot. The tools job's `test_configdefaults.py` checks
offline that the committed snapshot records the lock, the pack values and the renames, so a lock
change without a regenerated snapshot fails there too. The C# unit tests read every shipped file
with the mod's own readers and apply every pack value.

**Released versions are frozen.** Once `v<version>` is tagged, that version's snapshot is what
installs of the release have, and changing it would mislead every later upgrade from it. The pack's
release flow bumps the version at release time, so between a release and the next bump main keeps
the released version: then `check` reports a difference without failing ("waits for the next
version's") and `write` refuses. The bump that starts the next release writes the new version's
snapshot (`--config-defaults write`), which CI then requires. The `next` build between two
releases carries the version of the last bump, and its snapshot changes with main until that
version is released; an install that follows `next` follows those changes too, since its baselines
are what it last ran, not the version's final snapshot. A released install upgrading has baselines
equal to its frozen snapshot.

## Formats

What a fresh server of the pack writes (0.1.0: 94 files):

- **JSON**, read as the game's Newtonsoft does: comments, unquoted keys, single quotes, trailing
  commas (`LenientJson.cs`). Most files, in `ModConfig/` and in mod folders under it
  (`ImmersiveWoodworking/`, `WalkingSticks/`, `yangtransport/`, ...). Yang's Transport Tycoon's
  `station_names.json` is the one with comments, unquoted keys and CRLF.
- **JSON without an extension**: Metal Pots' `metalpotconfig`. Any file not ending in `.yaml` or
  `.yml` is read as JSON.
- **YAML** (`SimpleYaml.cs`): the files ConfigKit writes for content mods' declared settings,
  `betterruins.yaml`, `cartwrightscaravan.yaml`, `em.yaml`, `landformoverhaul.yaml`,
  `tailorsdelight.yaml`, `temporalsymphony.yaml` and `wool.yaml`: `key: scalar` lines (keys may have
  spaces) between comment lines. Nested mappings are read too; a sequence, flow collection, block
  scalar, anchor or tag is compared by its text and never changed. Only the scalar is rewritten;
  ConfigKit's `# (default: ...)` comment line under it stays as it was until ConfigKit next writes
  the file.

A file a reader cannot parse is left as it is, with a warning.

## Owners: which mods were checked

A file is only changed when every mod that decides what it holds (its **owners**) is loaded on
that side at the version the current snapshot has. `tools/configdefaults.py` works the owners out
when it writes the snapshot, in this order: `pack/config-defaults.toml`'s `[owners]`; a ConfigKit
`.yaml` named after a mod is that mod's and ConfigKit's; the mods whose code (any `.dll` in the
staged mod) has the file's name in it as a .NET string literal (its path, its own name, or the
folder it is in), narrowed to the mod the name points to when that is one of them; the mod the name
points to (`CarryOnConfig.json` is `carryon`, `stonequarry.json` is `stonequarryrepckfipil`). A
file none of these places fails the snapshot, to be settled in `[owners]` (exlib's shared
`ex_recipes.json` and `ex_values.json` are there, owned by exlib, ppex and smex).

## The running pack version and the installed mods

- **The pack version** is the one in the lock the mod carries inside its DLL (`pack/lock.json`,
  embedded at build time, as the pack version check reads it), which is the version of the tree it
  was built from. Its snapshot is looked up in the mod's own folder, then in
  `<data folder>/ConfigDefaults/<version>/` (snapshots a build does not carry: tests, and a dev
  build that should reconcile from a version it lacks). A build with no snapshot for its version
  (a dev build whose version was bumped without regenerating, an older build without this system's
  data) changes nothing and says so in the log. Another set of mods running this build (a test pack
  such as Fallenstar, a server with mods updated by hand) is checked file by file, below.
- **The installed mods** are the game's loaded mods on that side (`api.ModLoader.Mods`), compared
  with the current snapshot's `mods` file by file through the owners. A file whose owner is
  another version or not loaded is left at its old baseline (its version recorded per file in the
  state), and brought up from there on a later start once the mods match; one warning line lists
  them. Mods the pack does not have, and the game version, do not matter: only a file's owners
  do.

## Pack values

The values the pack sets instead of a mod's own default are repo data in `pack/config/ModConfig/`:
a `.yaml` file of `key: scalar` lines for a ConfigKit file, a `.json` object for a JSON one (each
leaf is a value), named after the file they go into. Today that is BetterRuins' ruin spacing and
vanilla structure chance (`betterruins.yaml`), and Primitive Survival's Living Dead spawn multiplier
at 0, which turns that creature's spawning off (`primitivesurvival5.json`), and Candela's burning
down, weather snuffing and torch time off, which leaves lights as in vanilla (`candela.json`). They go into each
snapshot's `index.json` (`packValues`), and a version's defaults are its snapshot's files with its
pack values set: so changing or adding a value moves untouched installs like any other default
change, and a value someone set by hand stays.

A fresh install gets them because, on the server, a file the pack sets values in that does not
exist yet is written whole, as the snapshot has it with the values set, when its owners match. The
server's StartPre runs before ConfigKit reads its YAML (in `AssetsLoaded`), so BetterRuins starts
with the pack's values. This replaces the Cairn pack file's `modConfig`, which only Cairn installs
got: a server from the server zip, or a hand install, never had them. `packtool assemble` no longer
writes `modConfig`. An install made by Cairn before this already has the values, and its first
start records the version and leaves them.

To change one: edit the file under `pack/config/ModConfig/`, regenerate the snapshot, commit. To
add one for a file nobody set before: the same; `test_configdefaults.py` checks a YAML key is a
setting the mod declares and in range, and the snapshot build that the file and setting exist.

The pack's own mod sets some other mods' values in code instead (unified woodworking's Immersive
Woodworking settings, the rarer battle towers' rates): those it needs wherever it runs, whatever a
player's file says, which a pack value is not.

## Renames

`pack/config-defaults.toml`'s `[[rename]]`: in `file`, the key at `from` (the full key path) is now
`to`, in the same object. Before comparing, a file with the old key and not the new one gets the
key renamed, value kept, and the baseline's defaults get the same rename, so an untouched value
follows the new default under its new name and a chosen one keeps its value. Each needs a `why`.
They go into the snapshot's `index.json`; the current version's list is the one used.

None is recorded. Seraph Horizons' own `TrunkEntitiesSettings.CarrySpeedAtFourLogs` became
`CarrySpeedAtOneLog` before the first snapshot, so no install has a baseline with the old key, and
the setting changed meaning (four logs to one) and default (0.8 to 1) with it: carrying a value
across would not be anyone's intent either. The example in the file is that one, commented out. Nor is
`CrucibleFurnaceSettings.ChimneyBlocks`, split into `ChimneyShaftBlocks` and `ChimneyTopBlocks` before
0.1.0 was released: its list held the clay brick chimney, now the chimney's top and not a shaft block.

## Where it runs

`ConfigDefaultsSystem` (in the pack's own mod), in `StartPre`, with `ExecuteOrder` at
`double.MinValue`: the game runs every mod system's `StartPre` in `ExecuteOrder`, so it is the first
`StartPre` of all, before any mod reads its config in its own `StartPre`, `Start` or later (ConfigKit
reads its YAML in `AssetsLoaded`; Newtonsoft configs are mostly read in `Start` or
`StartServerSide`). The asset manager has no mod assets yet in `StartPre`, so the snapshot is read
from the mod's unpacked folder on disk. Seraph Horizons' own `seraphhorizons.json` goes through it
like any other file, before `SeraphHorizonsSystem` loads it in `Start`; the switch itself is read
from the raw file first.

Each side runs for its own `ModConfig`: a dedicated server for its files; a client for the files
on the client (with the client's loaded mods; a server-only mod's file is not there). Only the
server writes a missing file. In singleplayer the two share one folder: the server runs first and
the client finds nothing left to do.

Capture runs log, and write to `configdefaults-capture.json` in the data folder, the files that
were already in `ModConfig` at the first `StartPre`: a mod that wrote its config in its mod system's
constructor or `ShouldLoad`. At 0.1.0 there are none; `--config-defaults` prints them if one
appears.

## The record, the log and the notice

The state is the record, `ModConfig/seraphhorizons-configdefaults.json`:

```json
{
  "PackVersion": "0.2.0",
  "Files": { "ex_values.json": "0.1.0" },
  "Notice": { "FromVersion": "0.1.0", "ToVersion": "0.2.0", "Changes": ["..."], "NotifiedPlayers": ["<uid>"] }
}
```

and the baselines, `ModConfig/seraphhorizons-configdefaults/<file>` (the same names and folders as
in `ModConfig`, about as large as the snapshot: 0.8 MB at 0.1.0), each the file as the snapshot of
the build that last brought it up had it, with the pack's values set. A baseline is written only
when it changes. `PackVersion` is the version of the build that last ran, `Files` the files left
behind at older defaults because a mod that writes them did not match (usually empty), by the
version they were last brought to; both name the defaults in the log, and, for a file with no
baseline, the version's snapshot to start from (above). Both are left out of the snapshot
(`pack/config-defaults.toml`'s `[[ignore]]`; an ignore ending in `/` is a folder). Deleting a
baseline makes its file a file with no baseline (above). Each
changed setting is a log line (`[seraphhorizons] Config defaults: <file>: <setting> <old> -> <new>
(the default was <old> in pack <a> and is <new> in <b>[, set by the pack]; this file still had the
old default)`, or, within a version, `the default was <old> in an earlier build of pack <a> and is
<new> in this one`), and a summary line follows. On a server, each player with the `controlserver`
privilege who joins afterwards gets the list in chat once (`NotifiedPlayers`), until the next
change replaces it. A client only logs.

## Turning it off

`"FollowPackDefaults": false` in `ModConfig/seraphhorizons.json`, each side for its own folder:
nothing is changed or written, not even the record. Turning it back on later reconciles from the
recorded baselines (or, with none, records and changes nothing).

## Residual risks

- A mod that reads its config before any `StartPre` (in its mod system's constructor or
  `ShouldLoad`) reads it before it is brought up to date; the change lands on disk and takes effect
  on the next start. Capture runs list any such file; none at 0.1.0. A mod that also uses
  `double.MinValue` and loads before this one would tie, and the game keeps load order on a tie.
- A mod that keeps settings somewhere other than `ModConfig` is not covered.
- Owners found by name can be wrong for a file no code names literally; a wrong owner only means
  the file is checked against the wrong mod's version.
- An install recorded by a build that kept only the pack version (no baselines) keeps any stale
  default it had from earlier in that same version (see A record without baselines).
- A file the pack's snapshot no longer has keeps its baseline, unused; if the file comes back, it
  is brought up from that baseline.
- A mod that changes the meaning of a setting without renaming it: an untouched value follows the
  new default, which is what its mod now ships, and a chosen one is kept with its old meaning.
