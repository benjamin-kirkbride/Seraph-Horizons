# Trunk entities

Part of the Seraph Horizons mod (`../README.md`), switched by `TrunkEntities` in
`ModConfig/seraphhorizons.json` (on by default), with its figures in `TrunkEntitiesSettings`.
Logging Expanded's (`loggingmod` 0.3.6) tree trunks stop being items you pocket. A felled tree
leaves a **trunk entity** lying on the ground, which you take by one end and drive on foot like a
sled, move with a rope, float down a river, work with tools where it lies, or shoulder very slowly through
Carry On (`carryon`) to load a station, a rack or a cart. A trunk is never in an inventory: the
only "slot" that holds one is Carry On's hands.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, `Rosser/`, `BuckingSawmill/` or `Woodworking/`,
which are this folder's neighbours. `Core/` is the game-independent part (settings, weights and
boxes, unit-tested without the game), `Game/` the entity, its renderer, the mod systems and the
patches. No other mod is referenced at build time: Logging Expanded is read through
`Machines/Game/LoggingBridge.cs`, Carry On and the stations are found by name and patched with
Harmony.

**What it needs.** Logging Expanded. With the switch off, Logging Expanded missing, or
`LoggingBridge` not resolving (Logging Expanded not as expected, one warning), nothing below
happens and Logging Expanded's trunks are items again, as it ships them. Carry On is optional:
without it there is one warning ("trunks cannot be carried: drive or rope them") and everything that
goes through hands is gone. A trunk can then be driven, roped, floated and worked with
tools, and fed to a rosser or a bucking mill by driving it into their infeed cells, but Logging
Expanded's sawhorses, Trunk Storage Rack and heating rack cannot be loaded at all (no trunk is in a
hand or an inventory to load from), carts and sleds take none, and Ctrl + right-click on a rosser
or mill lays the trunk on the ground beyond its infeed end. The bark spud on a trunk needs the
debarked trunk (the `Rosser` switch, `../Rosser/README.md`); without it the spud does nothing to a
trunk. Its bark needs Immersive Woodworking (`immersivewoodworking`); without it (or with its bark
roll not as expected, one warning) the spud still debarks, with no bark.

## The entity

Two entity types, `seraphhorizons:trunk-thin` and `seraphhorizons:trunk-thick`
(`assets/seraphhorizons/entities/trunk-*.json`), both of class `seraphhorizons.EntityTrunk`
(`Game/EntityTrunk.cs`). They are the two display classes the machines already draw trunks in
(`Machines/Core/TrunkBox.cs`): Logging Expanded's sizes xs, sm, md and lg (up to 24 logs) are thin,
shown as its `lg` model, 1 × 1 × 4 blocks; xl and xxl (25 logs and up) are thick, shown as `xxl`,
2 × 2 × 5. The type is picked from the stack's `size` variant when the entity is spawned
(`TrunkSpawns.Spawn`), and the type decides the boxes.

| | Thin | Thick |
|---|---|---|
| Sizes | xs, sm, md, lg | xl, xxl |
| Shown as | `lg`, 1 × 1 × 4 | `xxl`, 2 × 2 × 5 |
| Collision (`passivephysicsmultibox`) | four 1 × 1 × 1 boxes along z, x −0.5..0.5, z −2..2 | four 2 × 2 × 2 boxes one block apart (middles at z −1.5, −0.5, 0.5, 1.5), x −1..1, z −2.5..2.5 |
| `hitboxSize` | 1 × 1 | 2 × 2 |
| Floats with | half its height (0.5) under | half its height (1) under |

**Payload.** The trunk's own item stack (`loggingmod:treetrunk-{wood}-{size}-{branches}-{side}`,
its logs in `slots/0`, `branchCount`, resin and char in its attributes) is the entity's watched
attribute `trunk`, so it syncs to clients and saves with the entity, and whatever takes the trunk
takes that stack unchanged. `SetTrunk` rewrites it; a stack with no logs (or none) removes the
entity, nothing dropped, and a stack of the other display class (an xl trunk cut down to lg) goes
to a new entity of that type at the same place and yaw, the old one removed. An entity that loads
with no trunk or no logs removes itself.

**Behaviours** (both sides unless noted): `repulseagents` with `movable: false` (walking into a trunk
does not move it; the behaviour still pushes creatures off its middle box, and the entity counts as
a creature, `IsCreature`, so the game's shoving finds it, as the boat is found), `seraphhorizons.trunkphysics` (`Game/TrunkPhysics.cs`: the game's
`passivephysicsmultibox` with the drive and the step-up inside its tick, below; gravity factor 1,
ground drag 1, falling air drag 0.5), `ropetieable`, `seatable` with one seat, the driver's
(`controllable: false`, below), and on the client `interpolateposition`. With Carry On the server's list also
gets the pack's pick-up behaviour (`seraphhorizons.trunkcarry`, below). The game's multi-box physics
moves each box's middle round with the yaw but keeps the box itself axis-aligned and its size, as
for the raft, so every box is square across (a thick trunk's overlap): a trunk at a quarter turn
collides as the same trunk along x (5 × 2 for a thick one, not 6 × 1), and a thin trunk lying at
45° as a staircase of four cubes. No despawn, no decay.

**Buoyancy.** `MaterialDensity` 700 (water is 1000) and `SwimmingOffsetY` half the display class's
height, so a trunk floats about half under and the game's passive physics moves it with flowing
water as it moves items.

**Weight.** 10 + 8 per stored log (`WeightPerLog`, `Core/TrunkWeight.cs`): a 4-log trunk weighs 42,
a 10-log one 90 (about a cart), a 48-log one 394. The game's rope pull on an entity
(`ClothPoint.update`) is scaled by `clamp(50 / weight, 0.1, 2)` once the rope is taut, so weight is
what makes a big trunk follow slowly: about 1.2 at 4 logs, 0.56 at 10, 0.24 at 25, 0.13 at 48, and
the floor of 0.1 only past 61 logs, so every trunk Logging Expanded makes can be roped by one
player. Driving goes by the logs, not the weight (below). `Entity.Properties` is the type's shared object, not a copy per entity, so each trunk clones
it in `Initialize` before setting its own weight; the server keeps the weight in the watched
attribute `seraphhorizons:weight`, which a client applies to its copy.

**Shown.** The client renderer (`seraphhorizons.trunk`, `Game/TrunkEntityRenderer.cs`) tessellates
Logging Expanded's block for the stack in its display size (`Trunks.ShownBlock`: thin as `lg`,
thick as `xxl`, the debarked block if the trunk is debarked, `../Rosser/README.md`), laid along z
with its underside's middle on the entity's position, turned by the yaw as the collision boxes
are. It re-tessellates when the shown block changes (debranched, debarked). The info text names
the wood, the logs, the branches while there are any, "Debarked", the weight and, while someone
drives it, who ("Moved by"). The name is the stack's ("Oak Tree Trunk"); the types' own lang names
(`item-creature-trunk-thin`, `-thick`) are "Tree trunk".

**Selection.** The entity's square hitbox is only its width across, so `EntityTrunk.IntersectsRay`
picks against the collision boxes turned with the yaw (`TrunkBoxes.Turned`), square across as they
are, so the row keeps its width: a trunk is picked along its whole length whichever way it lies,
the nearest box hit winning (the game reads the tester's last hit point as the hit position, so the
nearest is tested again last). That alone was not enough in play: the game measures reach to
`SelectionBox` (the server's interaction range check, and attack range on both sides, 1.5 blocks
by default), so with the square hitbox only the middle of a long trunk was in reach.
`EntityTrunk.FitSelectionBox` sets `SelectionBox` to the bounding box of the turned boxes whenever
the yaw changes; at a slant it is wider than the trunk, which only makes the range checks lenient.
The collision box stays square, and `repulseagents` reads that and `touchDistance`, so the game's
own shove acts only at its middle; the trunk's solidity (below) acts on every box. A trunk entity is never collected (`CanCollect` false).

**Help.** Looking at a trunk: take this end, with the keys (empty hand; not while someone drives
it), tie a rope (with a `game:rope`), shoulder
it (Shift, empty hand, with Carry On), and each tool that would work it in its present state (the
knife while it has branches, shears from twelve, the axe and saw once they may, the spud on a clean
trunk with bark). Logging Expanded lists all four tools on a placed trunk whatever its state.

## How a trunk comes to be

