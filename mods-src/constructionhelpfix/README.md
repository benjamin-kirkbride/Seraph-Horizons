# Construction Help Fix

A code mod that removes the lag spike of looking at an unfinished right-click-constructed block:
the vanilla waterwheel, and modded machines built in stages the same way.

## The bug

`BEBehaviorRightClickConstructable` shows what the next construction stage needs through
`RightClickConstruction.GetInteractionHelp`, which calls `GenInteractionHelp` every time. For
each ingredient of the next stage, that method creates an `ItemStack` for **every collectible
in the game** and asks the ingredient whether it fits:

```csharp
foreach (CollectibleObject collectible in api.World.Collectibles) {
    ItemStack itemStack = new ItemStack(collectible);
    if (constructionIngredient.SatisfiesAsIngredient(itemStack, checkStackSize: false)) ...
}
```

The client asks for the hint whenever the selection changes, so the game stalls on placing the
block and on moving the crosshair from cell to cell of it. It doesn't stall while the view holds
still, or once the block is finished. `OnInteract` also calls it on every construction step, on
the client and the server. The more mods, the more collectibles, and the longer the stall.

## The fix

A Harmony transpiler on `RightClickConstruction.GenInteractionHelp`. Right after the method reads
`World.Collectibles`, it swaps in a shorter list: the collectibles that pass the checks
`SatisfiesAsIngredient` makes before it needs a stack.

- Any wildcard, regex or tags-only ingredient: the item class must equal `Type`, and a set `Code`
  must pass `WildcardUtil.Match` with `AllowedVariants`. That is the same call, with the same
  arguments, that `SatisfiesAsIngredient` makes.
- An exact ingredient: the same class and id as the resolved stack. `CollectibleObject.Satisfies`
  requires that unless the collectible's class overrides it. Then, and for any ingredient subclass,
  nothing is filtered.

The game's loop is left as it is. It still builds and tests a stack for each candidate, in the
original order, so the hint is identical. If the method no longer reads `World.Collectibles`
exactly once, or no longer has one `ConstructionIngredient` local, the mod logs a warning and
leaves it alone.

The mod is optional on either side (`"side": "Universal"`, not required on the client or the
server). A client with it gets a smooth hint, and a server with it skips the scan on each
construction step.

## Tests

`tests/PackTests/ConstructionHelpFixScenarios.cs` (Atlas) runs the game's original method (a
Harmony reverse patch) and the patched one on identical input, and requires the same hint, stack
for stack:

- one case per way an ingredient can match: exact, wildcard, allowed and skipped variants,
  placeholders, advanced wildcard, regex, tags only, wrong type, failed resolve, completed
  construction, and more
- every right-click-constructable block in the pack, at every stage, with each stored wildcard
- every distinct grid recipe ingredient in the pack, as a construction ingredient

The test project loads this directory's build as a mod, and leaves out a pinned copy from the
ModDB (`constructionhelpfix_*.zip` in `build/mods`).

## Releasing

The same as `mods-src/allowedvariantsfix/README.md`: bump the version in `modinfo.json` and
`ConstructionHelpFix.csproj`, merge, tag `constructionhelpfix-v<version>` on main,
upload the zip from the GitHub Release to the ModDB, then pin it in `pack/pack.toml` and run
`packtool lock`.

## Retiring it

When the game stops scanning every collectible, remove the mod and the scenarios, and mark the
mod obsolete on the ModDB.
