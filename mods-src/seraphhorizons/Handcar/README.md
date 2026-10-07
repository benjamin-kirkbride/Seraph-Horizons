# Handcar

Part of the Seraph Horizons mod (`../README.md`, "Handcar"): a standard-gauge rail car for Yang's
Transport Tycoon (`yangtransport`, pinned at 1.0.3), pumped by hand. A walking beam on an A-frame in
the middle of the deck rocks on a pivot pin; a pitman (connecting rod) from its front arm turns a crank
on a countershaft under the deck, whose 24-tooth wooden gear drives an 8-tooth pinion on the front axle,
so the wheels turn three times a stroke. Two riders stand at the beam's ends facing each other, both
hands on its handles. The deck behind the rear rider takes a chest or a crate.

This folder holds the model's generator (`tools/`) and the gameplay (`Core/`, `Game/`; switch
`Handcar`, settings `HandcarSettings`). Paths are from this folder unless they start with `assets/` or
`tests/`, which are the mod's (`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour.
Nothing of Yang's (shape, texture, sound) is copied: the car is coded against Yang's classes, by name,
and its model is this mod's own, in the game's oak and copper textures.

## How Yang's Transport Tycoon works, as the handcar uses it

Read from its 1.0.3 assembly (decompiled), and held by `tests/PackTests/HandcarScenarios.cs`.

- **Every class the car touches is sealed**: `EntityStandardGaugeLocomotive` (entity class
  `yangtransport.sglocomotive`), `EntityMinecart`, `EntityBehaviorSteamPowered`, both renderers, and the
  seat, `RailVehicleSeat` (internal). Nothing can be subclassed, so the car is Yang's own
  standard-gauge vehicle with a behaviour of this mod's and three Harmony patches found by name
  (`Game/YangBridge.cs`; if one is missing or changed the server logs it and there is no handcar). The
  mod does not reference Yang's assembly when it is built, as it references no other mod's: the
  release build needs only the game.