**The spawn swap** (`Game/TrunkSpawns.cs`). Logging Expanded's `FellingListener.SpawnTrunk` throws
the felled tree's trunk as an ordinary item entity (`SpawnItemEntity`). The server's
`OnEntitySpawn` hook removes every item entity whose stack is a trunk with logs, and spawns a trunk
entity in its place, lying the way the item was thrown (or any way, if it was not moving), one per
stack item. So felling, a sawhorse or station unloading onto the ground, a machine broken with a
trunk on it, or anything else that drops a trunk makes a trunk entity, and the pack's own code may
keep calling `SpawnItemEntity`. Trunk items saved in a world from before are swapped as they load
(`OnEntityLoaded`, a tick later, not mid-load). A trunk item holding no logs stays an item.

**Unpocketable.** A trunk item is an ordinary block stack: it keeps Logging Expanded's own storage
flag (`Backpack` while its `TreeTrunkBackpackOnly` is on) and sits in and moves between the slots
that take that flag like any block, so one given in creative or by command, or left over from before the feature, behaves.
It is unobtainable in survival instead: `TrunkPockets` prefixes the game's
`PlayerInventoryManager.TryGiveItemstack`, which every give path goes through (a station or machine
unload, Logging Expanded's own, picking up an item entity), and refuses a trunk unless the player
is in creative mode. The caller then drops it, and the spawn swap lays a trunk entity. (An earlier
version set the flag `Custom10` on every trunk, which no slot accepts; a trunk already in a slot
then could not be moved.) Server side only; a client predicts nothing for a give. Carry On's hands are not an
inventory and are unaffected. Logging Expanded's `AttachableStorageBypass`, which lets
backpack-only trunks into entity attachment slots, registers only while its `TreeTrunkBackpackOnly`
is on, so with Carry On the pack registers every trunk code with it (`TrunkCarry.RegisterCartBypass`):
the cart slots below need it either way.

**Creative pick.** The game's "Pick block" hotkey (middle click, `pickblock`) only picks blocks.
`Game/TrunkPick.cs` adds trunk entities: the client listens for the hotkey beside the game's own
handler (`AddHotkeyListener`, never in its place) and, in creative with a trunk entity selected,
moves to the hotbar slot the game's pick would use (the active one if empty, else the first empty
one, else the active one) and asks the server on the channel `seraphhorizons:trunkpick`. The
server checks the mode again (`TrunkPockets.MayGive`) and puts a copy of the trunk's exact stack
(wood, size, branches, logs) in that slot; a survival player gets nothing. Placing that item lays a
copy down as a trunk entity (`OldTrunkBlocks`; creative does not use it up).

## Moving a trunk

