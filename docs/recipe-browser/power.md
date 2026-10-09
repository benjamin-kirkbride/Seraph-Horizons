# Mechanical power

The export's optional `power` section holds what the site's Power page needs: the pack's
mechanical power producers and consumers, each with the parameters of its model, and the
wind. The exporter (`tools/recipe-export/Power/`) only extracts parameters from the running
server. Every derived figure (torque at a speed, peak power, the speed a network settles at
under a load, wind-averaged output) is computed by the app (`site/src/lib/power.ts`), so the
maths lives in one language. The format is `site/src/lib/power-data.ts` and the schema's
`power` ([schema.md](schema.md#mechanical-power)).

prepare-data copies the section as it is to `data/<version>/power.json`. An export without
one (every release made before it) gives no file, and the app reads the 404 as "no power data
for this version".

## Units

Everything is in the game's mechanical network units. "Speed" is the network's `Speed`;
"torque" is what a producer's `GetTorque` returns and "load" what a block's `GetResistance`
returns. The kN in a windmill's block info is cosmetic and is carried apart, as `shownKN`.

The network adds torques and resistances and moves its speed towards where they balance. On top
of the blocks' resistances, every node adds a drag of speed² × gear ratio² / 1000. The export
does not carry that drag.

## Producer models

**Rotor** (`rotor`): torque(s) = max(0, targetSpeed − s) × torqueFactor. This is the game's
`BEBehaviorMPRotor.GetTorque`. Its `capableSpeed` moves towards `TargetSpeed` every tick, and
the figures are taken once it has settled there. The game's windmill and water wheel are rotors,
and so are Manual Crank's crank and Yang Transport's engines.

**Wind** (`wind`): a rotor whose target speed is min(speedCap, wind × speedPerWind).
`turbulencePenalty`: the game halves a vanilla windmill's torque factor when another windmill
is within 1.5 × sails blocks. Millwright's rotors do not check. `sails` gives the count the
entry assumes, the most the rotor takes, and the torque factor one sail adds.

**Constant power** (`constantPower`): ppex's engine generator. torque(s) = budget / max(s,
minSpeed) up to taperFrom, then it falls linearly to 0 at shaftSpeed.

## Producers and where each figure comes from

Each entry's `sources` lists its figures: a config file (read live, by reflection), `block
attributes` or `behavior property` (the block's JSON as loaded, after patches), `code constant
(<class>)` for a literal inside a method, or `code default (<class>)` for a default the game
uses when the JSON leaves the key out.

| Producer | Model | Figures |
|---|---|---|
| Windmill rotor, wood and metal (`BEBehaviorWindmillRotor`) | wind, at the most sails | TargetSpeed min(0.6, wind); TorqueFactor sails / 4 × `powerMul` (behavior property `powerMulByType`: wood 1, metal 1.25), halved by turbulence; most sails from attributes `sailedShapesByType.maxLength` (5, 10). shownKN = (int)(sails / 5 × 100 × powerMul): 100, 250. |
| Millwright rotors, single, double, three, six (`BEBehaviorWindmillRotorEnhanced`) | wind | bm = the fitted sail's modifier (`SailCenteredModifier`, `SailAngledModifier`, `SailWideModifier`, all 2 by default, `Millwright.ModConfig.ModConfig.Loaded`) × the blade type's (1, 2, 0.75, 1.5, code constants). TargetSpeed min(0.6 × bm, wind × bm); TorqueFactor sails × bm / 4; most sails 7, 5, 8, 6 (code constants). The entry assumes centered sails; with the three modifiers equal the sail does not matter. shownKN (int)(sails × bm / 5 × 100). |
| Millwright vertical rotor (`BEBehaviorWindmillRotorUD`) | wind | Same, with `SailWideModifier` × 1 (× 0.667 for a "two" type, which the pack does not have), at most 8 sails. |
| Water wheel (`BEBehaviorMPWaterWheel`) | rotor | TargetSpeed min(0.3, flowRate); TorqueFactor flowRate. Two entries, below. |
| Hand crank (Manual Crank, `BEBehaviorManualCrank`) | rotor | `TargetSpeed` 0.5, `TorquePerPlayer` 0.5 per player, `HungerCostPerSecond` 2.5 (`ManualCrank.ManualCrankModSystem.Config`). One entry, one player. |
| ppex Watt and Cornish engines (`BEBehaviorEngineMPGenerator`) | constantPower | P = `WattEngineMaxPower` 0.3, or `CornishEnginePowerLow/Normal/High` 0.2, 0.4, 0.8 (`PipesAndPowerExpanded.PpexValues`). budget = P × `MpLoadPerEnginePower` 2 × `MpRatedSpeed` 1; shaftSpeed = (0.5 + P) × π / 5 (`BlockEntityEngine`); minSpeed 0.25 × MpRatedSpeed; taperFrom shaftSpeed × 2/3. Cost: `WattEngineSteamRate` 30, `CornishEngineSteamLow/Normal/High` 8, 16, 32 L/s. Conditions: inlet steam in the engine's pressure band (`…EngagePressure…`, `…BreakPressure…`). |
| Yang Transport steam engines (`SteamMechanica`) | rotor | At temperature T, the tier's `MaxTemperatureC` (block attributes `SteamEngine`: 800, 1100, 1200, 1300 °C): target speed T/100 × `AccelerationNPer100C` × `StationarySteamEngineSpeedMultiplier`, stall torque T/100 × `RawPowerNPer100C` × `StationarySteamEnginePowerMultiplier` (multipliers 1, `YangTransport.YangTransportSettings`); torqueFactor = stall / target. |

The creative rotor is left out.

**The water wheel.** Each tick the wheel looks at the 8 cells of its ring (radius = diameter / 2,
integer: 1 for the 3 m wheel). It counts only moving water whose `flowSpeed` is above the wheel's
`requiresMinFlowSpeed` (1.5), which in practice means rapid water: `flowSpeed` 2, push 0.004 along
its flow. Plain water has no `flowSpeed` and gives nothing. flowRate = |Σ (radial unit × facing)
· push| × radius × 750. One rapid water cell under the wheel, flowing across it, gives 0.004 × 750
= 3. The bottom cell and its two diagonal neighbours give (0.004 + 2 × 0.004 × √½) × 750 ≈ 7.24.
The push and `flowSpeed` are read from the rapid water block's attributes, and the wheel's
`diameter` and `requiresMinFlowSpeed` from its behavior properties (both unset, so the code's
defaults).

## Consumers

`load` is the load while working, fully oiled. `loadMax` is the high end of a ranged load, and
`idleLoad` the load while idle, unassembled or empty where the game sets one. `category` is
`machine` (works), `transmission` (only passes power on: friction) or `brake`.

| Consumers | Load from |
|---|---|
| Blocks with the game's own `MPConsumer` behavior: quern, grinding wheel, linkage arm, Hydrate or Diedrate's well winch, A Culinary Artillery's powered mixing bowl | behavior property `resistance`, 0.1 when unset (`BEBehaviorMPConsumer`). Found by scanning every block, one entry per domain and first code part. Whatever attaches to a linkage arm may raise its load at runtime. |
| Pulverizer 0.085 (0.005 without an axle), helve hammer 0.125 with a hammer (0.0005 without; the load is on the toggle on the axle, `game:woodentoggle`, which the entry links to), axles, angled gears, transmission and spur gear 0.0005, large gear 0.004, brake 3 | code constants (`BEBehaviorMPPulverizer`, `BEBehaviorMPToggle`, ...). The Archimedes screw is the behavior property `resistance`, 0.015 when unset, per block. The brake rises from 0 by 1 every 20 seconds while engaged, up to 3. The clutch is not a network node and adds nothing. |
| Millwright's brake 6, axle passthroughs 0.0005 | The game's brake ramp × `BrakeResistanceModifier` (2); code constant. |
| MPE gearbox and centered spur gear 0.001, Mad Mechanics' transmission 0.0005, ppex's MP generator 0.0005 | code constants. |
| Immersive Woodworking's sawmill and chopper | `SawmillResistance`, `ChopperResistance` 0.085 (`ImmersiveWoodworkingModSystem.Config`); 0.005 until assembled. A sawmill with a flywheel takes × 0.6, a separate entry. |
| Panning Machine | `MechanicalResistance` 0.085 (`PanningMachineModSystem.Config`). |
| smex's Bessemer transmission; mechanical blower | `BessemerTransmissionResistance` 0.25. The blower takes `MpBlowerBaseLoad` 0.05 + `MpBlowerLoadPerAtm` 0.05 per atm of back-pressure, up to `MpBlowerMaxPressure` 2: load 0.05, loadMax 0.15 (`SteelmakingExpanded.SmexValues`). Its port behavior is added to the blower's structure in code, so the entry is found by the block `smex:mpblower`. |
| ppex's mechanical fluid pump | `MpPumpBaseLoad` 0.05 + `MpPumpLoadPerAtm` 0.05 × `MpPumpDeliveryPressure` 1.5 = 0.125. |
| Gondola Cable Car stations | `ActiveStationResistance` 2.25 while running, × 1.6 with Viking cabins (code constant, a separate entry); `IdleStationResistance` 0.02 idle (`GondolaCableCarModSystem.Config`). A small station takes ActiveStationResistance × cabins / 5, 0.45 per cabin (the entry is for one). |
| The pack's own machines: bucking sawmill, rosser, gear cutter, draw bench | `Resistance` 0.17, 0.2, 0.2 (`BuckingSawmillSystem`, `RosserSystem`, `GearCutterSystem` `.Config`); the draw bench's `ResistanceLead` 0.2, `ResistanceCopper` 0.35 as loadMax. 0.005 until assembled (`BEBehaviorMillMP.IncompleteResistance`). |

**Oil.** The pack's MachineOil (`mods-src/seraphhorizons/README.md`, "Machines need oil") gives
some machines an oil tank: the helve hammer, the pulverizer, Immersive Woodworking's sawmill and
chopper, the bucking sawmill, the rosser, the gear cutter and the draw bench. A dry machine
loads its shaft `DryResistanceMultiplier` (3) times. Such an entry has `oil: { dryMultiplier,
tank }`, with the tank in litres (`MachineOilSettings.<machine>.Tank` points / 100). The gear
cutter is the exception. A dry tank wears its cutter kit faster instead of raising its load, so
its `dryMultiplier` is 1 and its note says so. With MachineOil off (or without the pack's mod)
no entry has `oil`.

## The wind

The patterns are the server's own, `game:config/windpatterns/*.json` as loaded
(`WeatherSystemServer.WindConfigs`): still, light, medium and strong breeze, and storm. The
exporter runs them the way `WeatherSimulationRegion` does, with the game's own `NatFloat`,
`LCGRandom` and `SimplexNoise`:

- When the current pattern runs out, a new one is picked uniformly (`Rand.NextInt(n)`). The
  patterns' `weight` is not used for wind.
- Its base strength is drawn from `strength` (uniform: avg + (U − 0.5) × 2 × var) and its
  duration from `durationHours` (invexp: avg + U1 × U2 × var, so on average avg + var / 4;
  light breeze, 6 and 48, lasts 18 hours on average, 6 to 54). Each pattern's
  `durationMeanHours` is that mean, worked out from the distribution it declares
  (`durationDist`).
- While it lasts, its strength is the base plus its gust noise, clamped to 0 to 1:
  `SimplexNoise(amplitudes, frequencies, seed + index).Noise(0, totalDays × 10)`. This applies
  only when the pattern has `strengthNoise`; `gusts` says so.

The run is deterministic: seed 1, 100 game years of the server's calendar (`DaysPerYear`,
`HoursPerDay`: 108 days of 24 hours in the pack) at 60 samples per game hour, about 15.5 million
samples. It takes about 1.3 s of the export. The result is each pattern's `share` of the time, a
`histogram` of sea-level wind in bins of 0.01 from 0 to the highest sample (still's negative
strengths count in the first bin), and the `mean`, with negative strengths counted as 0. A run
of the pack gives still 14.8%, light 30.2%, medium 20.1%, strong 25.6% and storm 9.3% of the
time, a mean of 0.425, and 28.7% of the time at 0.6 or more, which is a windmill's full speed.

The wind's `sources` name the asset each pattern was read from and the model's code constants
(the uniform choice, the gust formula, the draws, the altitude factor). A pattern that matches
no `config/windpatterns/` asset is marked `fallback`.

**Altitude** (`WeatherSimulationRegion.GetWindSpeed`; code constants). Above sea level the wind
is the sea-level wind × max(1, 0.9 + blocks above sea level / 100), capped at 1.5. Below sea
level it is divided by 1 + blocks below / 4; the export does not carry that. A windmill also
sees no wind with sunlight below 5 at its block unless the world allows underground windmills.

## Fallbacks

A figure the exporter cannot read (the mod is loaded but its type or member is gone, or a block
it expects is missing) takes the exporter's built-in default. The source is then marked
`fallback: true` and the server log warns
(`[seraphexport] power: cannot read ...`). The export does not fail on a fallback, but the Atlas
scenario `Power_section_reads_every_figure_live` does. So a mod update that renames a setting
fails CI rather than publishing stale figures. The export of the current pack has none. A mod
that is not loaded leaves its entries out.

## Adding a producer or consumer

- **A consumer** with the game's `MPConsumer` behavior needs nothing: the scan finds it.
  Anything else is one `Spec` in `Power/Consumers.cs`. A spec gives the behavior class (its
  short name, as the class registry resolves the block's behavior names), the category and
  where the load comes from: `Const` for a literal in `GetResistance` (cite the class), or
  `Cfg` for a config member by the type's full name and a dot path (`Live.Get` reads a loaded
  mod system's instance or the type's statics, properties and fields, public or not). Give
  `Mod` (the mod id that must be loaded) and, when the blocks should be one entry, `Id` and
  `ItemPrefix`. Without them the blocks are grouped by domain and first code part. `Oil` names
  the machine's `MachineOilSettings` entry.
- **A producer** is a method in `Power/Producers.cs`. Read block JSON with
  `Live.BlockNumber`, config with `Live.Number` and literals with `Live.Constant`, and build
  the model with `Rotor` or `Wind`, or a `constantPower` object. If no model fits, add one to
  `power-data.ts`, the schema, `site/src/lib/power.ts` and this page.
- Add the figures to `tests/PackTests/RecipeExportPowerScenarios.cs`, worked out by hand from
  the decompiled code, and run the scenario to check there is no fallback.
- `site-data validate` checks that ids are unique, that `item` is a key of `items` (or null)
  and `mod` a key of `mods`, that `loadMax` ≥ `load`, `sails.count` ≤ `max` and `taperFrom` ≤
  `shaftSpeed`, and that the wind's pattern and histogram shares each add up to 1 (within
  0.001, since each is rounded to 6 places).
