# Model pitfalls

Things to look out for when making a model for a mod in this repository, all from building the
bucking sawmill (`mods-src/seraphhorizons/BuckingSawmill/`) and the rosser
(`mods-src/seraphhorizons/Rosser/`): faults they had, and a few choices that avoided one. The
process itself is in
[Animated machine models](animated-machine-models.md); this is the short list to read before
starting and again before calling a model done.

Most of the faults were invisible to every automated check at the time and were found by looking
at the machine in the game. Where a check now exists, it is named, so a new model can reuse it.

## Geometry

- **Coplanar faces flicker (z-fighting).** Two drawn faces of different elements in one plane,
  facing the same way and overlapping, flicker as the camera moves. Anything built from
  overlapping boxes does it at every shared end: an octagon from four rotated strips, a
  cross-profile shaft from two bars, a collar flush with a bearing. The sawmill had 348 such pairs,
  first noticed on the main shaft and the drums. Remove faces pressed flat against a neighbour of
  the same part, and move the smaller of two visible ones in by about 0.015 voxels
  (`fix_coplanar`, checked by `coplanar_faces` in `make_shape.py`).
- **The source model may already have the fault.** Immersive Woodworking's own pinions z-fight.
  Run the checks on the source shape too, so inherited faults are known and not blamed on new work.
- **Check faces in world space, after rotations, and in more than one pose.** Two faces can be
  clear at rest and share a plane mid-cut.
- **Insets move things tests measure.** After insetting faces, a test that recomputed the cell
  boxes from the shape needed a wider tolerance. Compute gameplay boxes before cosmetic
  adjustments, so the rig file does not change when the look does.
- **A toothed wheel that meshes with nothing reads as a mistake**, as does a part with no visible
  cause for its motion. Write the "driven by, drives" table first.
- **Anything that slides through needs a swept-volume check, not a look.** The first west end had
  a post in the trunk's path.
- **Nothing may float.** Every fixed element joins the frame; every shaft sits in bearings.
- **Thin and long parts need support at both ends**, and timbers need to be thick enough to
  believe.
- **A list of parts is not proof they were built.** The rosser's first renders showed feed rolls,
  worms and selector dogs in oak, a chute that had never been built, and top-roll arms and a rock
  shaft with nothing to stop them. Each became a check (textures by role, the chute's boards and
  mouth, the stops), and each was found only by looking at renders of the written files.

## Material that moves through the machine

- **Measure the other mod's model; do not assume its shape.** Logging Expanded's trunk sections are
  rounded squares, not circles: the `xxl` model's flats are 15.05 voxels from the axis, its corners
  18.5 and its knots 19.45 (`lg`: 7.5 and 9.0). Scraper tips set to a radius would have floated 3.5
  voxels off a thick trunk's flats. The rosser's arms follow the corners with harmonics of the ring's
  turn, and its generator measures the flats and corners from Logging Expanded's zip on every run and
  fails if they move.
- **A fixed support cannot centre two sizes.** On a fixed bed a thin trunk's axis lies 7.5 voxels
  below a thick one's, and the two sections are concentric, so no fixed support touches the thin one
  without passing through the thick one. Anything that must be concentric with both (a ring, a
  bore) needs a support that moves with the size: the rosser's weighing cradle.
- **A part that only turns while the machine runs, at a rate that depends on the material, cannot be
  a plain `rotate`.** A rotate on a gated shaft angle has one ratio, so rolls on it match the trunk for
  one class only. The rosser's feed input φ is defined by the trunk's travel instead
  (ΔT / `feed.blocksPerRadian`), so the rolls turn exactly with it for both classes, every metal and
  every setting. Start, stop and the change of speed also have to happen at one place: hence its one
  three-position selector rather than a start dog and a separate speed change.
- **Every contact that eases with presence scales with it.** While p eases in, every gauge moves by
  p times its amount, so a knock-off (a contact that must let go part way through a throw) cannot be
  timed during a load. The rosser's pushrod has a lost-motion stirrup and the rock shaft a stop
  instead.
- **Two holds in a row must overlap for the shortest material.** The rosser's feed is held in by the
  infeed roll until the outfeed roll takes over; the design's first layout gave a thin trunk an
  overlap of about −2 to +2 voxels. Its infeed roll now drops slowly behind the tail, and a check requires the rock
  shaft fully in for the whole trip of both classes.