**Drive** (`EntityTrunk.TryDrive` and `BeforeCollision`, `Game/TrunkDriveSeat.cs`,
`Game/TrunkPhysics.cs`, maths in `Core/TrunkDrive.cs`, geometry in `Core/TrunkPull.cs`).
Right-click a trunk with an empty hand, not sneaking (sneak is Carry On's): the player takes the end
nearer the click and is mounted on the trunk's one seat, the game's own mount machinery
(`seatable`, the trunk its seat supplier), so they move with the trunk exactly. They stand as
Cartwright's Caravan's sled pusher does: on the ground `TrunkDrive.StandOff` (0.6) blocks beyond
that end along the axis, feet at the trunk's underside, facing along the trunk over it to its far
end, pushing it from behind; afloat, at the waterline less `SwimFeetBelow` (1), swimming. Their
body is held to that heading within ±0.3 rad (`BodyYawLimits`, the sled's `bodyYawLimit`) and the
head may look a quarter turn either way (`HeadYawLimits`); the seat's angle mode is `PushYaw`, so
the view turns with the trunk. The seat (`TrunkDriveSeat.SeatPosition`) is worked out from the
trunk's pose on whichever side asks, so on the driver's client it follows the predicted trunk (the
player physics puts a mounted player there every tick). The game feeds a mounted player's movement
keys into the seat's controls on both sides (`ServerMain.HandleMoveKeyChange`,
`SystemPlayerControl`, and the mount position packet's `MountControls`), and sneak dismounts (the
game's `EntitySeat`), which is how one lets go.

The keys: **W** pushes the trunk along its axis away from the player, the far end leading, **S**
draws it back into them (they walk backwards), **A** and **D** turn it about its middle, so both
ends swing and the player goes with theirs: A steers the far end, the one ahead, to the player's
left and D to their right, as a pushed sled turns (so their own end swings the other way; a first
build turned the player's end left on A, which nobody pushing a sled expects). Turning works
standing still. There are two gaits, walk forwards and walk back
(`TrunkDrive.Gait`), and no sprint: the speed is the trunk's.

**Animations** (`TrunkDriveSeat.SuggestedAnimation`). With Cartwright's Caravan loaded
(`cartwrightscaravan`), on land, the driver plays its sled pusher's animations, which its patch
(`patches/cartanimation.json`) adds to the player's shape: `pushsled-idle` standing,
`pushsled-walk` for W (and turning), `pushsled-walkback` for S, at its sled's speeds and blend
(weight 1, average), each with `WithFpVariant` as its sled has it, so in first person the game
plays `<code>-fp` where the shape has it (`pushsled-walk-fp`; `AnimationMetaData.Init`,
`PlayerAnimationManager.StartAnimation`). They are referenced by name only; none of Cartwright's
animation data is in the pack. Without Cartwright, the player's own `idle` and `walk` (S walks
forwards too: the player has no walking-backwards animation); afloat, `swimidle` and `swim`
either way. Speeds are eased with a time constant of 0.1 s
(`EaseSeconds`), so a trunk is up to speed in about a third of a second and stops as quickly,
with no glide. On land the speed is linear in the stored logs: one log is a player's walk
(`WalkBlocksPerSecond`, 4.3 blocks a second), 48 logs or more half of it, about 3.25 at 24.
Afloat it is the same but never under three quarters of the game's raft: `EntityBoat.SeatsToMotion`
gives one paddling seat a forward speed of 2 × `PhysicsFrameTime` (1/30) = 1/15 blocks per 1/60 s
at `speedMultiplier` 1, so 4 blocks a second (`RaftBlocksPerSecond`, before the water's drag; the
raft is a little slower than that in the water), and a trunk afloat drives at 3 blocks a second at
least. The turn is a radian a second for one log, half that at 48 (afloat never under 0.75). Any
trunk can be driven; the speed is the only penalty for weight. The turn goes through the multi-box
physics' own `AdjustCollisionBoxesToYaw` with a push, as the boat turns: the swinging boxes shove
the trunk off whatever they swing into, and a turn with no way out within a block is refused.

**Who may.** While someone drives a trunk nothing else is done to it: another player's click on it
(an empty hand, a rope, sneaking, which would reach Carry On's pick-up) stops at
`EntityTrunk.OnInteract` with "Someone is already moving this trunk", a tool's hold on it is
refused with the same error and idles (`TrunkToolBehavior`), and only the player the trunk's mark
names may sit in the seat (`TrunkDriveSeat.CanMount`). The mark is the watched
`seraphhorizons:grabbedBy`, the driver's entity id (`EntityTrunk.Grabbed`, `GrabbedBy`, `DriverId`;
`Driver` and `Driven` read the seat), set just before the mount and removed when the seat empties;
the end is `seraphhorizons:driveEnd`, so a client knows where to put the player. The seatable's own
click (any empty hand would sit on any seat) is switched off through its `CanSit` event. A trunk
with a rope of the game's own tied to it is not taken: the empty-hand click goes on to the game,
which takes that rope off. A drive click stops at the trunk on the client and the server alike,
never reaching `ropetieable`. The machines' ground pull still takes a driven trunk that is driven
into their infeed cells, which is how one feeds them without Carry On; the driver is let go as the
trunk goes. Whatever removes a trunk lets its driver go (`EntityTrunk.OnEntityDespawn`), and the
server lets go of a driver who dies or is no longer playing. A player leaving the game while
driving is let go first, so their save holds no mount.

**A drive never outlives the session.** A trunk loaded with a mark forgets it in `Initialize`,
before the seatable reads its seat data, so the seat's own re-mount of the saved passenger is
refused; the seat writes its own class name (`seraphhorizons.trunkdrive`) into the player's
`mountedOn`, whose lookup (`TrunkDriveSeat.GetMountable`, registered with `RegisterMountable`) gives
the server a seat only on a trunk marked as driven, so a player saved mounted (a crash) is not put
back, and their stale `mountedOn` is removed when they join. A trunk saved by an older build whose
grab was a game rope also loses that rope's cloth id.

**Who moves it: the driver's client, with the server falling back.** The trunk is driven through
the game's own path for a mount with a controllable seat, as its boats and Cartwright's sleds
are, so the driver's keys act at once and the step-up runs where the driver sees it:

- The seat is `controllable: true`, so the seatable's `ControllingControls` are its controls, and
  its `Controller` is the driver, set by the trunk (`EntityTrunk.UpdateController`, every tick and
  at mount and unmount).
- On the driver's client, `EntityBehaviorPlayerPhysics.OnRenderFrame` sees `MountedOn.MountSupplier.Controller`
  is its player, finds the first behaviour on the trunk that is `IPhysicsTickable`
  (`seraphhorizons.trunkphysics`; `repulseagents` is not one) and ticks it at 60 Hz beside its own,
  sending the trunk's position every fourth tick (`SendPlayerMountPositionPacket`). Its
  `interpolateposition` leaves a trunk its own player controls alone.
- On the server, `PhysicsManager` skips the physics of a `PhysicsBehaviorBase` whose
  `mountableSupplier.Controller` is a living player, and `ServerUdpNetwork.HandleMountPosition`
  applies the client's packet (`Pos.SetFromPacket`, the controls `FromInt`), relays it to the other
  clients and calls `IRemotePhysics.OnReceivedClientPos` on the first behaviour implementing it.
  `EntityBehaviorTrunkPhysics` re-implements that to note the time (`EntityTrunk.ClientPositionReceived`)
  before the base works out the ground and water flags from the position, as for any entity moved
  remotely, and turns its boxes to the yaw. Other clients interpolate the relayed positions
  (`HandleRemotePhysics`, the base's, plus the boxes).
- The tick (`EntityBehaviorTrunkPhysics.Step`, the drive and step-up in `EntityTrunk.BeforeCollision`)
  is the same code on both sides, deterministic from what both have: the seat's controls, the
  pose, the stored logs (from the synced stack) and the water flags. The game's passive physics
  refuses to tick on the server for any entity with a passenger in a controllable seat
  (`IsBeingControlled`), so the behaviour re-implements `IPhysicsTickable.OnPhysicsTick` with the
  physics manager's own test (`ClientDriven`) instead.

**The server's fallback.** A player with no client to predict (Atlas's fake players) or a client that
has gone quiet would leave the trunk unticked. So the server's `Controller` is the driver only while
their client has sent the trunk's position within `EntityTrunk.ClientPositionTimeoutMs` (500 ms;
counted from the mount, so a real client has that long to start); otherwise it is none, and the
game's physics manager ticks the trunk on the server as for any undriven trunk, from the seat's
controls, building and sending its positions to every client (the driver's too, whose prediction
they then correct). The first packet from the client gives it back at once. Unmounting clears the
`Controller` and stops the trunk's horizontal motion on both sides (`EntityTrunk.DriverLeft`), so
the server's physics takes over from where the driver's client left it with no glide. The server
also stands its own copy of the driver in the seat each tick (the driver's client reports the same
place from its seat; a player with no client has only this).

**Letting go on the driver's client** (`EntityTrunk.NoteLocalControl`). A first build let the trunk
dash, on letting go, from where the drive began to where it was. While a client predicts, the
server relays the driver's mount positions to the other players only (`ServerUdpNetwork`:
`item.Player != player`), and the trunk's `interpolateposition` on the driver's client skips
applying positions while the mount `IsBeingControlled` (`PopQueue`), so its last two snapshots
(`pL`, `pN`) stay where the drive began. On letting go, control ends there, the server's physics
manager starts sending the trunk's positions to the driver too, and the first one, with a tick gap
of up to 5 (a third of a second), was eased in from that stale snapshot. Now whenever this client's
player stops being the seatable's `Controller` (letting go, in `DriverLeft`, or anything else that
takes it away, checked in the trunk's game tick), the trunk resets its interpolation to its pose
there, the predicted one: the interpolation's own teleport (`OnReceivedServerPos(isTeleport:
true)` with `tickDiff` 1: the queue emptied, both snapshots the pose now), so the trunk stays put
until the server's next position, which is eased in from where it is. The alternative, feeding the
queue the predicted pose every physics step (`PushQueue` is public), was not taken: the renderer
pops the queue at its own pace (about 15 a second) and trims it only when a server position comes
in, so 60 snapshots a second would pile up behind the pose, and it would mean a hook into the
player physics' tick; one reset at the moment control ends is all the interpolation needs, and it
covers the fallback too (while the server ticks the trunk, its positions reach the driver's client
and are applied as they come, `Pos.SetFromPacket`, but the queue is not; on letting go after a
fallback the reset is the same). What remains is the server's own lag: its position is the last
the client sent, at most four of the client's 1/60 s steps behind the predicted pose (about 0.3
blocks at a walk), and the server neither marks its first position after letting go a teleport
(nothing in the trunk sets `IsTeleport`) nor moves the trunk on from it, so the driver may see the
trunk ease back that far in about a fifteenth of a second. On the server the trunk does not move on
letting go, predicted or not (`The_server_drives_a_trunk_no_client_predicts_and_lets_a_predicting_one`,
at its end); the client's
side is not tested (no client in Atlas).

**The position check.** The server accepts a mount position only within 128 blocks of where it has
the trunk on each axis (`EntityPosExtensions.SetFromPacket`; 64 blocks a tick of motion) and
otherwise refuses it and sends its own (logged as "Rejected mount position update"). A drive moves
at most 4.3 blocks a second and a step-up lifts at most a block in a tick, so a packet (every 4/60 s)
is never near it; only a client that predicted for over half a minute unheard could be.

**Why not Cartwright's physics.** Cartwright's sled is a `controlledphysics` entity (the game's
`EntityBehaviorControlledPhysics`, with its step-up and its stepping on blocks), which collides with
one square collision box. A trunk is up to 5 blocks long, so it keeps the multi-box physics
(`passivephysicsmultibox`, a row of boxes turned with its yaw) and its own step-up (below), and takes
from Cartwright only the stance, the keys' feel and, by name, the animations.

**Why inside the physics.** The drive and the step-up run in the trunk's physics tick
(`EntityBehaviorTrunkPhysics.applyCollision`, on whichever side ticks it), after the game's drag and gravity and
before its collision. The drive sets the horizontal motion outright there, so the speed is the
collision's, whatever the ground's drag (set from the entity's game tick it would be cut by the
ground drag, 0.7 a physics tick on most blocks, and unevenly, as game ticks and the 30 Hz physics
ticks do not line up). The step-up sees that motion and lifts the trunk before the collision, in the
same tick. The grab's step-up ran from the game tick on the motion left over from the last physics
tick, which the collision had zeroed against the step, so it often saw no motion, did not lift,
and the trunk sank back off the step a few times before a lift caught: the stutter seen in play.

**Rope.** `game:rope` ties to a trunk like to any `ropetieable` entity: to a fence post, an animal
or a cart. The pull on the motion is the game's (by `Properties.Weight`), but the rope acts at an
end (`Game/TrunkRope.cs`): each tick the rope's point pinned to the trunk is moved to the end nearer
the rope's far point (the game's `ClothPoint.pinnedToOffset`, with `pinnedToOffsetStartYaw`, found
by name, set to the trunk's yaw so the game adds no turn), and on the server, while the rope's pull
moves the trunk, it turns that end to lead at the drive's turn rate (`TrunkDrive.Turn`). If those fields are gone the log
says so once and ropes pull where they were tied, without turning.

**Step up** (`EntityTrunk.StepUp`, maths in `Core/TrunkStep.cs`). `passivephysicsmultibox` has no
step-up, so inside the physics tick (above, on the driver's client or the server), a trunk whose motion (the drive's or a
rope's, at least 0.3 blocks a second) runs into a solid block within `TrunkStep.Probe` (0.15) ahead is
lifted onto it in one go, if the rise is at most one block above its underside and its boxes are
clear lifted, both ahead and where it is (no cliffs, no ceilings). Blocks count as whole cubes, so a
slab is stepped like a full block. Not afloat, not while falling. (A first build lifted a quarter
block a tick, which left the trunk hanging against the step between lifts, dropping back under
gravity and lifted again, a stutter up each step.)

**Shove.** Walking into a trunk does not move it: `repulseagents` is `movable: false` (a first build
let a walk nudge it, which a player inside the hull did too, by 0.2 blocks in a tick), and the
trunk moves only by the drive, a rope, water and gravity.

**Solid** (`Game/TrunkSolid.cs`, geometry in `Core/TrunkPush.cs`). A trunk is as good as solid to
whoever walks into it: an agent (a player, an animal) in the trunk's footprint is moved out the
shortest way, at most `TrunkPush.MaxStep` (0.25) blocks a step, and its motion into the trunk is
taken away (the part along the way out). The trunk is its oriented footprint
(`TrunkPush.Footprint`): one rectangle as long and wide as the trunk (`TrunkBox.Size`), turned
with its yaw, from its underside to its top; the agent is a vertical cylinder, a circle of half
its collision box's width about the box's middle. The way out is worked out in the trunk's own
frame (the agent's middle turned by the trunk's yaw): from beside the trunk, straight away from
the nearest point of the outline, so along a side square to the trunk's axis at the same depth all
along it, and round a corner, round it; with the middle inside, the shortest way across or along
(so from the middle it leaves across the trunk, near an end past the end); then turned back into
the world. A circle rather than the agent's box turned into the trunk's frame because the box's
corners would make the depth change with the angle between them, and a round agent meets a turned
wall the same at any yaw. A first build pushed out of the trunk's turned collision boxes
(`TrunkBoxes.Turned`), which are axis-aligned squares (the game's multi-box physics turns only
their middles), along ±x or ±z: a trunk at a cardinal yaw was a flat wall, but one turned off it
was a staircase of square corners along the diagonal, so walking along it the push jumped between
x and z and in depth, the jagged side seen in play. The blocks still collide with those boxes (the
game's physics), so a turned trunk's corners stand a little past its footprint against blocks,
not against agents. That is over
three times a walk (about 0.07 blocks per 1/60 s), so a walking player is held at the surface; one
found deep inside slides out over a few steps. The game's `repulseagents` pushes by motion, at most
0.1 blocks per 1/60 s, by the trunk's middle hitbox only (`EntityBehaviorRepulseAgents`: its push
vector clamped to ±3 and divided by 30); this works by position, on the whole trunk. An agent whose feet
are within `StandMargin` (0.4) of the top is lifted onto it the whole way, its fall stopped and
`OnGround` set, so a player can stand on a trunk. The trunk itself is never moved by this, and its
driver (mounted, and standing beyond its end) is left alone. Driven into a bystander, the same push
moves them out of its way. It runs on the server every game tick for every agent near a trunk
(`EntityTrunk.OnGameTick`), and on the client every frame for the local player, right after the
game's player physics (`TrunkSolid.ClientRenderer`, stage `Before`, order 1.05; the player physics
renders at 1): a player's position is their client's to say, so the server's push alone would be
overwritten by the client's next report, while the client's is immediate. Standing on a trunk is
this push catching the player each frame after their physics has let them fall a little into it,
not a collision the game's physics knows of, so whether it feels like standing on a block (no
jitter, jumping off it) is to be seen in play.

**Water.** Trunks float and drift (above). Afloat (`Swimming` or `FeetInLiquid`, `EntityTrunk.Afloat`,
shown as "Afloat" in the info text) a trunk drives at least at three quarters of the raft's speed
(above), and the driver floats with it, swimming. For a rope it is `TrunkPull.WaterLightening` (6)
times lighter: on the server `Properties.Weight` is set to the lighter weight each tick. The
watched `seraphhorizons:weight` (`EntityTrunk.LandWeight`, the info text's weight) stays the land
weight, so the client's `Properties.Weight` is always the land one.

## Carry On

`Game/TrunkCarry.cs`, the Carry On bridge, found by name at run time (`CarryOn.CarrySystem`'s
`CarryManager`, CarryOnLib's `CarriedBlock` and `CarrySlot`, Carry On's placement and drop
services). If any of it is not as expected there is one warning and carrying is off, as without
Carry On. The stations and machines use it through `TryGive`, `Take`, `Carried` and `HandsFull`.

- **Pick up.** Carry On's own sneak + right-click targets blocks, so the pack handles it on the
  trunk entity: sneak + right-click with an empty hand starts a hold, as Carry On's pick-up of a
  block does, and when it ends puts the trunk's stack in the player's Carry On hands slot and
  removes the entity, with the trunk's place sound (`EntityBehaviorTrunkCarry`, server side, added
  to both entity types by `patches/trunkentities-carryon.json`). The hold lasts
  `TrunkCarry.PickUpSeconds`: the Carryable's `InteractDelay` (Carry On's default 0.8 s) over Carry
  On's `InteractSpeedMultiplier` when it can be found. The server checks it every 100 ms and drops
  it, with nothing taken, when the button (`ServerControls.RightMouseDown`) or sneak is let go, a
  hand fills, the player is over 6 blocks away or looks at another entity, or the trunk is gone.
  The server sends nothing back while it runs, so the client counts the same hold itself
  (`TrunkHoldCircle`, the maths in `Core/HoldProgress.cs`): from the right-button press on a trunk
  entity, while sneaking with both hands empty, nothing carried and within 6 blocks, it fills Carry
  On's own half-circle over `PickUpSeconds` by setting `CircleProgress` on Carry On's client
  `HudOverlayRenderer` (`CarrySystem.HudOverlayRenderer`, found by name), and hides it
  (`CircleVisible` false) the moment any of that stops. Carry On's own interaction leaves the
  circle alone while it has no action of its own, and it has none for an entity or a station. The
  same tracker fills the circle for taking a trunk off a sawhorse, the Trunk Storage Rack or the
  heating rack (`StationTake`, under Stations), from the press on a station holding a trunk
  (`TrunkStations.Offer`), sneaking or not. Without that
  renderer, one warning and no circle. Hands already full: the error "Your hands are full. Put
  down what you are carrying first." (`trunkentities-hands-full`). An item in either hand (the
  offhand too): "Empty both hands first." (`trunkentities-hands-not-empty`), and the trunk stays
  where it was. `TryGive` refuses it for every way into the hands (pick-up, a station, the rosser
  and mill's Ctrl, a cart), because Carry On locks both hand slots while carrying and its client
  starts no carry action, a put-down included, unless both are empty: a trunk taken with something
  in the offhand could never be put down. The carried stack gets a small block entity tree
  (`blockCode`, `type`), because Carry On attaches a carried block to a cart only with block entity
  data.
- **Carried as its class's model.** Carry On draws (and animates) the block of the stack it
  carries, so what goes into its hands is not the trunk's own block but the one of its class's
  model (`TrunkCarry.Shown`, by `Trunks.ShownBlock`): Logging Expanded's `lg` for a thin trunk and
  `xxl` for a thick one, debarked when the trunk is, no branches; only those two models are ever
  seen in hand, as on the ground and in the machines. The stack keeps the trunk's attributes (its
  logs) and the real block code under `seraphhorizons:trunkCode`; everything that reads the hands
  (`Carried`, `Take`, put-down, drops, the back check, the speed, the stations and machines) gets
  the real trunk back (`TrunkCarry.Real`), so a 1-log `xs` trunk is carried as an `lg` and put down
  as the same 1-log `xs`. A cart slot holds the shown stack as Carry On attached it (and shows the
  class's model too); taken back off, it is unwrapped and wrapped again. A trunk that reaches the
  hands as its own block some other way (Carry On's own pick-up, a cart slot filled before) is
  swapped for its shown stack by the `SetCarried` postfix below.
- **Speed.** While a trunk is carried, the player's `walkspeed` stat gets the code
  `seraphhorizons:trunk`, so the walk speed is `TrunkWeight.CarrySpeed` of its logs:
  `CarrySpeedAtOneLog` (1, a normal walk) for a trunk of 1 log, falling linearly to
  `CarrySpeedAtMaxLogs` (0.5) at 48 logs and beyond. About 0.90 at 10 logs and 0.74 at 25. Carry
  On's own slot modifier is set to 0 for trunks by the patch and cancelled out in the value
  besides. The server checks every online player every 250 ms (and at once when the pack itself
  gives or takes a trunk) and removes the code once no trunk is carried; the game syncs stats to
  the client. Atlas picks a 48-log trunk up the real way (the hold, server side) and finds the
  blended `walkspeed`, and the player's whole walk multiplier, at 0.5, with no code but the game's
  and this one: no `carryon:Hands` at all. A much slower walk seen in game (about 5% with a
  48-log trunk, the stat reading 0.5) is therefore not a second stat on the server; it was not
  reproduced headless, and if it recurs it is to be looked for on the client, with the probe
  under Troubleshooting below.
- **Animation and pose.** A second `Carryable` on `loggingmod:blocktypes/treetrunk`
  (`patches/trunkentities-carryon.json`), which Carry On merges into Logging Expanded's own
  (`patchPriority` 1 with `overrideExistingProperties`, so the order of the two patches does not
  matter): slot `Hands` with Logging Expanded's `trunkcarry` animation for xs, sm, md and lg and
  `trunkcarryheavy` for xl and xxl, matched on the carried block's code, so with only `lg` and
  `xxl` carried they go by class, thin and thick; and `walkSpeedModifier` 0. Those two are Logging
  Expanded's animations for a trunk held as an item (`heldTpIdleAnimationByType`, its player
  patches add both); its own Carry On patch gives the trunk a bare `Hands` slot, so with Logging
  Expanded alone a carried trunk plays Carry On's `holdheavy`. **No transform**: no
  `transformTemplates`, no `transformGroups`, no `translation` or `rotation`, so the trunk gets Carry
  On's default block transform (`BlockBehaviorCarryable.DefaultBlockTransform`: scale 0.5 about the
  block's centre, nothing else), as with Logging Expanded alone. Carry On's renderer
  (`CarryRenderDispatcher.RenderCarried`) puts it, in third person, on its `carryon:FrontCarry`
  attachment point, which hangs on the left forearm (its `HandL` element under `LowerArmL`), plus
  the hands offset (−0.3, −0.6, −0.5); in first person (not immersive) on
  `GetFirstPersonHandsMatrix`, a frame from the camera that ends turned 90° about y, so the trunk
  block, which lies along its z, lies across the view; the transform is applied after either
  (`CarryTransformResolver.ApplyTransformInPlace`: translate, origin, rotate x, z, y, scale,
  −origin). The pack had a transform before (Carry On's `carry-trunk` template, the game's chest
  carried across the front; then a `rotationX` of 90 for a trunk wrongly taken to stand upright,
  with `translation` [0.35, 0.6, −0.5]), which stood it on end in the middle of a first-person view
  and lifted it off the shoulder in third. Two ways the pose can still differ from what a player
  saw with Logging Expanded alone: the animation (`trunkcarry` raises the left forearm, and with it
  the attachment point, to the shoulder, where `holdheavy` holds it in front), and the size shown.
  The shapes do not share a centre: `xs`, `sm` and `md` run from z 0 to 16, 32 and 48 (in 1/16
  block), `lg` from −32 to 32 and `xxl` from −32 to 48, and the thick ones are two blocks wide and
  high (x 0 to 31, y 0 to 30) where the thin are one. At half scale about the block's centre a
  thin trunk is therefore always drawn as `lg` is, 0.25 to 0.75 of a block further back along its
  length than a vanilla `xs` to `md` would be, and a thick one as `xxl`, reaching up and out to one
  side from the same underside. Neither is a reason for a transform of its own: an `lg` carried
  with Logging Expanded alone sits the same. If the trunk proves to sit a little high or low on the
  shoulder, a `transformGroups` `hands` root with only a translation (e.g. `{ "id": "root",
  "translationY": -0.1 }`) in the patch's `properties` moves it, in block units of the hands frame
  before the half scale; and then the animation scenario's no-groups check goes. The game merges
  `propertiesByType` into `properties` with arrays concatenated, so the shared settings sit in
  `properties` only.
- **Hands only, never the back.** The patch sets `preventSwapBack`, and `StripBackSlots` removes any
  `Back` slot from a trunk's Carryables in code (`HasBackSlot` checks it). Whatever still puts a
  trunk on a back (Carry On's swap key, an old save) has it laid down at the player's feet as a
  trunk entity (`EvictFromBack`): from a Harmony postfix on every concrete
  `ICarryManager.SetCarried`, and from the 250 ms speed check. Without a `SetCarried` to patch, one
  warning, and only the periodic check does it.
- **Put down.** Carry On's place-down of a carried trunk (`CarryPlacementService.TryPlaceDown`,
  prefixed on both sides: the client predicts, then asks) never places a block. The server checks
  Carry On's permission for the cell, then lays a trunk entity in the cell Carry On chose, along the
  player's view, reaching away from them; the hands are emptied on both sides. If the entity cannot
  be spawned the trunk stays carried, with a warning in the log. The prefix only changes what the
  place-down does once it runs: Carry On's client calls it at the end of its own put-down hold
  (`CarryInteractionStateMachine`, sneak + right-click held for the Carryable's `InteractDelay`,
  with its own filling circle), so a trunk's put-down has the same delay and circle as any block's.
- **Drops.** When Carry On drops a carried block (death, damage, a quick drop, its own
  carried-block entity: `CarryDropService.DropCarriedBlock` and `DropBlockAsEntityOrItem`), a trunk
  is laid as a trunk entity where the carrier stands, never a block nor an item. If the entity
  cannot be spawned (a warning in the log), the trunk is left in the hands and Carry On's own drop
  goes on, so it is dropped as Carry On drops any block and never simply lost.
- **Racks are not carried.** Logging Expanded's `patches/carryon.json` gives its Trunk Storage Rack a
  Carryable, and a carried rack takes its four trunks with it. In `AssetsFinalize` the pack strips
  Carry On's `BlockBehaviorCarryable` from every `loggingmod:trunkstorage-*` block
  (`StripRacks`): in code, after Carry On's own asset pass merges and maps Carryables, rather than a
  JSON patch racing Logging Expanded's. The heating rack and stick storage keep theirs.
- **Carts and sleds.** Cartwright's Caravan ships Carry On's `attachablecarryable` on its cart and
  sled disabled, but Carry On 2.0.0-pre.8's own `patches/carryonmore/cartwrightscaravan.json` adds
  it to both entity types' server and client behaviour lists, so a carried trunk goes into a cart's
  or sled's storage slot by Carry On's attach; the feature adds nothing to the carts.
  How it looks there is Logging Expanded's existing patch (the `treetrunk-xs-cart` shape per slot).
  Taking it off by Carry On's own key puts it in the hands; so does the game's empty-hand take from
  an attachment slot (`EntityBehaviorAttachable.TryRemoveAttachment`, prefixed: a trunk fits no
  inventory), refused with the hands-full error while they are full and for a cart someone else
  owns. If that method is gone, one warning, and only Carry On's key takes a trunk off. Carry On's
  cart leftovers on the stack (`backpack`, `carryonbackup`, an empty `type`) are removed whenever a
  trunk leaves the hands.
- **Troubleshooting the walk speed.** Two probes print everything that goes into the player's
  movement speed at that moment (`Game/TrunkSpeedProbe.cs`): the client command `.trunkspeed` (no
  privilege) prints the client's view in its chat and the client log, and `/sh trunkspeed` (chat
  privilege, the caller's own player) the server's, in chat and the server log. Each prints every
  `walkspeed` stat entry (code, value, weight, persistent; the pack's and Carry On's marked) and
  `Stats.GetBlended("walkspeed")`, `EntityPlayer.walkSpeed`, `Controls` and `ServerControls`
  (`MovespeedMultiplier`, sneak, sprint, trying to move, flying, the walk vector's length), the
  player's `MoveSpeedMultiplier` and game mode, `OnGround`, `FeetInLiquid`, `PrevFrameCanStandUp`,
  the game's speed constants, the block under the feet and the one at them with their
  `WalkSpeedMultiplier`, `GetWalkSpeedMultiplier(0.3)` with and without the move speed multiplier,
  what Carry On holds in hands (the shown block, and the real trunk's logs and the pack's speed for
  it) and on the back, every `WatchedAttributes` and `Properties.Attributes` entry whose key
  contains "speed", and both hands' items. The game moves a player on the ground by
  `Controls.WalkVector` (base speed × `MovespeedMultiplier`) × `GetWalkSpeedMultiplier`, which is
  the sneak and sprint factors (from the server controls) × the two blocks' `WalkSpeedMultiplier`
  (÷ 2.5 in liquid; the blocks left out in creative) × `walkSpeed` × the sneak factor again when
  the player cannot stand up (`PrevFrameCanStandUp` false). Compare the client's dump with the
  server's while walking with a heavy trunk. A driver (mounted on a trunk's `TrunkDriveSeat`) does
  not walk at all: the seat carries them, and the trunk moves at its own speed. So for a driver
  the probe also prints the driven trunk: its stack's code, stored logs, class, the end taken,
  afloat (and why), `TrunkDrive.Speed(logs, afloat)` and `Turn` with the share they come from, the
  seat's keys, the drive's eased speed and turn on that side (0 on the side that does not tick the
  trunk), the trunk's motion, and who ticks its physics. A carried trunk's walk speed comes from
  `CarrySpeedAtOneLog` and `CarrySpeedAtMaxLogs` (Settings); a driven one's from `TrunkDrive`'s
  constants, which no setting changes. The client's chat copy has its braces doubled, since the
  client shows a command's reply through `Lang.Get`, which formats it (a lone brace logs an
  "Expected an ASCII digit" error); the server sends a reply of more than one line as it is, and
  the logged copies are unchanged.
- **Clicks while carrying.** Carry On lets a right-click through to a block while something is
  carried only if the block has its `CarryableInteract` behaviour, after its short hold (0.8 s by
  default, unless Carry On's `RemoveInteractDelayWhileCarrying` is on), and sends it on to the server.
  `patches/trunkentities-stations.json` adds `CarryableInteract`, for a carried `BlockTreeTrunk` only,
  to Logging Expanded's three sawhorses, Trunk Storage Rack and heating rack, and to the rosser's
  and bucking mill's frame and ghosts. Without it a carried trunk could not reach a station.

## Tools where it lies

Every knife, shears, axe and saw (by its tool type) and Immersive Woodworking's bark spud (by its
code, as `Woodworking/BarkDrops.cs` knows it) gets a `TrunkToolBehavior` in `AssetsFinalize`, on
both sides (`Game/TrunkToolsSystem.cs`). Some tools' classes override the held interaction without
calling their behaviours (the game's knife does in its steps), so each such override on those
classes is prefixed to run the behaviour first; the server log says how many tools and overrides.

A right-click hold on a trunk entity with one of them is taken at its start, steps until its time,
and does the work when it is let go at that time (less Logging Expanded's 0.1 s); let go early,
cancelled, or looked away from on the client, it does nothing. A refused hold says why (Logging
Expanded's `treetrunk-branches-first`, or `rosser-error-already-debarked`) and idles until let go.
After a completed hold the client waits 300 ms before starting the next, so a held button repeats
the work at a pace, as Logging Expanded's own harvest hold does. The client shows the held item's
swing and the game's progress bar; the spud plays Immersive Woodworking's debarking animation. A
hold that does not apply (a knife on a clean trunk) is not taken and the tool keeps its other uses.

The rules are Logging Expanded's for a placed trunk (`BlockTreeTrunk.OnBlockInteractStart/Stop`,
0.3.6), its settings and hooks read through `LoggingBridge` (`Game/TrunkHarvest.cs`):

| Tool | Hold | Needs | Does |
|---|---|---|---|
| Knife | 2 s | branches | Cuts min(branches, 12): that many sticks through Logging Expanded's `StickYieldModifier` hook, times the player's `stickDropRate`; at none left the trunk becomes the clean (`no`) one. The knife loses as much as branches came off. |
| Shears | 2 s | 12 branches or more | 12 branches for the wood's `game:sapling-{wood}-free`, a second on Logging Expanded's `BonusSaplingRoll` hook; 1 durability. At none left the trunk becomes the clean one (Logging Expanded leaves a placed trunk branchy with a count of none). |
| Axe | 0.75 s | no branches while `RequireBranchRemovalForProcessing` | Takes one log off, two while it holds two or more (Logging Expanded decrements twice), for `TreeTrunkLogYield` (1) of the wood's placed log; with a hammer in the offhand `TreeTrunkDebarkYield` (1) of its debarked log. A debarked trunk gives the debarked log at the plain yield, as a placed one does under the `Rosser` switch. 1 durability. |
| Saw | 0.75 s | as the axe | One log off for `TreeTrunkPlankYield` (6) of the wood's planks (`TreeManager.GetPlankCode`); 1 durability. |
| Bark spud | logs × `SpudSecondsPerLog` (0.5), 2 s at least | no branches counted, not debarked, the debarked trunk existing | The whole trunk becomes the debarked trunk (`Trunks.Debark`, the stored logs marked as the rosser marks them). Every stored log rolls Immersive Woodworking's bark once (`BarkDrops.Roll` with the spud's chance multiplier, dry, as the rosser rolls a dry log), the drops merged by kind. The spud loses Immersive Woodworking's `DebarkDurabilityPerLog` (1) per log, and plays one of its debarking sounds. |

The spud's hold is refused with "branches first" while the trunk has branches counted, whatever
`RequireBranchRemovalForProcessing` says: the rosser's limb breaker takes branches, a spud does
not. A 4-log trunk takes 2 s, a 10-log one 5 s, a 48-log one 24 s. What a tool makes is thrown
toward the player from the trunk's nearest point, as Logging Expanded throws a placed trunk's. The
sounds are Logging Expanded's (leaves for the knife and shears, wood for the axe at 0.75 volume,
the saw's), played by the server for everyone. A trunk whose last log is taken goes. A log taken
re-sizes the trunk as Logging Expanded sizes one it picks up (`BlockTreeTrunk.GetSizeClass`, here
`TrunkCode.SizeFor`: xs up to 3 logs, sm 8, md 15, lg 24, xl 35, xxl beyond), its wood, branches
state (debarked too) and side kept (`TrunkHarvest.WithLogs`), so an xl trunk of 25 logs sawn to 24
is an lg trunk, and a thin trunk entity, with the thin box, carry animation and machine class.

## Stations and machines

**Logging Expanded's stations** (`Game/TrunkStations.cs`, applied by `Game/TrunkStationsSystem.cs`
on both sides while trunk entities run with Carry On, under their own Harmony id). A prefix on each
block's `OnBlockInteractStart`, found by name: the sawhorses' `BlockWorkstation`, the Trunk Storage
Rack's `BlockTrunkStorage` and the heating rack's `BlockResinRack`.

- **Loading.** Carrying a trunk, hold right-click on the station (Carry On's short hold, above): the
  station takes it if it can, with a wood sound, and the hands are emptied. A sawhorse takes it when
  empty, and debranched while `RequireBranchRemovalForProcessing` holds (else Logging Expanded's
  "branches first" message); the rack while it holds fewer than four and its own `CanStoreTrunk`
  agrees; the heating rack when empty, and debranched under the same rule, as its own empty-hand load
  asks. A station that cannot take it does nothing, so a carried trunk never makes it unload. The
  sawhorse loads the trunk's stored log stack, as Logging Expanded's own load does.
- **Unloading.** With an empty hand and nothing carried, holding right-click on a station that
  would give a trunk back takes it into the hands at the end of a hold, as a pick-up off the ground
  is: the sawhorse's unload stack when it is a trunk (`BuildUnloadStack`; one loaded with logs or
  firewood gives those back at once, as Logging Expanded does), the rack's top trunk, the heating
  rack's trunk (`TrunkStations.Offer` says which, on either side). Carry On's own delay never
  applies here: its `CarryableInteract` hold runs only while something is carried, and the
  stations' own Carryables (the sawhorses', the heating rack's) are for carrying the station. So
  the server times it itself (`StationTake` in `TrunkStations.cs`, one for all three: the hold
  takes the station's take, `TrunkStations.TakeInto`), for `TrunkCarry.PickUpSeconds` of the trunk
  taken, checked every 100 ms. The hold breaks, leaving the trunk on the station, when the button is
  let go, a hand fills, something is carried, the player is over 6 blocks away or looks at another
  block, or the station no longer holds a trunk. While the hands are full, the error at the click,
  and no hold; should they fill at the very end, the error, and the trunk stays (the heating rack's
  is stored back, its retrieve having written its state into the stack). The client fills Carry
  On's circle for it (`TrunkHoldCircle`, under Pick up above).
- Anything else (a tool, logs, a knife on the heating rack, Carry On's own sneak clicks) is the
  original's. The client's prefix only says a carried trunk's click is the station's.

If a station's type or members are not found, that station is left as it ships, with one warning
naming what is missing; the server log says how many of the three were patched.

**The rosser and the bucking mill** (`../Rosser/Game/BERosser.cs`,
`../BuckingSawmill/Game/BEBuckingMill.cs`) do the same in their own block entities:

- **Loading by hand.** With an empty hand, a right-click loads the trunk carried in Carry On's
  hands under each machine's own rules (the rosser refuses a debarked or empty trunk and any while
  incomplete or occupied; the mill refuses a branched one while the branch rule holds, and loads
  only with its saws at the top, keeping a held click until they come up). With trunk entities and
  no Carry On, or without trunk entities, it is the old load from the hand, hotbar or backpack.
- **Ctrl + right-click** takes the trunk back into the hands; while they are full, the hands-full
  error and the trunk stays on the machine. With trunk entities but no Carry On, the trunk is laid on
  the ground as an entity two cells beyond the middle of the infeed cells, across the machine's line,
  so the machine does not take it straight back (`TrunkStations.DropBeyond`).
- **The ground pull.** Each machine's infeed cells are where it takes a rack's top trunk: the three
  ground cells just beyond its infeed end (`InfeedNeighbours` of each rig). A trunk entity lying there
  is taken as a rack's trunk is, under the same gates, at the same moments (the rosser's
  once-a-second rack poll; the mill every tick its saws are at or pass the top, and once a second):
  the machine empty, complete and turning at `MinSpeed`, `AutoPullFromRack` on, and the trunk one it
  takes (the rosser: not debarked, with logs; the mill: not branched while the branch rule holds, with
  logs). A trunk counts as in a cell by its middle's column, at its underside's height
  (`TrunkStations.FindInCells`, floor of x and z, floor of y + 0.5): a trunk resting on the floor of a
  cell is in it however long it is, and one whose middle lies outside the three cells is not. **A rack,
  or for the mill a feeder (a rosser in line), comes first**: the ground is looked at only when none
  of them has a trunk ready (`PullFromRack` falls through to `PullFromGround`). The entity is removed
  and its stack goes on unchanged. The block info speaks only of racks and feeders.

## Old worlds

Placed trunk multiblocks, from before trunk entities ran, are **deleted** as they load, with nothing
given back and no migration (`Game/OldTrunkBlocks.cs`): a Harmony postfix on Logging Expanded's
`BETreeTrunk.Initialize` (server side, while the feature runs) queues the controller's removal for
the next tick, the game's multiblock behaviour takes the filler blocks with it, and each removal is
logged. That is the pack's call: such trunks were few, and a trunk can no longer be placed. If the
block entity type is not found, one warning, and placed trunks are left as they are.

Trunk item entities lying in a world are swapped as they load (above). A trunk in an inventory from
before (a hotbar, a backpack, a chest) is an ordinary stack again: it moves to any slot that takes
Logging Expanded's own flag, and thrown out it becomes a trunk entity, which no survival player can
pick back up. (Under the earlier `Custom10` flag such a trunk could not be moved at all.) Placed from a hotbar, it is laid down as a
trunk entity in the cell the block would have gone into, along the player's view, and never placed:
a Harmony prefix on Logging Expanded's `BlockTreeTrunk.TryPlaceBlock` (both sides, patched once per
process, while the feature runs; the client only answers yes) spawns the entity and says the block
went down, so the game takes the stack from the hotbar as for any placed block. Carry On's
put-down also ends in `TryPlaceBlock`, but its own place-down is intercepted first (above). If the
method is not found, one warning; the trunk is then placed as a block and the postfix above deletes
it the next tick.

## Config

`TrunkEntities` (bool, default true) switches the feature; `TrunkEntitiesSettings` in
`ModConfig/seraphhorizons.json` (`Core/TrunkEntityConfig.cs`) holds its figures, next to `Rosser`.
Values out of range fall back to the default with a warning. The server's values are used.

| Setting | Default | Range | |
|---|---|---|---|
| `WeightPerLog` | 8 | 0..1000 | Weight a stored log adds: weight = 10 + logs × this. What a rope pulls against (the drive goes by logs). |
| `CarrySpeedAtOneLog` | 1 | 0..1 | Walk speed, as a multiple of the normal one, carrying a trunk of 1 log (1 is a normal walk) |
| `CarrySpeedAtMaxLogs` | 0.5 | 0..1 | Walk speed carrying one of 48 logs or more; linear in logs between the two |
| `SpudSecondsPerLog` | 0.5 | 0..60 | The bark spud's hold per stored log, 2 s at least |

The drive's figures are constants in `Core/TrunkDrive.cs`, not settings. (The grab's `GrabRange`
and `MaxGrabWeight` are gone: any trunk can be driven, and a config that still names them is
read without them.)

**Who decides.** The classes (the entity, the pick-up behaviour, the renderer) are registered on
both sides whatever the setting, so both entity types always exist. The server decides in `Start`
(the switch on, Logging Expanded installed, `LoggingBridge` resolving) and writes it to the world
config (`seraphhorizons:trunkEntities`), which the game sends a client before it starts its mods; a
client follows that, whatever its own setting (a mismatch logs one notification), as
`UnifiedWoodworking` does. **Off**, nothing runs: no spawn swap, no storage flag, no drive, no tool
behaviour, no station or machine change, no deletion, and the feature's three patch files are emptied
before the game's patch loader runs (`carryon` and `carts` by `TrunkEntitySystem.DisablePatches`,
`stations` by `TrunkStationsSystem`, which also empties it when Carry On is missing). Logging
Expanded's trunks are then items, as it ships them. Trunk entities already in a world (and any
spawned) turn back into the trunk items they hold, the tick after they load: the spawn swap reversed
(`TrunkEntitySystem.Unswap`, server side, with Logging Expanded installed; each one logged).

## Tests

- `tests/TrunkEntities/TrunkPullTests.cs` checks the geometry (`TrunkPull`: the ends as
  `TrunkBoxes.Turned` turns the trunk, the nearer end, the yaw facing an end along a line, the
  short way round and capped, the wrap), the rope's lighter weight afloat (`EffectiveWeight`) and
  the step-up (`TrunkStep.Lift`: lifts onto a one-block rise in one go, finishes a rise left part
  way, stops on top; none on flat ground, without motion, up a two-block cliff, under a ceiling or
  moving away). `tests/TrunkEntities/TrunkDriveTests.cs` checks the drive (`TrunkDrive`): the land
  speed linear from a walk at one log to half at 48 and never rising, afloat never under three
  quarters of the raft, the turn rates, the keys' speed and turn, A swinging the driver's end to
  their left and the driver looking over the middle, the stand just beyond the taken end with W
  pushing the trunk away from it (the way the driver faces) and S back into it, the gaits (walk,
  walk back, idle; turning on the spot walks), and the ease up to speed in a few tenths of a second
  without overshooting.
  `tests/TrunkEntities/TrunkPushTests.cs` checks the solidity's geometry (`TrunkPush`): nothing
  outside or touching (nor diagonally off a corner, where a box's corner would touch), across
  rather than along from the middle (thin and thick), past the end near an end, straight away from
  a grazed side and diagonally from a grazed corner, up onto the top only within the margin, capped
  steps getting out in a few, motion into the trunk stopped (on a slanted wall only the part into
  it), a quarter-turned trunk lying along x, and a trunk at yaw 0.6: along its whole side, sampled
  every 0.05, the way out within 5° of square to its axis and the depth the same (no staircase:
  consecutive samples within 0.05), out across from its middle and along near an end, and clear
  once moved the whole way. The rope's end pin and turn have no test.

- `tests/TrunkEntities/TrunkEntityCoreTests.cs` (part of the mod's unit tests,
  `dotnet test mods-src/seraphhorizons/tests`, no game needed) compiles `Core/` and tests the
  documented defaults, out-of-range values falling back and edge values kept; the weight (10 + 8 per
  log, and following the setting); the carry speed, linear from 4 to 48 logs and never rising; the
  spud's hold (half a second a log, 2 s at least); the boxes
  per class (four cubes, four overlapping 2 × 2 × 2 cubes, none for none); the radius; and the
  turned boxes at yaw 0, a half turn and a quarter turn (laid along x, a thin trunk one block wide
  and a thick one 5 × 2), their middles kept at their distance.
- The Atlas scenarios (`tests/PackTests`) load this build with every locked mod, Carry On included,
  and the pack's default settings; each works on a floor of its own high in the sky. A player is a
  fake one whose clicks reach the server through the entity's or the item's own interaction methods.
  - `TrunkEntityScenarios.cs`: the feature runs with the world config key, Logging Expanded's own storage
    flag and both types; a trunk item spawned as felling spawns it (`SpawnItemEntity`) becomes a
    thin trunk entity with its stack, branches and info, and a thick one for xl; spawned at a
    survival player's feet it becomes a trunk entity and nothing is collected; `TryGiveItemstack`
    refuses a trunk to a survival player (other stacks still go in) and gives it to a creative one;
    a trunk set straight into a hotbar slot moves to another slot that holds it and, dropped, lies
    there as a trunk entity;
    the weight follows the logs per entity and leaves the type's alone, and no logs removes it; a
    trunk dropped from a height rests on the ground; a placed trunk multiblock is removed when it
    loads, nothing dropped; a trunk left in a hotbar, placed through the game's own placement, is
    taken from the hotbar and lies there as a trunk entity, with no block; and the drive: a trunk
    as a world saves it mid-drive and from when the grab was a game rope (the driver's and rope's
    ids, the rope in its `ropetieable` list, no such rope in the game) loads with the mark and the
    rope's id cleared; an empty hand at an end then mounts the player on the trunk's seat by that
    end, with no rope of any kind, standing 0.6 beyond it at the trunk's height; the driver's own
    clicks change nothing; the seat's sneak lets go and clears the mark. Driven (fake players' keys
    set on their seat's controls, as the game feeds them, once the server has taken over from the
    client that never sends): the driver faces the far end; W pushes the trunk away from the taken
    end with the driver still just beyond it, S draws it back, A and D turn it both ways with its
    middle staying put and the driver at their end; another player's empty hand, their sneak hold
    (Carry On's pick-up) and a mount of the seat are all refused, and once let go the other player
    can take it by the other end. A 1-log trunk driven with W goes 1.6 to 2.4 times as far as a
    48-log one in the same time; a driven trunk climbs a one-block step; a 48-log trunk driven in a
    pool goes over 1.2 times as far as on land. The prediction path: the seat is controllable and
    its controls are the seatable's `ControllingControls`; W pressed right at the mount moves the
    trunk within a second (the server's fallback after the client's 500 ms grace), with no
    `Controller`, and at the drive's speed within 20 % (ticked once, not twice); then the test plays
    the driver's client (two `Step`s of 1/60 s and an `OnReceivedClientPos` each server tick, with S)
    and the trunk moves as those steps alone give, within 20 %, the driver its `Controller` and the
    driver still at their end; once those stop, the server ticks it again within the timeout; and
    letting go clears the `Controller` and leaves the trunk at rest. Letting go of a trunk driven
    with W, first predicted (the played client) and then ticked by the server (the fallback), does
    not move it on the server: no step over 0.3 blocks between ticks, and no more than that from
    where it was let go, over the 15 ticks after. A fake player teleported into a thin trunk's
    middle is out of its footprint within 20 ticks, on the near side, and the trunk has not moved.
  - `TrunkToolScenarios.cs` (a partial file of `SharedWorldScenarios`, on the plain world): every tool kind gets the behaviour; the axe takes a log, with a hammer
    a debarked log; the knife cuts sticks and leaves a clean trunk; shears make a sapling from
    twelve branches; the saw cuts planks; the axe and saw refuse a branched trunk; the spud debarks
    a clean trunk in one hold and drops bark; an xl trunk sawn to 24 logs becomes a thin lg trunk
    entity in its place; a trunk at its last log is gone after the axe.
  - `TrunkCarryScenarios.cs`: carrying runs; a carried trunk has Logging Expanded's animation by
    size and Carry On's default pose (no templates, no transform groups, no translation or
    rotation); Trunk Storage Racks cannot be carried; a trunk given goes into empty hands and back out;
    an item in either hand refuses it; carrying slows by the logs and the speed goes when it is put
    away; `/sh trunkspeed` prints the pack's `walkspeed` entry, the blend, the walk multiplier and
    the trunk carried; sneak-clicking a trunk entity starts a hold and shoulders it only when the hold ends
    (hands full: refused at once); letting go early, or the trunk going, ends the hold with nothing
    taken; no trunk block has a Back slot; a trunk put on the back is laid down beside the player
    as a trunk entity; putting it down lays a trunk entity and no
    block; a dropped carried trunk lands as a trunk entity; a cart takes a carried trunk and gives
    it back to the hands.
  - `TrunkStationScenarios.cs` (on the woodworking world, `WoodworkingScenarios`, where trunk
    entities run with Carry On): a running rosser, and a running mill with a debranched trunk, take a
    trunk entity from their infeed cells only; Ctrl on the mill puts the trunk in the hands and full
    hands refuse; the sawhorse, the Trunk Storage Rack and the heating rack load from the hands and
    unload into them after the pick-up hold (at least 0.5 s), each letting go early leaving the
    trunk where it was.
  - The rosser's, the mill's and the debarked trunk's own scenarios (`RosserScenarios.cs`,
    `BuckingSawmillScenarios.cs`, `DebarkedTrunkScenarios.cs`) run with trunk entities on and hand
    their trunks over through Carry On's hands.

```sh
dotnet test mods-src/seraphhorizons/tests --filter "FullyQualifiedName~TrunkEntities"
TMPDIR=~/.cache/atlas-tmp VINTAGE_STORY=<game> dotnet test tests/PackTests \
  --filter "FullyQualifiedName~TrunkEntityScenarios|FullyQualifiedName~TrunkCarryScenarios|FullyQualifiedName~TrunkStationScenarios"