- **Drive.** A standard-gauge lead vehicle's tick asks `EntityStandardGaugeLocomotive.TryComputeConvoyDrive(leadVehicle,
  convoyLength, convoyWeight, out normalizedThrottle, out accelerationBPSPerSec, out maxSpeedBPS)`, which
  only a steam engine (`SteamEngineBehaviour`, `EntityBehaviorSteamPowered`) answers. Speed is unsigned
  (`Speed`); the way it goes is the lead end (`LeadEnd`, `EndA` forward). A throttle against the lead
  end while moving brakes to a stop (`BrakeDecel` plus drag) and then turns the lead end round. Coasting
  slows by `Roll0 + RollW x (weight - 1)` (`SGLocomotive.Drive`, which also has `HardMaxSpeed`,
  `StopEpsilon` and `ReverseMaxSpeedMul`, 0.4 by default: the handcar sets 1, it has no reverse gear).
  Track caps speed too: 16 blocks a second on wooden standard-gauge rails, 32 on metal.
- **Thrust, for scale.** Yang's primitive engine cart (`enginecart-primitive`: raw power 1, 800 °C,
  weight 1) tops out at 1 x 800 / 100 - 1 = 7 blocks a second and pulls 0.1 x 1 x 8 = 0.8 blocks a
  second squared (`SteamPowered.EngineCartDrive.TryComputeLoadedKinematicLimits`).
- **Seats.** `RailVehicleSeat` puts a rider at its attachment point (on the point's element, plus half
  that element's size: `SGLocomotiveBodyTransform.GetCenteredAttachmentPointPosition`) and turns every
  rider to the car's yaw, whatever the seat's `mountRotation`. Its `SuggestedAnimation` is the
  passenger's animation of the seat config's `animation` code. A rider gets on only by the seat's
  selection box (`SGBodySelectionBoxes`, one box per listed attachment point, the point's element; each
  must also be an `attachable` slot). Mounted keys reach the seat's `Controls` on both sides, for any
  seat, controllable or not.
- **Branch.** The convoy's lead's watched attribute `turnLeverPhase` (0 left, 1 straight, 2 right, in
  the car's frame; Yang flips it when the lead end is `EndB`) picks the branch at the next switch. The
  renderer drops the shape's `TNL_LFT`, `TNL_STR` and `TNL_RGT` elements by name, leaving the one for
  the phase.
- **Renderer.** `yangtransport.sglocomotivebogies`: the body is posed from its bogies
  (`SGLocomotive.Render.Bogies[]`, each a shape and an offset along the car; the body at the front
  bogie plus `BodyOffsetForward`), and drawn with the entity's own animator's matrices, so the body
  shape's animations run as on any entity. Its own bogie animations are baked from a distance kept
  private in the renderer; the handcar's body animation is driven by this mod instead (below). In the
  body's model frame the front (`EndA`) is towards -x, the rails' top is y 0 and the track's centre line
  is z 16; the model point (8, 0, 16) is the body's point.
- **Removal.** Sneak and right-click with an empty hand picks a stopped, empty vehicle up as the item of
  its entity's code. Yang's patch `patches/wrench-railvehicle-deconstruct.json` puts
  `CollectibleBehaviorRailVehicleDeconstructTool` on the game's wrench: held 6 seconds with a hammer in
  the other hand on a vehicle whose type is `deconstructible` with a `deconstructDrops` table, it
  spawns those drops and despawns the vehicle (which leaves its `Alive` true: a test checks it is gone
  from the loaded entities). The handcar's `deconstructDrops` is the handcar itself.
- **Weight.** `SGLocomotive.SelfWeight` plus `AttachmentWeight` (`Default`, `ByCategoryCode`, `ByCode`)
  over the attachable slots; a coupled wagon adds its own.

## Gameplay

`assets/seraphhorizons/entities/handcar.json` is Yang's `yangtransport.sglocomotive` with the bogies,
seats, cargo slot and selection boxes of this model, Yang's `sglocostats` behaviour, and
`seraphhorizons.HumanPowered` (`Game/EntityBehaviorHumanPowered.cs`), which takes the steam engine's
place. The item (`itemtypes/handcar.json`, Yang's item class) places it on standard-gauge track.

**Riding.** Two seats, front and rear, one at each end of the beam; the rear rider faces the car's
front, the front rider its back. Right-click on the deck at an end steps on; sneak steps off. A rider
only rides until they pump.

**Pumping** (`Core/HandcarDrive.cs`). Forward pumps the car the way the rider faces, back the other way.
The pumpers' directions add up: one pumping gives the solo top speed and pull, two pumping the same way
the pair's, two pumping against each other lock the beam (the car brakes to a stop and stays there).
Pumping against the way the car rolls brakes it to a stop, and then it goes back, as Yang's drive does
with a throttle against the lead end. Nobody pumping, the car coasts down by its drag. Load costs top
speed (`SpeedLossPerWeight` for each weight over the bare car's 2, down to `MinTopSpeed`). The drive goes
to Yang through a prefix on `TryComputeConvoyDrive` (`Game/HandcarPatches.cs`) for a lead that has the
behaviour: throttle (-1, 0 or 1), acceleration and top speed; Yang does the rest. Pumping in survival
costs the rider 1.5 satiety a second, scaled as sprinting is (`EntityBehaviorHunger.ConsumeSaturation`,
once a second).

**Branch.** Left or right moves the branch selector, Yang's turn lever, one place that way as the rider
faces (the front rider's left is the car's right): each press one place, edge-triggered.

**Cargo.** One `attachable` slot (`CARGO_AP`, on the deck behind the rear rider) takes a chest or a
crate, weight 1.

**Taking it up.** Yang's own: a sneak click with an empty hand, or the wrench with a hammer, gives the
handcar back whole.

**Recipe** (`recipes/grid/handcar.json`). Copper age, Yang's standard-gauge stock less its engine: four
of Yang's minecarts (the running gear, as every standard-gauge wagon of Yang's takes), two planks, a
wooden spur gear, a wooden axle and two copper rods.

| Setting (`HandcarSettings`) | Default | |
|---|---|---|
| `TopSpeedOne` | 4.2 | Blocks a second with one rider pumping: 60 % of the primitive engine cart's 7 |
| `TopSpeedTwo` | 5.95 | With two: 85 % |
| `AccelerationOne` | 0.6 | Blocks a second squared with one; the engine cart pulls 0.8 |
| `AccelerationTwo` | 0.9 | With two |
| `SpeedLossPerWeight` | 0.5 | Top speed lost per weight over the bare car's (a chest, a coupled wagon) |
| `MinTopSpeed` | 0.8 | The least top speed however loaded |
| `CoastDrag` | 0.35 | Slowing while coasting, blocks a second squared (Yang's `Roll0`, written into the entity type at load) |
| `DragPerWeight` | 0.05 | More of it per weight over 1 (`RollW`) |
| `BrakeDeceleration` | 2.5 | Braking while pumped against the way it rolls (`BrakeDecel`) |
| `SatietyPerPumpSecond` | 1.5 | A pumping rider's satiety a second, sprinting's |
| `PumpFadeSeconds` | 0.35 | The pump animation's fade in and out (client) |

A value out of range falls back to its default with a warning; `TopSpeedTwo` is never below
`TopSpeedOne`, `MinTopSpeed` never above it.

**Off** (or without Yang's Transport Tycoon, or with it changed so the bridge does not bind), the server
marks the entity type, the item type and the recipe disabled before the game loads them, empties the
riders' patch (so the seraph and the player get no handcar animations), and patches nothing; handcars
already in a world are lost. A client follows the server's world config (`seraphhorizons:handcar`).

## The riders' animation

The hard part, and how it is done:

- **Everything moves by distance, not time.** The client (`Game/HandcarAnimator.cs`, a renderer before
  the entities' own frame) measures how far each car has rolled from its interpolated position along
  its yaw (`Core/HandcarMotion.cs`, `TravelTracker`; a jump of over 4 blocks counts nothing). The
  stroke's phase is that distance over `cycle.distancePerCycle` (three turns of a 5-voxel wheel, 5.89
  blocks), and the phase sets the frame of the car's `pump` animation (wheels, countershaft, crank,
  pitman, beam) and of both riders' animations, every frame. So the hands are on the handles at any
  speed, forwards or back, rolling or stopped.
- **The animations never run by themselves.** Their speed is 0.0001 (the game skips an animation of
  speed 0 altogether, easing too) and their ease speeds 10000, so the game eases them at about 1 a
  second; their easing is set each frame ahead of the game's own step (`AnimationEasing.Before`) so it
  lands where it is meant to.
- **Two animations per seat** (`patches/handcar-riders.json`, on `game:shapes/entity/humanoid/seraph-faceless.json`
  and `game:entities/humanoid/player.json`, as Logging Expanded and Carry On add theirs): the seat's
  `grip` animation (the seat's `animation`, which Yang hands the rider as `SuggestedAnimation`, synced
  by the server) stands the rider upright on the deck, hands following the handle as it rocks; the
  `pump` animation (client-side) is the same with the torso leaning into each stroke. Both put the hands on the handles at every frame, so blending
  them never takes a hand off. The effort fades in over `PumpFadeSeconds` while the server says the
  seat pumps (`seraphhorizons:handcarPumping`, a bit a seat) and out when it stops; when a rider gets on
  or off, the grip fades in or out over a quarter second. The seats differ (the beam's two arms are not
  each other's mirror, as the pitman sits on the front one), so each has its own pair.
- **Solved for the game's own seraph.** `tools/riders.py` reads the game's seraph (from
  `VINTAGE_STORY`), and solves each arm (upper arm, lower arm, hand) for the handle at each of 60
  frames (damped Gauss-Newton, the hand weighted 40 to 1 against staying near rest), with the torso
  leaning into the stroke; the hands miss the handles by at most 0.006 voxels at the keyframes and
  between them. `tests/PackTests/HandcarScenarios.cs` runs the game's own `ClientAnimator` on the
  patched seraph and on the car's shape at sampled frames and blends and requires each hand within 0.25
  voxels of its grip attachment point (0.6 in a half-and-half blend).
- **Facing.** Yang turns every rider to the car's front. A postfix on `RailVehicleSeat.SeatPosition` adds
  a handcar seat's `mountRotation` (the front seat's 180) to the yaw, which the game's own player follows
  (the seat's angle mode is `FixateYaw`). The game draws other players' riders by the car's yaw instead
  (`EntityPlayerShapeRenderer.loadModelMatrixForPlayer`) but applies the seat's `RenderTransform` first,
  so a postfix there turns another player's front rider round.

## Model

`tools/make_shape.py` writes, deterministically, from plain boxes:

| File | |
|---|---|
| `assets/seraphhorizons/shapes/entity/handcar.json` | The body: the frame at the root, each moving part under its joint, the `pump` animation, the attachment points (seats, grips, cargo, forwarding hit boxes) and the faceless selection boxes Yang reads |
| `assets/seraphhorizons/shapes/entity/handcar-axlebox.json` | Yang's bogie shape for this car: one axle's journal boxes |
| `assets/seraphhorizons/config/handcar-rig.json` | The part rig, anchors, cycle and bogie offsets |
| `assets/seraphhorizons/patches/handcar-riders.json` | The riders' animations and their metadata |
| `tests/Handcar/rig-reference.json` | Every part's matrix at 25 axle angles (Python reference maths) |
| `tests/Handcar/rider-reference.json` | The hands' targets at sampled frames, and the seraph's figures they were solved against |

```sh
VINTAGE_STORY=$HOME/Games/vintagestory python3 mods-src/seraphhorizons/Handcar/tools/make_shape.py   # rewrite them, validated
python3 mods-src/seraphhorizons/Handcar/tools/make_shape.py --out /tmp/handcar                       # or into a folder
```

It validates its own output (`tools/validate_handcar.py`) and exits non-zero on a failed check: every
element in its part, the linkage on its pins (the pitman's ends within 0.05 voxels of the crank pin and
the beam's pin over a whole stroke; the beam swings 25 to 50 degrees, today 39.7), the gears meshing at
their pitch circles, no two parts' boxes meeting except where they are meant to (pins in their eyes,
teeth in mesh) at 36 poses, the frame standing on its sills, nothing coplanar, the cargo slot clear, the
body animation's keyframes on the rig (and within 0.15 voxels between them), and the riders: hands on
the handles, bodies clear of the beam and the stand. The car is 66 voxels long (4.125 blocks, Yang's
`VehicleLength`), the axles 37.4 voxels apart.

**Rig** (`handcar-rig.json`, the bucking sawmill's schema, `../BuckingSawmill/README.md`). One input,
θ, the axle's angle in radians. The wheels (`axle_front`, `axle_rear`) turn by θ about z, the
countershaft (`gear`) by -θ/3. The beam swings about its pivot by its angle as a Fourier series of the
crank's angle (five `swing` drivers, harmonics of ratio -k/3); the pitman swings about its rest crank
pin by its own series and slides with the crank pin (three `slide` drivers). The branch levers
(`lever_left`, `lever_straight`, `lever_right`) require `left`, `straight` and `right` and do not move
(the game shows one at a time). Anchors, in blocks in the body's model frame: `seatFront`, `seatRear`
(`pos`, the rider's feet, and `turn`, degrees), `gripFrontR`, `gripFrontL`, `gripRearR`, `gripRearL`
(`pos` at rest, `"part": "beam"`: they ride the beam), `cargo`; then `cycle` (`animation`, `frames`,
`axleTurns`, `wheelRadius`, `distancePerCycle`), `riders` (each seat's `grip` and `pump` animation
codes) and `bogies` (Yang's `BodyOffsetForward` and the bogies' offsets, which the entity type must
match). `Core/HandcarRig.cs` reads it for the game; a broken rig is logged and the riders stand still.

The site's model viewer shows it at `#/models/handcar` (`site/models.json`): Play turns θ, the grips
ride the beam.

