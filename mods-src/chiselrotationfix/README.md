# Chisel Rotation Fix

On the ModDB: <https://mods.vintagestory.at/chiselrotationfix>

A server-side code mod that works around
[VintageStory-Issues#9495](https://github.com/anegostudios/VintageStory-Issues/issues/9495):
chiseled blocks in a rotated structure can be made of the wrong block.

## The bug

A schematic stores blocks by its author's ids, with `BlockCodes` mapping each id to a code, and
a chiseled block stores its `materials` the same way. Unrotated placement maps them to the
world's ids once, in `BlockEntityMicroBlock.OnLoadCollectibleMappings`.

Worldgen's 90/180/270° copies, and WorldEdit's rotated imports, are first rotated with
`BlockSchematic.TransformWhilePacked`. That hands each chiseled block to
`BlockEntityMicroBlock.OnTransformed`, which already maps `materials` to world ids, and stores
them. Placement then maps them again, as if they were schematic ids. A world id that is also a
key of `BlockCodes`, for a different block, turns into that block. The closing
`BlockSchematic.Pack` writes the right code for the world id of every block in the grid, so the
second mapping only goes wrong for a material that isn't also a grid block.

Which materials collide depends on the world's block ids, so the mod set decides which ruins
break. In this pack, as of 1.22.7, it corrupts about 7,000 chiseled-block materials across
BetterRuins' femursnapper ruins, underground ruins and story structures. Aged granite becomes
`overlay-damagedstone` and renders see-through. Elsewhere logs turn into flowers, and other
materials into glowworms, altars and the like. With only BetterRuins installed it hits a few story structures and
`mediumruins-femursnapper-o2-1009`. Rotation 0 never goes through `OnTransformed`.

## The fix

A Harmony prefix and postfix on `BlockEntityMicroBlock.OnTransformed`. The prefix notes which
`materials` the game is about to resolve. The postfix swaps each resulting world id for a
`BlockCodes` key that names the same block, so placement maps it once, to the right block.

The key is always at least the world's block count, adding an entry when needed. `Pack` writes
keys for the world ids of the grid's blocks, which are all below that count, so it can't
overwrite them. Reusing a low key would let it.

The rotation itself (`GetRotatedBlockCode`, the voxel cuboids) is still the game's. Entries the
game leaves alone (an id missing from `BlockCodes`, or a code that isn't registered) are left
alone too, and so is WorldEdit's mirror, which calls `OnTransformed` with an empty mapping. If
the method is gone, the mod logs a warning and patches nothing.

Worldgen and WorldEdit run on the server, so clients need nothing (`"side": "Server"`,
`"requiredOnClient": false`). The patch only changes structures placed after it is installed;
`/chiselfix` repairs those generated before.

## Repairing existing structures

> **Back up your world before running the repair. Every time.** It rewrites blocks in the save
> and there is no undo. Quit the game (or stop the server), copy the world's `.vcdbs` file from
> `Saves/` somewhere safe, then start it again. The command's name,
> `i_backed_up_my_world_first_repair`, is there to make you stop and do this.

```
/chiselfix check                             [radius] [position]
/chiselfix i_backed_up_my_world_first_repair [radius] [position]
```

Both need `controlserver`, as `/wgen` does, and look at the structures within `radius` blocks
(default 256, horizontally) of `position`, or of the player running it. `check` reports what the
repair would change and changes nothing: run it first. The command's help, and every `check`
that finds something to repair, repeat the warning above. Each structure gets a line (the schematic and
rotation it found, how many chiseled blocks were repaired or left alone, or why it was skipped)
and the server log gets the same report. Nothing runs on its own, and nothing but the repaired
blocks' materials is written to the save.

A world's block ids never change, so the bug's damage can be worked out again:

- **Which structures.** Each map region lists the structures worldgen placed in it, as
  `domain:file.json/structurecode` and a bounding box, but not the rotation. The command takes
  the schematics of that file from that GenStructures structure, rotates each 0/90/180/270° as
  worldgen does, and keeps those the size of the box. It compares each with the world, sampling
  the schematic's ordinary blocks; a block counts if it is there, or is a rock variant of it
  (air, meta blocks, liquids and soil that block layers replace aren't compared). The best
  reading must match at least 60%, or the structure is skipped as unmatched. Every reading within
  10 points of it is kept: a ruin that looks much the same turned around (the wayshrine ruin,
  `mediumruins-femursnapper-o5-1005`, matches 94% at the three wrong rotations) can't be told
  apart by its blocks. Rotation 0 is never affected.
- **What the bug placed, and what it should have.** The schematic is rotated twice, with the
  patch and with it skipped (on that thread only), and each chiseled block's materials are mapped
  as that structure's placement maps them: through the rotated `BlockCodes` with granite turned
  into the placement's rock, and for surface structures the rock replaced once more by
  `OnPlacementBySchematic`. Surface placement took the rock at the schematic's center, which the
  map chunk still records; underground placement sampled a stone block, so the rock is the one
  the structure's own blocks were turned into.
- **What changes.** A chiseled block is rewritten only if its stored materials are exactly what
  the bug placed by one of the kept readings, and every reading that says so agrees on what it
  should be (and none says it was placed right). Stone usually comes out the same whichever way a
  ruin is turned; a block the readings disagree on, such as a log's direction, is left alone and
  counted. Anything else (a player chiseled it, another structure overlaps it, the schematic
  changed since) is left alone and counted too. Only `materials` changes: the cuboids, decor and name
  stay, and the block is marked dirty so clients redraw it. A material that became, or stopped
  being, the block-layer meta block is reported, not repaired, since placement reworked its
  cuboids.

Limits: only loaded chunks are touched. A structure not wholly loaded is skipped and reported, so
go near it and run the command again. Only GenStructures' structures can be identified: story
structures, villages, and anything placed by `/wgen structures spawn` or WorldEdit (which records
nothing) are not repaired. If the patch isn't applied, the command refuses to run.

## Tests

`tests/PackTests/ChiselRotationFixScenarios.cs` (Atlas):

- a one-block schematic built so that its material's world id is also a key naming
  `overlay-damagedstone`, rotated each way
- every BetterRuins schematic, rotated each way: every chiseled-block material it places is a
  material of the unrotated schematic, turned the same way
- `/chiselfix`, on a BetterRuins underground ruin and a surface ruin placed at 90° as the
  unpatched game places them (registered in the map region) next to a copy placed with the
  patch: the repair makes their chiseled blocks the same, `check` changes nothing, and a block
  whose materials were changed since is left alone
- the same for the wayshrine ruin at 270°, which its blocks can't tell from its other rotations

Without the mod, the first two fail; the second lists the corrupted materials. Which materials
collide depends on how the pack numbers its blocks, which changes whenever a mod is added or
updated, so the `/chiselfix` scenarios force the collision: they add a `BlockCodes` key at each
material's world id, naming `overlay-damagedstone`, and remove it afterwards. The Atlas world is
superflat, so they also load GenStructures' structures themselves. The test project loads this
directory's build as a mod, and leaves out a pinned copy from the ModDB
(`chiselrotationfix_*.zip` in `build/mods`).

## Releasing

The same as `mods-src/allowedvariantsfix/README.md`: bump the version in `modinfo.json` and
`ChiselRotationFix.csproj`, merge, tag `chiselrotationfix-v<version>` on main, upload the zip
from the GitHub Release to the ModDB, then pin it in `pack/pack.toml` (`side = "server"`) and run
`packtool lock`.

## Retiring it

When #9495 is fixed, the first two scenarios pass without the mod: remove the `<AtlasMod>`
reference from `tests/PackTests/PackTests.csproj` and run them. If they pass, drop the pin, this
directory and the scenarios, and mark the mod obsolete on the ModDB. `/chiselfix` replays the
game's own `OnTransformed`, so once that is fixed it finds nothing to repair: worlds need
repairing before then, with this version.