## Files and generation

- **Hand edits are lost on the next generator run.** Port them into the script, or stop
  regenerating.
- **Element names are an interface.** The rig finds its parts by name prefix; renaming an element
  in a model editor silently drops it from its part.
- **Generation must be deterministic.** Run it twice into two folders and `diff -r` them, or
  every regeneration makes a noisy diff.
- **Every implementation of the rig maths must agree.** The sawmill has three (Python, C#,
  TypeScript for the site viewer), held together by reference poses
  (`tests/BuckingSawmill/rig-reference.json`). Change the maths in one and the reference catches
  the others.
- **Paths are written in many places.** Moving the model between mods broke the site's unit test,
  the docs and the licence note. Search for the old path everywhere, including `site/` and
  `LICENSE`. Moving the shared code to `Machines/` left old paths in READMEs, comments, a blocktype's
  comment and a generated reference's `_comment`.
- **A value both gameplay and the model use needs one definition and a test.** The rosser's feed
  pace is gameplay's (`RosserPace`), and the model draws it as gears (`feed.gear` in the rig). They are
  kept as two numbers on purpose, and `RosserRigTests` holds the drawn ratio to the pace at the
  defaults within 3 %; the shipped rig's parse test also failed, as intended, while the generator was
  part way through adding the key. Without such a test the drawn gearing and the real pace would
  drift apart unnoticed.
- **A shared generator package can change a machine you did not touch.** When the mill's generic code
  moved to `machinegen`, the gate was a byte-identical regeneration of the mill, validation log
  included. Run that gate for every machine after any change to the package.
- **A greedy box splitter is sensitive near ties.** A 1e-4 rounding difference changed one of the
  rosser's cells' boxes between the generator and the site. Build the cells' boxes from the shipped,
  rounded files, so every reader gets the same split.
- **A JSON patch that adds to another mod's list by index depends on that index.** The debarked
  trunk's patch adds to `/variantgroups/2/states`; if Logging Expanded reorders its groups, it would
  patch the wrong one. `DebarkedTrunks.Bind` checks the group is `branches` with states `yes` and `no`
  before the patch loader runs, and empties the patch with one warning if not.

## Licence and credit

- **Parts taken from another mod's model carry that mod's terms.** Get the author's permission
  first, record it in `CREDITS.md` (shipped in the zip), and say in `LICENSE` exactly which files
  are outside the repository's licence. Decide where those files live before building on them.