## Tests

- `tests/Handcar/HandcarDriveTests.cs`: pumping directions by facing, coasting, solo and pair, two
  against each other (braking, then holding), load, the defaults as 60 % and 85 % of the engine cart,
  the satiety as sprinting's, the branch selector's steps, settings out of range.
- `tests/Handcar/HandcarMotionTests.cs`: the distance rolled (forwards, back, slopes, jumps), the phase
  and frame, the effort's fade, the easing set ahead of the game's step.
- `tests/Handcar/HandcarRigTests.cs`: the shipped rig parses, matches the Python reference poses through
  the shared rig maths (`Machines/Core/`), and agrees with the entity type (bogies, seats, animations,
  drive defaults, deconstruction) and the rider reference.
- `tools/tests/test_handcar_model.py`: the shipped shape and rig are what the generator builds today
  (without the game: the rider animations need the seraph), the linkage and gearing hold, the pump
  animation follows the rig, the riders' patch and hand targets agree with the rig.
- `site/test/handcar.test.ts`: the viewer's rig maths against the reference poses, the anchors.
- `tests/PackTests/HandcarScenarios.cs` (Atlas, the shared world): the car registered with its item,
  recipe, animations and Yang's wrench patch; placed by its item on standard-gauge track, two players
  mounted at the right height, facing each other; one pumping to the solo top speed, rolling forward and
  paying satiety over a rider who does not pump; both to the pair speed; pumping back braking and
  reversing; both back at the pair speed; two against each other stopping and holding; coasting by its
  drag; the branch selector stepped by both riders as they face; sneak getting both off; the wrench and a
  hammer taking it apart into its item; a sneak click picking it up; and the riders' hands on the handles
  in the game's own animator. `SwitchesOffScenarios` requires none of it with the switch off.

## Not done, and what is not checked

- **No game client here.** The renderer's side (the riders' animation in a live client, the facing
  patches on a real client, first-person) has been run only in the game's animator on the server, not
  seen in a client. In first person the game draws the player's own arms from its first-person
  animator, which the animator sets too, but whether the hands sit on the handle there is not checked.
- **Curves.** Another player's rider is turned by the car's yaw; on a curve the body and the riders'
  feet may sit a quarter of a voxel apart.
- **Item transforms** (in hand, on the ground, in the GUI) are first guesses.
- **No icon** for the recipe browser yet (`tools/icon-export`, rendered by hand).
