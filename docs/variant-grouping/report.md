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
- **Untitled groups**: automatic and shipped-`groupBy` groups have no lang key; their title is derived
  from the members' names (`Core/TitleDeriver.cs`, see `Core/README.md`). The report counts groups with
  a derived title, those whose names are all the same (title = the representative's name) and those
  that fall back to the representative's name, lists the 40 largest derived titles to eyeball and the
  largest fallbacks: a big fallback (30+ members) is a candidate for an override `group` rule with a
  `title` in `mods-src/tidyvariants/assets/tidyvariants/config/overrides.json`.
- **Engine issues**: see `mods-src/tidyvariants/Core/README.md` for the kinds. `groupby-inexact` means
  the handbook gets no `groupBy` for that group (no wildcard or regex matches exactly its codes);
  `groupby-shared-code` that its pattern also matches a same-code collectible of the other kind
  (vanilla's ore block and item), which no pattern can avoid. Patterns are verified across kinds, as
  on the client.

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
`groupby-shared-code`, `variant-mismatch`) is report-only: how a mod's variants group is taste, not correctness.

## Numbers (2026-10-01, game 1.22.7)

| | |
|---|---:|
| creative entries before | 29,467 |
| hidden | 2 (variant rule: `vinteng:metalplatform-down`, `game:beenade-opened`) |
| groups | 1,452 (640 automatic, 477 shipped `groupBy`, 335 override) |
| entries in a group | 24,322 |
| tiles after | 6,595 |
| untitled automatic groups | 1,117: 594 derived title, 424 all names alike, 99 fall back (largest: 26, `purposefulstorage:tuningcylinderrack-*`) |
| issues | 64 `groupby-shared-code`, 3 `variant-mismatch` (0 `groupby-inexact`) |

Largest domains (before → after): `game` 17,130 → 4,472, `materialneeds` 336 → 200, `butchering`
392 → 150, `hardcorewaterforked` 150 → 150, `alchemy` 847 → 109, `primitivesurvival` 241 → 109,
`vinteng` 254 → 94, `cartwrightscaravan` 486 → 92, `oils` 83 → 83, `expandedfoods` 1,925 → 63.