# The tool scenarios share SharedWorldScenarios' server: run that class, or just its trunk tests
TMPDIR=~/.cache/atlas-tmp VINTAGE_STORY=<game> dotnet test tests/PackTests \
  --filter "FullyQualifiedName~SharedWorldScenarios&(FullyQualifiedName~trunk|FullyQualifiedName~Every_tool_kind|FullyQualifiedName~Shears_make|FullyQualifiedName~A_saw_cuts_a_log)"
```

With the switch off (`fixtures/switches-off`, `TrunkEntities: false`) the other switches' scenarios
run on Logging Expanded's trunks as items, and `SwitchesOffScenarios` checks the feature off: the
world config key false, carrying unavailable, the trunks' storage flag Logging Expanded's (backpack
only), the Trunk Storage Rack keeping its Carryable, Cartwright's carts and sleds with no more than
Carry On's own `attachablecarryable`, no tool behaviour on an axe, knife or saw, nothing patched
under the feature's Harmony ids, no pick-up behaviour on the entity types, a trunk item staying an
item, and a trunk entity turning back into the trunk item it holds.

## Known limits, and what is not checked in the game

Nothing client-side has been run in the game. Atlas runs a server only, so these are compiled but
unseen: the renderer (the trunk's place, turn and texture against its boxes, the debarked look, the
shadow pass), the client's picking along the turned boxes, the interaction help, the tools' swing,
progress bar and the spud's animation, the client's repeat pause, Carry On's client prediction of
a put-down (the same patched method, which a server cannot run), the carry animations and the
pose with Carry On's default transform (see Animation and pose above; whether a slight
`translationY` would sit the trunk better on the shoulder is not checked, and the same section says
how to add one), the client side of the 5 % walk with a heavy trunk (`.trunkspeed` is there to
find it), how the pick-up hold feels
with no progress ring and whether a real client's held button reaches the server for its whole
length, Carry On's swap key with a trunk in the hands, and everything of the drive a client shows: the driver's place,
facing and animations (Cartwright's `pushsled-*` poses on a trunk: its arms are posed for the
sled's handle, which may sit wrong against a trunk's end, and whether its idle and walk-back
first-person variants, which its patch does not define, cause any trouble), the view turning with
the trunk, the body and head limits, sneak letting go on a real client; and the driver's client's
prediction itself (Atlas plays it by calling the same tick on the server): whether it is smooth,
whether the server's positions during the 500 ms after a mount (none) or after a quiet spell
(the fallback's, which correct the prediction) show as a jump, whether letting go is now free of the
dash (the interpolation reset, above) and how the server's lag of up to 0.3 blocks shows, a trunk and driver as other players
see them, a player standing on a trunk someone else drives, and the drive on a dedicated server
with real latency; and the
client's half of the solidity (walking into a trunk, standing on one, jumping off it). Before release, play
through:

- felling a real tree (Atlas spawns the trunk item as Logging Expanded's felling does, not by
  felling), and the trunk's look and rest on uneven ground and slopes;
- floating and drifting in still and flowing water (buoyancy is not in any scenario);
- a rope item tied to a trunk and to a post, an animal and a cart (the game's, but untested here),
  and shoving by walking into a trunk;
- driving a thin and a thick 48-log trunk on flat ground, up a step, into a wall, across a slope
  and in a river, and whether the speeds, the turn and the ease feel right (the constants in
  `TrunkDrive` are first guesses; the swimming driver's height, `SwimFeetBelow`, is a guess; the
  walk animations' speed is Cartwright's, not scaled by the trunk's);
  whether the server's copy of the driver, stood in the seat each tick, ever fights the driver's
  client's reports (others see the driver at the seat of their own trunk, so it should not show);
  a trunk turned by a rope against a wall can swing its boxes into it;
- the 0.8 s Carry On hold before a station, rosser or mill takes a carried trunk;
- a carried trunk on a sled, and taking one off a cart with an empty hand from a real client;
- a trunk lying at an angle (its collision is a staircase of axis-aligned boxes, above).

Also not checked: the axe on a debarked trunk without a hammer (debarked logs, by reading; the
placed-trunk scenario for it went with placed trunks), Carry On's death, damage and quick drops (one
dropped-trunk scenario covers the drop path), the heating rack's hands-full put-back, and the
feature without Carry On or without Immersive Woodworking: the pack always loads both, so Atlas
never sees those paths.

Known compromises:

1. **Display size.** Every thin trunk is shown, boxed and driven as a 4-block `lg` trunk and every
   thick one as a 5-block `xxl`, whatever its own length, as the machines show them.
2. **The ground pull finds a trunk by its middle.** A trunk lying across the infeed cells with its
   middle outside them is not taken; one needs to lie in line with them.
3. **The handbook describes trunk entities.** The woodworking guide and the machines' handbook pages
   say how trunks are moved and loaded with the feature on, and are not rewritten when it is off.
4. **The game's own repulse acts at the middle.** `repulseagents` uses the entity's
   `CollisionBox`, one box at the trunk's centre, not the turned boxes; it no longer moves the
   trunk, and keeping agents out of it is `TrunkSolid`'s, on its whole footprint.
5. **Survival never receives a trunk, whatever asks.** `TrunkPockets` refuses every
   `TryGiveItemstack` of a trunk to a player not in creative mode, so a mod or command that gives
   one either drops it (and it lies there as a trunk entity) or, if it does not drop what was refused,
   loses it; a direct slot write, as by an admin tool, still puts one in. Only the server is patched
   (in single player the shared class is patched for both sides); gives are the server's to make.
