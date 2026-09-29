# Allowed Variants Fix

A server-side code mod that works around
[VintageStory-Issues#9256](https://github.com/anegostudios/VintageStory-Issues/issues/9256):
when a recipe has a named wildcard ingredient (`"name": "wood"`), the game ignores
`allowedVariants` and `skipVariants` on its other, unnamed wildcard ingredients.

## The bug

The game expands a recipe with a named ingredient into one recipe per value of the name
(`RecipeBase.GenerateRecipesForAllIngredientCombinations`). For each copy,
`RecipeBase.FillPlaceHolder` fills in the named ingredient and then sets `AllowedVariants` and
`SkipVariants` to null on *every* ingredient. An unnamed `game:soil-*-none` limited to `["low"]`
becomes a bare `game:soil-*-none`, and the crafting grid and handbook accept every fertility,
terra preta included.

It affects every recipe type (they all use the base method; `SmithingRecipe` overrides it but
calls it). In this pack, as of 1.22.7, it widens ten recipes in eight mods, among them BetterRuins' dry dirty
gravel (any soil), Butchering's butcher hook (any nails) and A Culinary Artillery's cauldron
(any solder). No vanilla recipe has the shape.

## The fix

A Harmony prefix and postfix on `RecipeBase.FillPlaceHolder(string, string)`: the prefix saves
the filters of every ingredient the call does not fill, the postfix puts them back. Ingredients
it fills are still cleared, as before; their code is exact by then. The game's own logic is left
alone, so the patch keeps working if the method body changes, and does nothing once the game
stops clearing the filters. If the method is gone, the mod logs a warning and patches nothing.

The server builds and sends recipes, with each ingredient's filters, so clients need nothing
(`"side": "Server"`, `"requiredOnClient": false`). Singleplayer runs the same server in process.

## Tests

`tests/PackTests/AllowedVariantsFixScenarios.cs` (Atlas) checks the issue's own recipe built in
place, BetterRuins' dirty gravel and Butchering's hook. The test project loads this directory's
build as a mod, and leaves out a pinned copy from the ModDB (`allowedvariantsfix_*.zip` in
`build/mods`), so the scenarios test the source. `tools/tests/test_mods_src.py` checks that the
`.csproj`, `modinfo.json` and the pin in `pack/pack.toml` agree on the version.

## Releasing

Built locally and uploaded to the ModDB by hand; the pack then pins it like any other mod.

1. Bump the version in both `modinfo.json` and `AllowedVariantsFix.csproj`.
2. `dotnet build mods-src/allowedvariantsfix -c Release` (needs `VINTAGE_STORY`), which writes
   `build/allowedvariantsfix_<version>.zip`.
3. Upload that zip as a new release on the mod's ModDB page. Keep the file name: the test
   project recognises the pinned copy by it.
4. Set the version in the mod's `[[mod]]` entry in `pack/pack.toml` (the first release adds the
   entry, `side = "server"`), then `python3 tools/packtool.py lock`.

Between steps 1 and 4, `test_mods_src.py` fails on purpose.

## Retiring it

When #9256 is fixed, the scenarios pass without the mod: remove the `<AtlasMod>` reference from
`tests/PackTests/PackTests.csproj` and run them. If they pass, drop the pin, this directory and
the scenarios, and mark the mod obsolete on the ModDB.
