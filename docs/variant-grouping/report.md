# Tidy Variants report (#259)

`tests/PackTests/TidyVariantsReportScenarios.cs` reads the server's own Tidy Variants resolution
(built at `WorldReady` against every locked mod plus the local `mods-src/tidyvariants` build) and
reports what hiding and grouping do to the creative inventory.

## Running it

```sh
python3 tools/packtool.py fetch            # once: stage the locked mods in build/mods
export VINTAGE_STORY=/path/to/vs_server_linux-x64_1.22.7
dotnet test tests/PackTests --filter "FullyQualifiedName~TidyVariantsReport" --logger "console;verbosity=normal"
```

It writes `build/tidyvariants-report.md` (for reading) and `build/tidyvariants-report.json` (everything,
untruncated), and prints the Markdown to the test output. `TIDYVARIANTS_REPORT_DIR` changes the
directory: CI sets it inside the atlas results, so the report is in the `atlas-results-rest` artifact.

## Reading it

- **Tiles after**: what the full creative list shows with no group expanded: visible plain entries plus
  one tile per group. Per domain and per tab, a group counts once in each domain/tab where it has a member,
  so the per-tab numbers are what that tab shows.
- **Largest groups**: check that the big ones are things a player would want in one tile.
- **Untitled automatic groups**: automatic and shipped-`groupBy` groups have no lang key; their tile is
  named after the representative. The biggest ones are candidates for an override `group` rule with a
  `title` in `mods-src/tidyvariants/assets/tidyvariants/config/overrides.json`.
- **Engine issues**: see `mods-src/tidyvariants/Core/README.md` for the kinds. `groupby-inexact` means
  the handbook gets no `groupBy` for that group (no wildcard matches exactly its codes).

## What fails and what only reports

Fails (each is a bug in the engine, the bridge or the shipped override file):

- an entry is both hidden and grouped, a group has fewer than two members or a member whose `GroupOf`
  disagrees, or an entry is in more than one group; tiles disagree with `TidyStats`;
- a group's representative is not one of its visible members;
- the override file is missing or fails to parse, or yields `rule-unused`, `representative-unmatched`,
  `duplicate-group-id` or `placeholder-unresolved` against the pack;
- an override group's `title` lang key has no translation on the server;
- **budget**: tiles after grouping exceed `TileCeiling` (7,600). If a wanted change (a big new mod) passes
  it, read the report first, then raise the constant to about 15% above the new number and say why in the PR.

Everything else (per-domain and per-tab numbers, group sizes, untitled groups, `groupby-inexact`,
`variant-mismatch`) is report-only: how a mod's variants group is taste, not correctness.

## Numbers (2026-10-01, game 1.22.7)

| | |
|---|---:|
| creative entries before | 29,449 |
| hidden | 2 (variant rule: `vinteng:metalplatform-down`, `game:beenade-opened`) |
| groups | 1,449 (638 automatic, 490 shipped `groupBy`, 321 override) |
| entries in a group | 24,316 |
| tiles after | 6,580 |
| untitled automatic groups | 1,128 |
| issues | 16 `groupby-inexact`, 3 `variant-mismatch` |

Largest domains (before → after): `game` 17,130 → 4,472, `materialneeds` 336 → 200, `butchering`
392 → 150, `hardcorewaterforked` 150 → 150, `alchemy` 847 → 109, `primitivesurvival` 241 → 109,
`vinteng` 254 → 94, `cartwrightscaravan` 486 → 92, `oils` 83 → 83, `expandedfoods` 1,925 → 63.
