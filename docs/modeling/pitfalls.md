# Model pitfalls

Things to look out for when making a model for a mod in this repository, all from building the
bucking sawmill (`mods-src/seraphhorizons/BuckingSawmill/`): faults it had, and a few choices
that avoided one. The process itself is in
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
  `LICENSE`.

## Licence and credit

- **Parts taken from another mod's model carry that mod's terms.** Get the author's permission
  first, record it in `CREDITS.md` (shipped in the zip), and say in `LICENSE` exactly which files
  are outside the repository's licence. Decide where those files live before building on them.

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
- **Boxes belong to cells.** A box taller than its cell needs a cell above to carry the rest; the
  top half of a thick trunk over the controller column still has no collision for this reason.
- **Show held objects in a small set of known sizes.** Trunks come in six sizes; drawing each as
  it comes meant six fits to check. The mill shows two (LG and XXL), chosen by thickness, and the
  hitbox follows the shown one.
- **The footprint affects neighbours.** A two-block rack placed against the mill is refused in
  one orientation because its second block would land inside the machine. Work out where feeding
  blocks stand for all four facings.
- **`Block.SuggestedHVOrientation` is the way the player looks,** not the way back toward them.

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
5. hitboxes: walk into it, and click every part of it, loaded and empty;
6. placing it in all four facings, next to the blocks that feed it;
7. breaking it while it runs, in creative and survival, and relogging;
8. every help line's icon;
9. the item in hand, on the ground and in the handbook.
