# The shared machine base

Every machine in the Seraph Horizons mod is to sit on one game-side base in
[`mods-src/seraphhorizons/Machines/`](../../mods-src/seraphhorizons/Machines/), as it already sits on one maths
and generator layer (`Machines/Core`, `Machines/tools/machinegen`). The ore machines were the first on it (their
`BEOreMachine` was lifted into it), the press brake (a hand machine) and the draw bench (a powered, oiled one) the
pilots that proved it covers both kinds. This page says what the base gives, how a machine moves onto it, and what
went wrong on the way. [Animated machine models](animated-machine-models.md) is the model side; this is the
game side.

## What the base gives

| Class | Where | What it does |
|---|---|---|
| `BEMachine` | `Game/BEMachine.cs` | The controller's block entity: the staged build and wearing parts (`MachineAssembly` from `config/builds/{machine}.json`), the cells and their boxes, the running rules and state, mechanical power and the load through the oil, oil, hand holds, the reservoir, fire, the block info, the renderer. Abstract: a machine subclasses it. |
| `BlockMachine` | `Game/MachineBlocks.cs` | The frame (`class: "seraphhorizons.Machine"`): placement with room for every cell, the side the player looks, stamping the cells, forwarding a click and right-click held (start, step, stop, cancel) to the block entity, drops, pick block, boxes, the built name, help, fire. One class for every machine: no `Block{X}` of its own. |
| `BlockMachineCell`, `BlockMachineCellPower`, `BEMachineCell` | `Game/MachineBlocks.cs`, `Game/BEMachineCell.cs` | The other cells (`seraphhorizons:machine-cell`) and the power cell (`machine-cellpower-{face}`, the axle's world face), in place of every machine's own ghost pair. A cell forwards everything to its controller (`IMachineGhost`, so a trunk feeder or taker finds the controller through it); the power cell carries `BEBehaviorMachineMP`. An ore machine's infeed cell is a container a chute pushes into (`BEMachine.CellTakes`). |
| `BEBehaviorMachineMP` | `Game/BEBehaviorMachineMP.cs` | The one mechanical power consumer (`seraphhorizons.MachineMP`): finds its network through the power face and puts `BEMachine.Resistance` on it. The oil is in `Resistance`, so every machine's dry rule is the same code. |
| `MachineRenderer` | `Game/MachineRenderer.cs` | Draws the rig's parts from `shapes/block/{machine}.json`, each while `ShowsPart`, posed every frame from `BEMachine.FrameInput`, lit from the cell above the footprint's middle, rebuilding its meshes when `TextureKey` changes. |
| `MachineText` | `Game/MachineText.cs` | The block info in one layout (below), with the wrench's detail and hint. |
| `MachinesSystem` | `Game/MachinesSystem.cs` | Registers the above, loads and checks the build files (`Build(machine)`, resolved against the game's items once they are known), holds the rigs of machines whose rig file is their own dialect (`RegisterRig`), and `Disable` for a switch. |
| `MachineOiling` | `Game/MachineOiling.cs` | One machine's oil, a piece any owner composes: the tank and its tree key, pouring by right-click, the drain a job, the dry effect on the shaft's load, the block info's oil lines with the wrench's detail, the smoke. `BEMachine` keeps one; the game's and Immersive Woodworking's machines one beside theirs (`MachineOil/ForeignMachines.cs`, which only hooks their methods to it). |
| `HandHolds` | `Core/HandHolds.cs` | The players working a hand machine (the quern's pattern), 600 ms by default, the timeout the machine's own. |
| `MachineJob` | `Core/MachineJob.cs` | The work on a machine: class k, W, its end, the steps counted (a hammer's blows). `Advance`, `Step`, `Restore`, `Crossed`. |
| `IMachineRig` | `Core/IMachineRig.cs` | What the base reads of a rig, whatever its dialect: cells, parts, anchors by name (`power`, `infeed`, `water`, the outlets); `OtherCells()`, `PowerAnchor()`, `ShaftAxis()`, `Middle()`, `LightCell()`. |
| `MachineInfo` | `Core/MachineInfo.cs` | Block info lines, each always shown or only with a wrench. |
| `OreMachinesSystem`, `BEOreMachine` | `Game/` | The ore machines' own half on the base: their settings and rigs, the ore figures, the infeed and outlets, tailings. |

## Moving a machine onto it

A machine on the base is a build file, a rig that implements `IMachineRig`, a frame block type, a block entity
class and its system. Everything else of the copied pattern goes.

### The build file

`assets/seraphhorizons/config/builds/{machine}.json`, the schema in `Machines/Core/MachineBuildJson.cs`. **Keep the
stages identical**: the same stages, materials, counts and order as the machine's `{X}Parts` had; only where they
are defined changes. A stage's `adds` are its rig `requires`. What the old parts classes did that the plain stages
do not:

- **One item a stage, or several at once.** A material's `quantity` is taken from the held stack in one click
  (fewer is `TooFew`), so the draw bench's two chains are `"quantity": 2`. Two items of one stage that went in by
  clicks of their own, either first (the bench's rods and spring), are two materials of the stage.
- **An item that may not exist** (the bench's spring, with `MandrelSprings` off): `"ifExists": true` on the
  material. `MachinesSystem.Build` leaves it out once the world's items are known (`MachineBuild.Resolve`).
- **A wearing part** (renewed when worn out): `wearing[]`. Fitted with a stage (`"stage": "die"`, with that stage's
  `"materials": []`) it is that stage's last material. `"requires"` hides its rig part while none is fitted.
- **A part that carries durability and comes back out** (the bench's die): `"durable": true`, `count` 1. It goes in
  with the held item's durability left (`BEMachine` passes it), `MachineAssembly.Take(id)` takes it back out with
  what is left, breaking gives it back with that durability, a spent one is gone. Its `uses` from a setting:
  override `BuildSpec` and return `base.BuildSpec(world)?.WithUses(id, setting)`.
- **Parts in any order** (the bucking mill's, the rosser's): one stage, each part a material of it with `"draws"`
  its rig `requires`, so each is drawn as it goes in; two of one item that went in a click each (the mill's
  sashes) are two materials of one code. A part a click may take fewer than it still needs with `"partial": true`
  (the rosser's ring sections); four rods that one click fills both roll sets with stay one material, the machine
  drawing the first two itself (`ShowsPart`, `RosserBuild.Shows`). A drawn `pick` is drawn only in the metal put
  in (the rosser's pipes).
- **A wearing part with a rule of its own** (the mill's blade kit, after both sashes but before or after the rest;
  the rosser's heads, on the ring): no `stage` (a stage's first set is fitted only once the whole stage is in),
  and the machine fits it from `InteractContent` with `Wearing.Renew(code, held, built: true, uses)`, its rule in
  `Core/` beside its build (`BladeKits.Fit`, `RosserBuild.FitHeads`). Uses that follow the item fitted (each
  metal's own durability or capacity): `WithUses(id, code => ...)` (`WearingPart.UsesOf`).
- **No `block`** for a workshop machine whose construction record the recipe export writes by hand (the press
  brake, the draw bench): with a `block` the exporter's generic ore-machine pass would add a second construction
  record and a power consumer (`RecipeSection.OreMachines.cs`, `Power/OreMachineConsumers.cs`). Reworking the
  exporter's paths is a later job.
- Stage names: lang `seraphhorizons:{machine}-stage-{id}` (else the file's `name`).

The unit test `Every_shipped_build_file_is_its_rigs_stages` (`tests/Machines/OreMachineSharedTests.cs`) checks every
build file against its rig: a rig without `buildStages` is held to its parts (every `adds` must be a part's
`requires`).

### The rig

The rig file's dialect stays as it is (unifying it is out of scope). Make the machine's rig class implement
`IMachineRig`: `Parts` its moving parts, `Anchors` its anchors by the shared names (the draw bench maps
`powerCell`/`powerFace` to `power`; a hand machine has none), and register it from the machine's system:

```csharp
public override void Start(ICoreAPI api)
{
    _api = api;
    _config = LoadConfig(api);
    api.RegisterBlockEntityClass("seraphhorizons.DrawBench", typeof(BEDrawBench));
    MachinesSystem.Of(api).RegisterRig(MachineId, () => Rig);
}
```

A machine not registered is read as an ore machine's rig (`OreMachinesSystem.Rig`). `MachinesSystem` runs first
(`ExecuteOrder` 0.05). The rig must still load in `AssetsLoaded` (collision lookups run off the main thread).

### The frame block type

`class: "seraphhorizons.Machine"`, `entityClass` the machine's block entity class. The machine's id is the code's
first part (`drawbench-frame-north` is `drawbench`) or `attributes.machine.machine`. Delete the ghost block types
and drop them from the system's `TypeAssets`, and their `block-{x}-ghost*` lang lines.

### The block entity

Subclass `BEMachine` and override what is the machine's. Each opt-in is a few lines:

| To | Override |
|---|---|
| Take power | Give the rig a `power` anchor. `ShaftLoad` (the built load before oil), `ConfigMinSpeed` (else 0.05), and `Work(radians)` for the shaft's advance while `Working`. `LoadsShaft` when the token 0.005 lasts past the build (the bench's: until the die is in). |
| Take oil | `OilKind => OilMachine.X` (add `X` to `OilMachine` and a property `X` to `MachineOilConfig` with its defaults: that is all the settings need), `DryEffect` (`Load`, the default, multiplies the load while dry; `Wear` leaves it and the machine wears its cutter by `OilFill`, the gear cutter's and cylinder borer's rule), `DrainOil(jobs)` at each finished job, `SmokePoint`. The base does the rest through its `MachineOiling` (below). |
| Be worked by hand | `HandWorked => true`, `HoldTimeoutMs` if not 600 (the rocker's 400), `StartHold(byPlayer)` from `InteractWork` on a work click, `HasWork`, and `WorkUnpowered(dt)` while held. The base forwards the steps and the stop, keeps the holds, syncs `Held`, and gives the state `Unworked` ("hold right mouse") while work is on and no one holds. |
| Hold water | `WaterPerItem`, `ReservoirCapacity`, `WaterIntakePerSecond`, a `water` anchor on the rig; `SpendWater` per item. |
| Hold items | `NewInventory()` (the base's is empty), `CellTakes` for a chute into a cell. |
| Burn | `Ignitable`, `Burning`, `CannotIgnite`, `Ignite`. |
| Take its work | `InteractContent` (before the build: take the work back, a bucket), `InteractWork` (after it: load the work, start a hold). Return whether the click was the machine's; act on the server. `CreativeShortcut(player)` tells a creative Ctrl from the machine's own Ctrl. |
| Say when it is whole | `Complete` (the name), `CreativeFills` (the creative shortcut), `LoadsShaft`: each `Built` by default; `Ready` (built and every wearing part in) for a machine that stops without its wearing part. |
| Draw | `FrameInput(dt, far)` for its own clock (the base's turns θ and ψ with the shaft: `AdvanceShaft()`); `ShowsPart` for parts drawn by the work rather than the build; `TextureKey` and `PartTextures` for a texture the build picks (an edge's metal); `RenderRange`; `Animates`. Particles go in `FrameInput`, sounds in `OnClientTick`. |
| Save | `ToTreeAttributes`/`FromTreeAttributes` for the work only, calling the base: the build, wearing parts, state, oil, water and holds are the base's. |
| Be placed lengthwise | `attributes.machine.lengthwise: true` on the frame: it extends away from the player, its native west end farthest (`Footprint.PlacedFacing`, the mill's and rosser's rule), not its native north facing them. |
| Give its own boxes | Override `CellBoxes`/`CollisionBoxes` (a trunk lying in it: the mill's, by trunk class; the rosser's, along its path, with hollow cells). Build tables whole and only read them there: collision lookups run off the main thread. |
| Draw more than its parts | `CreateRenderer` returns any `IRenderer`: the mill's and the rosser's own (a trunk, parts in a fitted metal, sounds and particles of their own clocks). |
| Fit from a slot of its own | `TryFit(slot, player)` does a click's fitting from any slot (a scenario's, a creative shortcut's): true, false with the error sent, or null when it is no part. |
| Help | `WorkHelp` for the built machine's lines; the build's, the renewal's and the creative shortcut's are the base's. |

Ticks: `TickMs` (server, 100 by default; the workshop machines' 50) and `ClientTickMs`. The server's tick calls
`Tick(dt)` (the machine's own business: chutes), then computes the state and calls `Work` or `WorkUnpowered`
while it is `Working`. If `Work` returns true the base marks the machine dirty with a full sync: a machine that
syncs its own W in steps returns false.

### Oil: one piece for every machine

Oil is not the base's own code: it is `MachineOiling` (`Game/MachineOiling.cs`), which any owner composes,
over the tank maths (`Core/MachineOil.cs`) and the low-level tree, pour and smoke helpers (`Game/Oil.cs`).

| What | `MachineOiling` | The base calls it | The foreign patches call it |
|---|---|---|---|
| Made | `NewIfOn(api, machine, effect)`; `New(machine, config)` | `Initialize` | `StateOf`, the first time the server sees the machine |
| Saved and synced | `Write(tree)`; `Restore(tree, world, ...)`, or `Load` / `Read` | `To/FromTreeAttributes` | the `To/FromTreeAttributes` postfixes |
| Filled | `Interact(owner, player)`: null when the player holds no oil | first in `OnInteract` | the `OnBlockInteractStart` prefixes |
| Drained | `Drain(owner, jobs)`, by the entry's `DrainPerJob`, redrawing when the tank runs dry | `DrainOil(jobs)` | the jobs' postfixes (a strike, an item, a pass) |
| The load | `Asked(resistance, owner)`: times the dry multiplier while dry for `OilDryEffect.Load`, as it is for `Wear` | `Resistance` (the shared MP behaviour's) | the `GetResistance` postfixes |
| The info | `AppendInfo(MachineInfo, api)`; `AppendInfo(dsc, player, api)` writes it whole with the wrench's detail and hint | `GetBlockInfo` | the `GetBlockInfo` postfixes (with the load line, as they have no shaft line) |
| The smoke | `SmokeIfDry(capi, at)` | the client tick, while running | `MachineOilSystem`'s smoke tick, while turning |

The settings block is the same for every machine: `MachineOilSettings.{Machine}` is
`{ "Tank": points, "DrainPerJob": points }` (100 points to the litre), with `DryResistanceMultiplier` (3) and the
oils (`OilLiquids`, `OilLumps`) shared. A new entry changes the config defaults, so regenerate the snapshot
(docs/config-defaults.md), and give the machine's power entry its `oil` in the recipe export (`Spec.Oil`, or the
ore machines' pass). The bucking mill, rosser, gear cutter and cylinder borer still use `Oil`'s older "own
machine" helpers (`NewOwn`, `LoadOwn`, `Interact`, `Info`, `Drain`); they move to the piece with the base, and
those helpers go.

### The block info

One layout for every machine (`BEMachine.GetBlockInfo`, `MachineText`), in this order:

1. The build: `Build: stage n of m, {stage}` and `Still takes: …`, or once built each wearing part's share left
   (`seraphhorizons:{machine}-wearing-{id}` names it; `{machine}-info-worn` says a missing one its own way).
2. The state, with the work's percent while working (`MachineRun`; `seraphhorizons:{machine}-state-{state}`
   overrides a state's line: the bench's `idle` is "Bench empty: put a lead or copper tube blank on").
3. The oil: the tank, and while dry how much harder it turns.
4. The reservoir.
5. The machine's own lines: `AppendContents` (what is on it, the die's metal).

**With a wrench** (the game's `EnumTool.Wrench`, `game:wrench-*`, in either hand) the detail follows: each
wearing part's uses left, the oil a job drains and the jobs left, the dry load against the oiled one, the shaft's
speed and load, the facing and the face the axle meets, and the machine's own `AppendDetail` (W exactly, turns a
section). **Without one**, a last line says a wrench shows more, whenever there is detail (there always is: the
facing). Judge per line: what a player acts on is a line; figures for tuning or checking are detail.

### What to delete

The machine's `Block{X}`, `Block{X}Ghost`, `Block{X}GhostPower`, `BE{X}Ghost`, `BEBehavior{X}MP`, `{X}Renderer`,
its `{X}Parts` (with the stage enum and its tests), its `{X}Holds`, its job record (`FoldJob`, `DrawJob`: keep the
arithmetic as static helpers over `MachineJob`, `Folding.Start`, `Drawing.Advance`), the renderer's view interface
(`I{X}View`), the ghost block types, and the lang lines nothing uses any more (`{x}-error-order`,
`-part-fitted`, `-not-a-part`, `-too-few`, `blockhelp-{x}-fitpart`, `{x}-info-next`, `-then`; the shared
`machine-error-*` messages replace them). Grep the whole repository for each name before deleting: another machine
may borrow it (the amalgam pan used the press brake's `LeverHolds`).

### Tests

- **Unit** (`mods-src/seraphhorizons/tests/{X}/`): the parts tests become tests of the build file through
  `MachineAssembly` (`MachineBuildJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds",
  "{x}.json")))`; the build files are copied to the output). Keep every rule the old tests held (order, counts, what
  is refused); drop only what saves breaking took away (the press brake's legacy rod screws).
- **Atlas** (`tests/PackTests/{X}Scenarios.cs`): `PlaceGhosts` → `PlaceCells`, `GhostCells()` → `CellBlocks()`,
  `BE{X}Ghost` → `BEMachineCell`, `Block{X}GhostPower` → `BlockMachineCellPower`, `BEBehavior{X}MP` →
  `BEBehaviorMachineMP`, `Block{X}` → `BlockMachine`, `{X}Parts` → `Assembly` (`Build.Next`, `Build.CodeIn`,
  `Build.StageComplete`, `Returns()`), and the block info's strings to the shared layout (a helper that hands the
  player a wrench reads the detail). Check `SwitchesOffScenarios` and the recipe export's scenarios for the names too.
  Run the machine's scenarios, `RecipeExport*` and `RecipeExportPowerScenarios`.
- **The export** must not change: a power consumer `Spec` on the old behavior becomes `BEBehaviorMachineMP` with
  `ByFrame = true` (`tools/recipe-export/Power/Consumers.cs`), found by its frame.

## Pitfalls

- **The state lags a tick.** The state is the server tick's. The base refreshes it after every interaction
  (`RefreshState`); call it yourself after anything else that changes it between ticks.
- **`Running` of a hand machine** is `Working` and `Held`: let go, it stops before the next tick.
- **A whole stage a click.** The creative shortcut fills the next stage, every material at once, where the old
  parts classes filled one item a click (the bench's rods, then its spring: five clicks now, not six).
- **`Built` is not `Ready`.** A machine whose wearing part is fitted with its last stage is built with the part
  missing. Decide per use: the name, the creative shortcut and the token load were the bench's "complete", die and all.
- **Saves.** The base's tree keys differ from the old ones and nothing migrates: a machine saved before loads as
  a bare frame (the owner's ruling: breaking saves is fine). Block and item codes stay.
- **The `Machines` namespace.** A property named `Machines` on a block entity hides the namespace for every
  subclass (`Machines.Tailings.Discard` stopped compiling).
- **`new` on `Rig`.** A subclass that wants its own rig type hides the base's: `private new DrawBenchRig? Rig =>
  base.Rig as DrawBenchRig;`.
- **Names the base already has.** `State` (a `MachineState`), `Running`, `Wet`, `Water`, `Complete`, `Power`,
  `Side`, `Rig`, `Oiling` and `OnInteract` are the base's: a machine's own trip state, running rule or wet flag
  takes another name (`TripState`, `ShaftRuns`, `SawsRun`, `DripWet`), or `new`, as `Rig`.
- **`Work` only runs when the shaft advanced.** The base calls it while `Working` and the shaft turned this tick;
  what must run while stopped (the mill's hand winding, a hold lapsing) goes in `Tick`, which runs every tick
  before the state.
- **A machine that never stops for water.** The rosser debarks dry: `Wet => true` keeps the running rules from
  stopping it, while its reservoir is still the base's (`SpendWater`, `Water.Holds`).
- **A wearing part is a code and its uses left, not an item stack.** Read `Wearing.Fitted(id)` afresh: a stack built
  from it (`StackOf`) is a copy, so changing it changes nothing in the machine.
- **A JSON patch on a block type that opens with a comment** fails ("Cannot set child value on JValue": the patch
  loader takes the comment for the root). Put the file's comment inside its object (the machine cells', which
  `patches/trunkentities-stations.json` patches).
- **Building in the sandbox.** MSBuild's parallel nodes fail silently there: build and test with `-m:1
  -nodeReuse:false`, and `-p:EnableSourceControlManagerQueries=false` when SourceLink cannot read the worktree.

## Still to move

Wave 2 moves the rest onto the base: the bucking sawmill, the rosser, the gear cutter, the cylinder borer (their
own `BEBehavior{X}MP` and oil; the gear cutter and borer with `DryEffect.Wear`), the squaring shear and the mandrel
station (hand machines: their `TreadleHolds` and `WindHolds` become `HandHolds`), the eidolon gantry, the crucible
furnace (its own build model, `FurnaceBuild`), the hand stations (riddle, rocker, amalgam pan), and oil for every
shaft-powered ore machine (the jaw breaker, stamp battery, arrastra, ball mill, jig, shaking table, vanner, and the
reverberatory roaster and Parkes kettle, which also turn on a shaft): an `OilMachine` value and a
`MachineOilConfig` entry each, their defaults scaled by load with the reasoning in each README, the config-defaults
snapshot regenerated, and their power entries' `oil` in the export.