- **Permission covers the use it was given for.** The permission for the sawmill's parts was asked
  for the bucking sawmill; the rosser takes parts of the same model, so it needs the author's word
  too. Until then, credit the parts without claiming permission. Generated files can carry the claim
  as well (the generator's `_comment` in each shape), so check those too.

## Drawing it in the game

- **Split static from moving.** The block's own shape draws the frame in the chunk mesh; a
  renderer draws only what moves. Drawing the frame in the renderer as well costs frames and
  doubles every face.
- **A renderer outlives its block unless it is told otherwise.** Breaking a running mill left the
  moving parts hanging in the air, white with the missing-texture mark. The client removes a broken
  block at once, and an update the server sent just before can bring the block entity back over
  air. Create the renderer only when the block entity's block is the machine, dispose it on both
  removal and unload, and have the client tick drop a block entity whose block is gone.
- **White with question marks means the wrong texture source,** usually a mesh tessellated with a
  block that is air or not the machine's.
- **Values that change on game ticks look jerky when drawn as they are.** Client ticks run 20 times
  a second; frames run faster. The saws' sink and rise stepped visibly until frames drew part way
  between the last two ticks. Anything a renderer reads every frame must either advance per frame
  or be interpolated.
- **Draw the shadow passes too,** or the moving parts cast no shadow while the frame does.
- **Take the light from a cell that is not inside the model,** or the moving parts draw dark.
- **A big jump in a shown value needs easing; small steps need following.** Easing everything
  makes the parts lag the mechanism.
- **Sounds and particles belong to states, not to motion.** The sawmill sounds only while cutting.
  List which states are silent on purpose, so silence is not mistaken for a bug.

## Shapes the player touches

- **Collision and selection boxes must match what is drawn,** including things the machine holds.
  A loaded trunk with no hitbox could be walked through and not clicked.
- **A player walks on top of the machine.** Boxes that match what is drawn leave gaps between
  them, and a player who drops into one (the mill's trough over its saws, the rosser's hollow cells
  and the spaces about its ring and rolls) is stuck among the boxes, or caught by the trunk moving
  in. Both machines' rigs put a `lid` on every column's top cell: a thin collision-only box over
  the whole cell at the deck's height. It must stay out of the selection boxes, or it takes the
  clicks meant for the parts and the trunk under it.
- **Boxes belong to cells.** A box taller than its cell needs a cell above to carry the rest; the
  top half of a thick trunk over the controller column still has no collision for this reason.
- **Show held objects in a small set of known sizes.** Trunks come in six sizes; drawing each as
  it comes meant six fits to check. The mill shows two (LG and XXL), chosen by thickness, and the
  hitbox follows the shown one.
- **The footprint affects neighbours.** A two-block rack placed against the mill is refused in
  one orientation because its second block would land inside the machine. Work out where feeding
  blocks stand for all four facings.
- **`Block.SuggestedHVOrientation` is the way the player looks,** not the way back toward them.
- **Another mod's container can drop what it is given, silently.** Logging Expanded's Trunk Storage
  Rack holds four trunks, and its `PushTrunk` drops a fifth without a word. The rosser checks
  `TrunkCount` below 4 before pushing, and leaves the trunk on its bed when the rack is full.
- **A machine that hands material to another must not decide for it.** The bucking mill takes the
  rosser's trunk through an interface (`ITrunkFeeder`): it peeks, checks its own rules, and takes
  only when it will load, in the same tick, so a trunk it refuses stays where it is and none is
  duplicated or lost. While a taker is in line, the feeder does not also push to a rack.
- **Neighbour cells beside a wider section are not the end's.** The mill's rule for its rack cells
  also finds cells beside a narrower bed; on the rosser, whose station is wider than its beds, that
  would have put a "rack cell" beside the bed. The rosser's neighbours are only beyond the end row.
- **Hollow cells.** Cells a moving load passes through, with no part of the machine in them, must
  still be the machine's (or a player builds in the load's way), but have no box of their own. The
  rosser marks them `hollow` in the rig. Whether the game is happy with a cell whose selection box
  list is empty has not been seen in the game yet.

## Testing on a headless server

- **The server has no block textures.** `Block.Textures` is null on the server, even for another
  mod's block, so an Atlas test of a texture patch has to read the patched asset and check the texture
  files exist.
- **A sky position far from spawn may be in an unloaded chunk.** A scenario run alone can start
  before its chunk column is loaded; load it first, and keep a player near a machine whose scenario
  runs longer than a minute, or the server unloads it.
- **Reflection on another mod can meet two members of one name.** Pipes and Power Expanded 0.6.8's
  pipe network has two `State` properties (one hides the other), and a plain lookup threw an
  ambiguity error in Atlas. The rosser's binder takes the most derived one.

## Icons and help

- **Help lines that cycle through item stacks show every stack given.** Logging Expanded defines
  trunks for 55 woods, most from a mod not in the pack; the "load trunk" icon was blank most of
  the time. Filter to things that exist in the game being played.
- **A block defined for another mod's variants has no texture without that mod.** This applies to
  any list built by scanning `World.Blocks` by code prefix.

## The site viewer

- **Long unbroken text overflows a phone.** A file path in the model's credit line made the page
  64 px too wide; `overflow-wrap: anywhere` on description and credit fixed it. The end-to-end
  test "the model page fits a phone" catches this, but only in CI.
- **The viewer is not the game.** It shows geometry, motion and z-fighting well. It does not show
  the game's lighting, texture atlas, hitboxes, sounds or frame pacing.

## What no check covers

Before calling a model done, look at it in the game for:

1. flicker on shared faces, from several distances;
2. textures on every part, including with a different metal or wood fitted;
3. motion smoothness at low and high shaft speeds;
4. shadows and lighting, indoors and out;
5. hitboxes: walk into it, walk on top of it, and click every part of it, loaded and empty;
6. placing it in all four facings, next to the blocks that feed it;
7. breaking it while it runs, in creative and survival, and relogging;
8. every help line's icon;
9. the item in hand, on the ground and in the handbook;
10. material moving through the machine: how it looks as it passes each station (the rosser's
    trunk changes from bark to debarked inside the ring), and walking into and clicking it at every
    point of its trip.
