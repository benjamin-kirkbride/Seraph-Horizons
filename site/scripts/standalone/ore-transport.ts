// The "Transport: can rock be hauled home?" section of the smelt-by-grade proposal tab
// (ore-proposal.ts renders it). The proposal leans on bulk: rock forms stack poorly, concentrate
// stacks to 128, and each metal's deposit sits in its own 5 km ore cell, so a central plant has
// ore hauled to it from kilometres away. This audits every way the pack moves items in bulk, from
// the mod assets and code (the figures and their files are TRANSPORTS and NOTES), and models the
// haul: trips and time to bring metal home as rock or as concentrate, at 1, 2 and 5 km.
//
// Only data literals sit at the top level: ore-proposal.ts imports this module and this module
// imports ore-proposal.ts, so its imports are read inside functions only.

import { CFG, TIER_LABEL, TIERS } from "./ore-line.ts";
import { bestConcentrate, GRADES, PROPOSED, STACK, UNITS, UNITS_PER_PRODUCT, type Grade, type Proposal } from "./ore-proposal.ts";

// ---- the building blocks, each with its source in NOTES

/**
 * The engine's walking on the ground (note 17): each 1/60 s the walk vector is added to the motion,
 * which then keeps 1 − 0.3 of itself (PModuleOnGround's ground drag) and 0.983^(33/60) of that
 * (PModuleMotionDrag's air drag); the position moves by the motion each 1/60 s. A player's walk
 * vector is 1.5 (GlobalConstants.BaseMoveSpeed) × 1/60 s; a ridden mount's or cart's is its gait's
 * movespeed. The rider's client steps both at 1/60 s (EntityBehaviorPlayerPhysics.OnRenderFrame).
 */
export const GROUND = { drag: 0.3, airDrag: 0.983, baseMove: 1.5, tick: 1 / 60 } as const;

/** Blocks a second, steady on flat ground, for a walk vector (a gait's movespeed): about 135.7 × it. */
export function gaitSpeed(movespeed: number): number {
  const keep = (1 - GROUND.drag) * GROUND.airDrag ** (33 * GROUND.tick);
  return (movespeed * keep) / (1 - keep) / GROUND.tick;
}

/** Gait movespeeds (notes 5 and 6), as loaded in the pack. */
export const GAITS = {
  /** The tamed elk: a rider has trot and sprint (its rideableGaitOrder); walk and canter are the elk's own. */
  elk: { walk: 0.018, trot: 0.036, canter: 0.05, sprint: 0.08 },
  /** Cartwright's narrow (slim) cart, as the pack's cartwrightscaravan.yaml sets it. */
  cartNarrow: { walk: 0.016, jog: 0.033, sprint: 0.045 },
  /** Cartwright's wide (basic) cart. */
  cartWide: { walk: 0.015, jog: 0.03, sprint: 0.04 },
} as const;

/** The player: 10 hotbar slots and 4 bag slots; the best bag for rock is the sturdy mining bag. Walk in blocks a second (note 2). */
export const PLAYER = { hotbar: 10, bags: 4, miningBagSturdy: 14, walk: gaitSpeed(GROUND.baseMove * GROUND.tick) } as const;
/** Slots a player carries with four sturdy mining bags (ore and concentrate are both storage flag 5, the bag takes 4). */
export const PLAYER_SLOTS = PLAYER.hotbar + PLAYER.bags * PLAYER.miningBagSturdy;

/** Containers a vehicle takes: a chest 16 slots, a crate 25 (ebony or purpleheart; 20 in other woods), a reed basket 8. */
export const BOX = { chest: 16, crate: 25, basket: 8 } as const;

/** Carry On, as the pack sets it: walk-speed modifiers, and Purposeful Storage's resource bin as Mad Hoarding sizes it. */
export const CARRY = { handsWalk: -0.25, backWalk: -0.15, crateHandsWalk: -0.3, binStacks: 24 } as const;

/** Yang's Transport Tycoon: speed caps by track (blocks a second) and the stock. */
export const YANG = {
  track: { wagon: { wood: 8, metal: 14 }, standard: { wood: 16, metal: 32 } },
  boxcar: { slots: 64, weight: 4 },
  minecart: { weight: 1 },
  engineCartStandard: { power: 2.65, maxC: 1200, weight: 5 },
  locoStandard: { power: 4, maxC: 1100, weight: 10 },
  locoAdvanced: { power: 5.5, maxC: 1250, weight: 8 },
} as const;

/**
 * Yang's track recipes (note 18): one craft makes `pieces` rail pieces from support beams or iron
 * rods and planks. One piece lays one block of track (the owner's figure).
 */
export const RAILS = [
  { gauge: "standard", material: "wood", pieces: 48, binder: "support beams", binderQty: 6, planks: 12 },
  { gauge: "standard", material: "metal", pieces: 192, binder: "iron rods", binderQty: 6, planks: 12 },
  { gauge: "wagon", material: "wood", pieces: 32, binder: "support beams", binderQty: 4, planks: 8 },
  { gauge: "wagon", material: "metal", pieces: 128, binder: "iron rods", binderQty: 4, planks: 8 },
] as const;
export type Rail = (typeof RAILS)[number];

/** Whole crafts, and what they take, for `blocks` blocks of track. */
export function railCost(r: Rail, blocks: number): { crafts: number; binder: number; planks: number } {
  const crafts = Math.ceil(blocks / r.pieces);
  return { crafts, binder: crafts * r.binderQty, planks: crafts * r.planks };
}

/** The handcar's defaults (HandcarConfig.cs). */
export const HANDCAR = { topOne: 4.2, topTwo: 5.95, lossPerWeight: 0.5, minTop: 0.8, bareWeight: 2 } as const;

/** Top speed of a steam convoy at full heat: power × max °C ÷ 100 less the convoy's weight, capped by the track. */
export function steamTopSpeed(engine: { power: number; maxC: number; weight: number }, wagonWeight: number, cap: number): number {
  return Math.min(cap, Math.max(0, (engine.power * engine.maxC) / 100 - engine.weight - wagonWeight));
}

/** The handcar's top speed with `riders` pumping and `load` weight over the bare car (a crate 1, a boxcar 4). */
export function handcarTopSpeed(load: number, riders: 1 | 2 = 1): number {
  const top = riders === 1 ? HANDCAR.topOne : HANDCAR.topTwo;
  return Math.max(HANDCAR.minTop, top - HANDCAR.lossPerWeight * load);
}

// ---- the transports

export type Verdict = "holds" | "strained" | "breaks";

export interface Transport {
  id: string;
  name: string;
  /** What it carries, in words. */
  load: string;
  /** Slots that take raw ore and concentrate, the riding player's own included. */
  slots: { raw: number; conc: number };
  /** Blocks a second loaded with raw ore, or null where no figure in blocks a second was found. */
  speed: number | null;
  speedNote: string;
  build: string;
  terrain: string;
  notes: number[];
  verdict: Verdict;
  why: string;
}

/** Built lazily: the figures come from ore-proposal.ts's imports. */
export function transports(): Transport[] {
  const P = PLAYER_SLOTS;
  const carrySpeed = PLAYER.walk * (1 + CARRY.handsWalk + CARRY.backWalk);
  const minecartTrain = steamTopSpeed(YANG.engineCartStandard, 6 * (YANG.minecart.weight + 1), YANG.track.wagon.metal);
  const sgStandard = steamTopSpeed(YANG.locoStandard, 4 * YANG.boxcar.weight, YANG.track.standard.metal);
  const sgAdvanced = steamTopSpeed(YANG.locoAdvanced, 7 * YANG.boxcar.weight, YANG.track.standard.metal);
  return [
    {
      id: "foot",
      name: "On foot",
      load: `hotbar ${PLAYER.hotbar} + ${PLAYER.bags} sturdy mining bags × ${PLAYER.miningBagSturdy}`,
      slots: { raw: P, conc: P },
      speed: PLAYER.walk,
      speedNote: `walking; sprint ${num(2 * PLAYER.walk, 2)} at a hunger cost`,
      build: "Iron: sturdy mining bag (iron plate, sturdy leather); bronze bag 12 slots",
      terrain: "anywhere",
      notes: [1, 2],
      verdict: "holds",
      why: "The baseline the lever was set against.",
    },
    {
      id: "carryon",
      name: "On foot, Carry On",
      load: `+ a resource bin in the hands and one on the back, ${CARRY.binStacks} stacks each (concentrate: a crate in the hands, a chest on the back)`,
      slots: { raw: P + 2 * CARRY.binStacks, conc: P + BOX.crate + BOX.chest },
      speed: carrySpeed,
      speedNote: `walk × ${Math.round((1 + CARRY.handsWalk + CARRY.backWalk) * 100)} %, no sprint`,
      build: "Stone age: a bin is 6 planks",
      terrain: "anywhere; drops what it carries when hit",
      notes: [3, 4],
      verdict: "holds",
      why: "Nearly doubles a walker's rock for 12 planks, and the bin takes ore and crushed ore but not concentrate: the one container that favours rock.",
    },
    {
      id: "cartnarrow",
      name: "Narrow cart",
      load: `3 chests × ${BOX.chest}`,
      slots: { raw: P + 3 * BOX.chest, conc: P + 3 * BOX.chest },
      speed: gaitSpeed(GAITS.cartNarrow.sprint),
      speedNote: `sprint, held; jog ${num(gaitSpeed(GAITS.cartNarrow.jog), 2)}, walk ${num(gaitSpeed(GAITS.cartNarrow.walk), 2)}; the load never slows it`,
      build: "Wood, with metal hoops and nails (copper will do)",
      terrain: "land, steps 1.2 blocks; pulled by the player",
      notes: [5, 17],
      verdict: "holds",
      why: `Under twice a walker's load at ${num(gaitSpeed(GAITS.cartNarrow.sprint) / PLAYER.walk)} × walking pace.`,
    },
    {
      id: "cartwide",
      name: "Wide cart",
      load: `6 chests × ${BOX.chest}`,
      slots: { raw: P + 6 * BOX.chest, conc: P + 6 * BOX.chest },
      speed: gaitSpeed(GAITS.cartWide.sprint),
      speedNote: `sprint, held; jog ${num(gaitSpeed(GAITS.cartWide.jog), 2)}, walk ${num(gaitSpeed(GAITS.cartWide.walk), 2)}; the load never slows it`,
      build: "Wood, with metal hoops and nails",
      terrain: "land, steps 1.2 blocks; pulled by the player",
      notes: [5, 17],
      verdict: "holds",
      why: `2.5 walkers' worth at ${num(gaitSpeed(GAITS.cartWide.sprint) / PLAYER.walk)} × walking pace: a real convoy of trips still.`,
    },
    {
      id: "elk",
      name: "Elk with saddlebags",
      load: "sturdy saddlebags 20",
      slots: { raw: P + 20, conc: P + 20 },
      speed: gaitSpeed(GAITS.elk.sprint),
      speedNote: `gallop (sprint), no stamina; trot ${num(gaitSpeed(GAITS.elk.trot), 2)}`,
      build: "A tamed elk, sturdy leather and nails",
      terrain: "land; pulls no cart",
      notes: [6, 17],
      verdict: "holds",
      why: `${num(gaitSpeed(GAITS.elk.sprint) / PLAYER.walk)} × walking pace, but little more room than a walker.`,
    },
    {
      id: "raft",
      name: "Raft",
      load: `2 reed baskets × ${BOX.basket}`,
      slots: { raw: P + 2 * BOX.basket, conc: P + 2 * BOX.basket },
      speed: 4,
      speedNote: "before the water's drag",
      build: "Stone age",
      terrain: "water only",
      notes: [7],
      verdict: "holds",
      why: "A walker's load afloat.",
    },
    {
      id: "sailboat",
      name: "Sailed boat",
      load: `8 crates × ${BOX.crate}`,
      slots: { raw: P + 8 * BOX.crate, conc: P + 8 * BOX.crate },
      speed: 4 * 1.2,
      speedNote: "raft × 1.2, before drag",
      build: "Wood, rope and linen, no metal",
      terrain: "water only, both ends on it",
      notes: [7, 8],
      verdict: "strained",
      why: "Four walkers' worth with no metal at all, where a river or coast joins mine and plant.",
    },
    {
      id: "handcar",
      name: "Handcar",
      load: `1 crate × ${BOX.crate}`,
      slots: { raw: P + BOX.crate, conc: P + BOX.crate },
      speed: handcarTopSpeed(1),
      speedNote: `one rider; two ${Math.round(handcarTopSpeed(1, 2) * 100) / 100}`,
      build: "Copper: copper rods, 4 of Yang's minecarts",
      terrain: "standard-gauge track",
      notes: [9, 10],
      verdict: "holds",
      why: "One crate more than walking, on track.",
    },
    {
      id: "handcarboxcars",
      name: "Handcar + 2 boxcars",
      load: `1 crate + 2 boxcars × ${YANG.boxcar.slots}`,
      slots: { raw: P + BOX.crate + 2 * YANG.boxcar.slots, conc: P + BOX.crate + 2 * YANG.boxcar.slots },
      speed: handcarTopSpeed(1 + 2 * YANG.boxcar.weight),
      speedNote: `the riders' floor; two riders ${Math.round(handcarTopSpeed(1 + 2 * YANG.boxcar.weight, 2) * 100) / 100}`,
      build: "Iron: a boxcar is 10 iron plates, 2 trunks, 4 minecarts",
      terrain: "standard-gauge track",
      notes: [9, 10],
      verdict: "holds",
      why: "Room for three walkers, at a crawl: the weight is the brake.",
    },
    {
      id: "minecarts",
      name: "Engine cart + 6 minecarts",
      load: `6 crates × ${BOX.crate}`,
      slots: { raw: P + 6 * BOX.crate, conc: P + 6 * BOX.crate },
      speed: minecartTrain,
      speedNote: "standard engine cart, metal wagon rails (cap 14)",
      build: "Iron (standard engine)",
      terrain: "wagon rails",
      notes: [10],
      verdict: "breaks",
      why: `Three walkers' worth at ${num(minecartTrain / PLAYER.walk, 0)} times walking pace, and it can run on a timetable.`,
    },
    {
      id: "sgstandard",
      name: "Locomotive + 4 boxcars",
      load: `4 boxcars × ${YANG.boxcar.slots}`,
      slots: { raw: P + 4 * YANG.boxcar.slots, conc: P + 4 * YANG.boxcar.slots },
      speed: sgStandard,
      speedNote: "standard locomotive, metal track; 16 on wood",
      build: "Iron",
      terrain: "standard-gauge track",
      notes: [10],
      verdict: "breaks",
      why: `Five walkers' worth at ${num(sgStandard / PLAYER.walk, 0)} times walking pace; a full boxcar weighs what an empty one does.`,
    },
    {
      id: "sgadvanced",
      name: "Advanced locomotive + 7 boxcars",
      load: `7 boxcars × ${YANG.boxcar.slots}`,
      slots: { raw: P + 7 * YANG.boxcar.slots, conc: P + 7 * YANG.boxcar.slots },
      speed: sgAdvanced,
      speedNote: "metal track cap",
      build: "Steel and titanium",
      terrain: "standard-gauge track",
      notes: [10],
      verdict: "breaks",
      why: "Eight walkers' worth at 32 blocks a second: a whole 100-ingot batch of medium ore in one run.",
    },
    {
      id: "plane",
      name: "Flying machine",
      load: `1 crate × ${BOX.crate}`,
      slots: { raw: P + BOX.crate, conc: P + BOX.crate },
      speed: 20,
      speedNote: "≈ 20, its file says; 10 L of aqua vitae at 1 L a minute",
      build: "Iron (engine), aqua vitae for fuel",
      terrain: "any, from a 28-block runway",
      notes: [11],
      verdict: "strained",
      why: `A walker's load ${num(20 / PLAYER.walk, 0)} times as fast, as the crow flies, for fuel.`,
    },
    {
      id: "gondola",
      name: "Cable car (one cabin)",
      load: `1 crate × ${BOX.crate}`,
      slots: { raw: P + BOX.crate, conc: P + BOX.crate },
      speed: 5,
      speedNote: "MaxTravelSpeed",
      build: "Iron: a cable spool is an iron ingot for 25 m; mechanical power",
      terrain: "spans of 8 to 256 blocks, grade ≤ 1, curves ≤ 15°",
      notes: [12],
      verdict: "holds",
      why: `A walker's load at ${num(5 / PLAYER.walk)} × walking pace, after building towers every 256 blocks.`,
    },
  ];
}

/** Ways to move things that do not fit the haul table: they move the player, or items one at a time. */
export const OTHERS: { name: string; what: string; notes: number[] }[] = [
  {
    name: "Zipline",
    what: "Carries the player and their inventory downhill only, up to 30 blocks a second, on lines up to 1000 blocks long with a grade of at least 3 %. A loaded run is fast only where the mine is above the plant; the empty trip back walks. Whether Carry On blocks ride along: unknown.",
    notes: [13],
  },
  {
    name: "Craftable translocators",
    what: "A linked pair 1500 to 8000 blocks apart moves the player and their inventory at once, 3 jumps a temporal gear: a round trip costs two thirds of a gear. It takes the walk out of a 2 or 5 km haul but not the load, and each trip is paid in gears. Whether a carried block goes through: unknown.",
    notes: [14],
  },
  {
    name: "Chutes and hoppers",
    what: "Move items one at a time (item-flowrate 1) along a line of chute blocks. In the pack a straight chute is two copper chute sections, each two angles from the press brake: about 4 copper ingots a block, 4000 a kilometre. Whether a line keeps moving through unloaded chunks: unknown. No threat at kilometre range.",
    notes: [15],
  },
  {
    name: "An elk and a cart",
    what: "Not a combination in the pack. A Cartwright's cart is driven by the player who mounts its grip seat, and its only other way to be pulled, a rope to its tie point, is off (\"very janky\", its own config says). With rope towing turned on, a ridden elk could lead a roped cart at the elk's own gaits: the cart's 3 or 6 slots plus the elk's saddlebags, untested.",
    notes: [5, 6],
  },
  {
    name: "Not item transport",
    what: "ppex's pipes carry fluids and steam, smex's canals molten metal; the pack's trunk entities and their Carry On stations move Logging Expanded trunks only; Cartwright's sled takes 3 baskets and its market stall is a shop.",
    notes: [15, 16],
  },
];

export const NOTES: Record<number, string> = {
  1: "Player: hotbar 10 slots (VintagestoryLib InventoryPlayerHotbar holds 12: the 10, the offhand and one more), 4 bag slots (InventoryPlayerBackpacks). Sturdy mining bag 14 slots, mining bag 12, both storageFlags 4 (assets/survival/itemtypes/wearable/bag/miningbagsturdy.json, miningbag.json; recipe recipes/grid/miningbag.json, iron plate and sturdy leather for the sturdy one); backpack 6, sturdy 8 (backpack.json). Vanilla ore and the pack's concentrate are storageFlags 5 (ore-graded.json; main's itemtypes/oreprocessing/concentrate.json), so both go in a mining bag. Tools take hotbar slots; Tool Belt frees some.",
  2: "Walk 3.39 blocks a second (note 17): EntityControls.CalcMovementVectors gives a walk vector of BaseMoveSpeed 1.5 × the 1/60 s physics step = 0.025, × the walkspeed stat (1 unmodified) and the ground block's WalkSpeedMultiplier (1; stone paths 1.3, wooden paths 1.12, snow 0.8 to 0.95). Sprinting doubles it (GlobalConstants.SprintSpeedMultiplier 2, in EntityAgent.GetWalkSpeedMultiplier) at a hunger cost (EntityBehaviorHunger: 1.5 × the seconds sprinted). mods-src/seraphhorizons/TrunkEntities/Core/TrunkDrive.cs's WalkBlocksPerSecond 4.3 is above the engine's figure.",
  3: "Carry On 2.0.0-pre.8: one block in the hands and one on the back. The pack's config (configdefaults/0.1.0/files/CarryOnConfig.json): chests in the hands and on the back, crates in the hands only, trunks not at all, no sprint with the hands full. Default walk modifiers −0.25 in the hands, −0.15 on the back (CarryCodes.Defaults, decompiled); a crate −0.3 (patches/carryable/crate.json). A carried block drops on a hit of 1 (hands) or 6 (back). Its boat.json and carryonmore/cartwrightscaravan.json let a carried chest or crate be set straight onto a boat or a cart.",
  4: "Purposeful Storage 2.1.1's resource bin: config/restrictions/resources/resourcebin.json takes *:ore-*, *:crushed-*, nuggets and powders, not the pack's seraphhorizons:concentrate-*; patches/carryon.json makes it carryable in the hands and on the back; one slot of 8 stacks (BEResourceBin, decompiled), which Mad Hoarding 1.0.1's ResourceBinPatch raises to 24 (decompiled); recipes/grid/resources/resourcebin.json, 6 planks. One item type per bin.",
  5: "Cartwright's Caravan 1.9.1, entities/nonliving/cart.json: the wide cart has 6 storage slots and the narrow 3, each taking a chest or basket (a crate only with cartcrateable, off in configdefaults' cartwrightscaravan.yaml); the sturdy cart is skipped. Gaits in configdefaults' cartwrightscaravan.yaml (basiccart = wide, slimcart = narrow): walk 0.015 / 0.016, jog 0.03 / 0.033, sprint 0.04 / 0.045 (backwards 0.007 / 0.008); as blocks a second by note 17. The cart is a rideable EntityAgent whose controllable seat is its grip (MainGripAP): the player mounted there drives the cart's own gaits (EntityBehaviorRideable, EntityBehaviorGait.Move), so nothing pulls it and nothing slows it: no gait or physics code reads its weight (90) or its storage, a full cart goes as fast as an empty one. The rider holds the sprint key (no bridle, so the Hold scheme), paying the player's sprint hunger. Rope towing (ropetieable) is off: slimcartropetieable and widecartropetieable false. Wheels: recipes/grid/wheels.json (support beams, resin, hoops, nails).",
  6: "assets/survival/entities/animal/mammal/hooved/elk-tamed.json (code tameddeer-*; no mod in the pack changes its gaits): one lowerbackside slot for saddlebags; gaits walk 0.018, trot 0.036, canter 0.05, sprint 0.08, of which a rider gets trot and sprint (rideable.rideableGaitOrder Land: walkback, idle, trot, sprint): trot 4.89 and gallop 10.86 blocks a second by note 17. No stamina: EntityBehaviorRideable (1.22.7) has none, so the gallop holds as long as the rider asks; with the Press bridle (hoovedwearables-head-bridle2, controlScheme Press) it holds hands-free with no sprint key, and so no hunger cost; the Hold bridle needs the key held. Saddle and bridle change no speed. itemtypes/wearable/animal/hooved.json: saddlebags 16, sturdy 20; recipes/grid/tack.json.",
  7: "Raft: entities/nonliving/boat-raft.json, 2 basket slots, speedMultiplier 1; reed basket 8 slots (blocktypes/reed/reedchest.json). 4 blocks a second: TrunkDrive.RaftBlocksPerSecond (EntityBoat.SeatsToMotion, before drag).",
  8: "entities/nonliving/boat-sailed.json: 8 storage slots taking a seat, chest, basket or crate; speedMultiplier 1.2 (its blocks a second scaled from the raft's; wind not modelled); deconstructs to 144 planks, 22 support beams, 13 rope, 15 linen. Crate: 20 slots, 25 in ebony and purpleheart, one item type, storageType 189 which takes flag 4 (blocktypes/wood/woodtyped/crate.json).",
  9: "mods-src/seraphhorizons/Handcar/README.md and Core/HandcarConfig.cs: one cargo slot (chest or crate, weight 1), top speed 4.2 one rider and 5.95 two, less 0.5 a weight over the bare car's 2, never under 0.8; a coupled car adds its weight. Recipe recipes/grid/handcar.json.",
  10: "Yang's Transport Tycoon 1.0.3: entities/sgwagon_boxcar.json (2 × 32 slots, weight 4), minecart.json (one chest, crate or basket, weight 1 plus 1 for the box), sglocomotive.json and enginecart.json (power a 100 °C, max °C, weight), config/trackmaterialspeeds.json (wagon rails 8 wood, 14 metal; standard gauge 16, 32). Top speed = power × max °C ÷ 100 − convoy weight (Handcar/README.md, from the decompiled drive), at full heat. A vehicle's weight is its own plus its attachments (RailVehicleWeightSpec.ComputeTotalWeight, decompiled): boxcar storage adds nothing however full. Recipes: recipes/grid/standardgaugecarts.json (boxcar 10 iron plates), standardgaugerails.json and tunnelrails.json (track, note 18). Conductor locusts drive a timetable (lang: leave on time or mileage, no load criterion); whether a hopper or chute can fill a boxcar: unknown.",
  11: "Flying machine 0.1.0, entities/flyingmachine.json: one cargo slot (chest, basket, crate), flight.maxSpeed 0.34 a tick (its comment: about 20 blocks a second), 10 L of aqua vitae, 1 L a minute at full throttle, about 28 blocks of runway; engine of iron (recipes/grid/engine.json).",
  12: "Gondola Cable Car 0.4.43, entities/cabin.json: one storage slot (chest, basket, crate) and four seats; configdefaults GondolaCableCarConfig.json: MaxTravelSpeed 5, spans 8 to 256, MaxGrade 1, MaxCurveDegrees 15; cable spool smithed from an iron ingot, 25 m on large routes (lang).",
  13: "Zipline 1.0.8, configdefaults ZiplineConfig.json: MaxAnchorDistanceBlocks 1000, MinAverageGrade 0.03, MaximumSpeed 30.",
  14: "Rickety Translocation, configdefaults RicketyTranslocationConfig.json: MinDistance 1500, MaxDistance 8000, FuelPerGear 3.",
  15: "Chutes: assets/survival/blocktypes/metal/chute*.json (item-flowrate 1); the pack's patches/unifiedpipes-chutesection.json and recipes/grid/chutesection.json (two angles a section); a straight chute is two sections (recipes/grid/chute.json); an angle is a half plate, a half plate half a 2-ingot plate. ppex 0.7.1 and smex 0.10.1 have no item-flow blocks.",
  16: "Cartwright's Caravan entities/nonliving/sled.json (3 basket slots; chests only with sledchestable, off) and marketstall.json; mods-src/seraphhorizons/TrunkEntities/README.md.",
  17: "Blocks a second from a gait, decompiled from the 1.22.7 game: EntityBehaviorGait.Move sets the walk vector to the gait's movespeed × GlobalConstants.OverallSpeedMultiplier (1); EntityControls.CalcMovementVectors sets the player's to BaseMoveSpeed 1.5 × dt. The rider's client steps the player and a mount it controls at dt 1/60 s (EntityBehaviorPlayerPhysics.OnRenderFrame), through the same PModuleOnGround (each 1/60 s: motion += walk vector, then × (1 − groundDragFactor 0.3)) and PModuleMotionDrag (× AirDragAlways 0.983^(dt × 33)); ApplyTests moves the position by motion × dt × 60. Steady state: motion = walk vector × k ÷ (1 − k), k = 0.7 × 0.983^0.55, so blocks a second = 135.7 × the walk vector: a player 0.025 → 3.39. Measured in an Atlas run with every locked mod, a spawned tamed elk and both carts on flat granite, walk vector held (the server steps at 1/30 s, 135.0 ×): 0.025 → 3.37, 0.036 → 4.87, 0.08 → 10.72, wide cart 0.04 → 5.41, narrow 0.045 → 6.10.",
  18: "Yang's Transport Tycoon 1.0.3, recipes/grid/standardgaugerails.json: ABA three rows high, A a support beam (game:supportbeam-*) or an iron rod (game:rod-iron), B 4 planks: 6 beams or rods and 12 planks make 48 wooden or 192 metal widerails_straight pieces. recipes/grid/tunnelrails.json, the wagon rails (minecarts and engine carts): ABA two rows, 4 and 8, make 32 wooden or 128 metal. A piece is a block of track (the owner's figure). Turns, slopes, forks and swerves are crafted from straight pieces (2 to 5 for one), and a slope adds a support beam. An iron rod is smithed from one iron ingot (assets/survival/recipes/smithing/rod.json). Curves, stations and signals are left out.",
};

// ---- the haul model

/** The haul the brief asks for: 1000 units (10 ingots); and a plant's batch, ten times that. */
export const HAUL = 1000;
export const BATCH = 10 * HAUL;
export const DISTANCES = [1000, 2000, 5000];

export type Load = "raw" | "chunk" | "conc";

/** Units of metal in the ore that must come home for `target` units to come out of the furnace. */
export function rockUnits(grade: Grade, p: Proposal = PROPOSED, target = HAUL): number {
  return target / p.byGrade[grade];
}

/**
 * Slots to bring `target` units home. Raw ore (4 a stack at every grade) and chunks (16) smelt at
 * the grade's share under the proposal, so the rock carries target ÷ share; concentrate smelts whole,
 * so it is target ÷ 5 items whatever the grade and tier (the tier decides how much rock was dug).
 */
export function haulSlots(load: Load, grade: Grade, target = HAUL, p: Proposal = PROPOSED): number {
  if (load === "conc") return target / UNITS_PER_PRODUCT / STACK.concentrate;
  const items = rockUnits(grade, p, target) / UNITS[grade];
  return items / STACK[load];
}

/** Whole trips: a part load is a trip. */
export const trips = (slots: number, capacity: number) => Math.max(1, Math.ceil(slots / capacity - 1e-9));

/** Minutes for one round trip of `distance` blocks each way. */
export const roundTripMinutes = (distance: number, speed: number) => (2 * distance) / speed / 60;

/** Hours to bring `target` units home from `distance`: whole trips × the round trip. */
export function haulHours(t: Transport, load: Load, grade: Grade, distance: number, target = HAUL): number | null {
  if (t.speed == null) return null;
  const cap = load === "conc" ? t.slots.conc : t.slots.raw;
  return (trips(haulSlots(load, grade, target), cap) * roundTripMinutes(distance, t.speed)) / 60;
}

// ---- rendering

const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const num = (x: number, d = 1) => `${Math.round(x * 10 ** d) / 10 ** d}`;
const pct = (x: number) => `${Math.round(x * 1000) / 10} %`;
const cap = (s: string) => s[0]!.toUpperCase() + s.slice(1);
const refs = (ns: number[]) => `<sup>${ns.map((n) => `<a class="ot-ref" href="#ot-n${n}">${n}</a>`).join(", ")}</sup>`;
const time = (h: number | null) => (h == null ? "—" : h < 1 ? `${Math.round(h * 60)} min` : `${num(h)} h`);
const VERDICT: Record<Verdict, string> = { holds: "lever holds", strained: "strained", breaks: "breaks it" };

function renderAudit(ts: Transport[]): string {
  const head = `<tr><th>Transport</th><th>Carries</th><th>Slots<span class="ol-when">raw · concentrate, the player's ${PLAYER_SLOTS} included</span></th><th>Speed<span class="ol-when">blocks a second</span></th><th>Build</th><th>Terrain</th></tr>`;
  const rows = ts
    .map(
      (t) =>
        `<tr><th scope="row"><b>${esc(t.name)}</b>${refs(t.notes)}</th><td>${esc(t.load)}</td><td class="ol-c"><span class="ol-f"><b>${t.slots.raw}</b>${t.slots.conc !== t.slots.raw ? ` · ${t.slots.conc}` : ""}</span></td><td class="ol-c"><span class="ol-f"><b>${t.speed == null ? "unknown" : num(t.speed, 2)}</b></span><span class="ol-cnote">${esc(t.speedNote)}</span></td><td>${esc(t.build)}</td><td>${esc(t.terrain)}</td></tr>`,
    )
    .join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table ot-table ot-audit"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<ul class="ot-others">${OTHERS.map((o) => `<li><b>${esc(o.name)}</b>${refs(o.notes)}: ${esc(o.what)}</li>`).join("")}</ul>`;
}

/** What `target` units is as a load, by grade: raw, chunk and concentrate, and the rock each tier digs for it. */
function renderLoads(): string {
  const head = `<tr><th>Grade</th><th>Raw ore<span class="ol-when">stack ${STACK.raw}</span></th><th>Chunk<span class="ol-when">stack ${STACK.chunk}</span></th><th>Concentrate<span class="ol-when">stack ${STACK.concentrate}</span></th>${TIERS.map((t) => `<th class="ol-th-${t}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}<span class="ol-when">rock dug for it</span></th>`).join("")}<th>Raw ÷ concentrate<span class="ol-when">slots</span></th></tr>`;
  const rows = GRADES.map((g) => {
    const rock = rockUnits(g);
    const raw = haulSlots("raw", g);
    const chunk = haulSlots("chunk", g);
    const conc = haulSlots("conc", g);
    const tiers = TIERS.map((t) => {
      const c = bestConcentrate(t, g);
      return `<td class="ol-c"><span class="ol-f"><b>${num(HAUL / c.value, 0)}</b> units</span><span class="ol-cnote">${esc(c.device)} ${pct(c.value)}</span></td>`;
    }).join("");
    return `<tr><th scope="row"><b>${cap(g)}</b><span class="ol-when">${UNITS[g]} units an ore, smelts ${pct(PROPOSED.byGrade[g])}</span></th><td class="ol-c"><span class="ol-f"><b>${num(raw)}</b> slots</span><span class="ol-cnote">${num(rock / UNITS[g], 0)} ore, ${num(rock, 0)} units of rock</span></td><td class="ol-c"><span class="ol-f"><b>${num(chunk)}</b> slots</span><span class="ol-cnote">${g === "rich" || g === "bountiful" ? "how it is mined" : "not mined as chunks"}</span></td><td class="ol-c"><span class="ol-f"><b>${num(conc, 2)}</b> slots</span><span class="ol-cnote">${num(HAUL / UNITS_PER_PRODUCT, 0)} items</span></td>${tiers}<td class="ol-c"><span class="ol-f"><b>× ${num(raw / conc, 0)}</b></span></td></tr>`;
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table ot-table ot-loads"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> raw slots = ${HAUL} ÷ the grade's smelt share (proposed) ÷ units an ore ÷ ${STACK.raw}; chunk the same ÷ ${STACK.chunk}; concentrate = ${HAUL} ÷ ${UNITS_PER_PRODUCT} ÷ ${STACK.concentrate} = ${num(haulSlots("conc", "poor"), 2)} slots at any grade, since it smelts whole; the tier columns are the rock that concentrate came out of (${HAUL} ÷ the best device's recovery, poor not ground ×&nbsp;${CFG.fineUnground} where the tier has no grinder). Default ores; hematite (20/25/30/40) needs a fifth fewer slots, cassiterite and gold quartz (5/10/15/20) far more. Rich and bountiful are mined as chunks on main (OreProducts.ChunkStack 16), a quarter of the raw figure.</p>`;
}

/** Trips and hours, per transport, for `target` units. */
function renderHaul(ts: Transport[], target: number): string {
  const head = `<tr><th>Transport</th><th>Slots</th><th>Trips, raw ore<span class="ol-when">poor · medium · rich · bountiful</span></th><th>Trips, concentrate</th>${DISTANCES.map((d) => `<th>${d / 1000} km<span class="ol-when">medium raw → concentrate</span></th>`).join("")}<th>Verdict</th></tr>`;
  const rows = ts
    .map((t) => {
      const raw = GRADES.map((g) => trips(haulSlots("raw", g, target), t.slots.raw));
      const conc = trips(haulSlots("conc", "medium", target), t.slots.conc);
      const ratio = raw[1]! / conc;
      const times = DISTANCES.map((d) => {
        const hr = haulHours(t, "raw", "medium", d, target);
        const hc = haulHours(t, "conc", "medium", d, target);
        return `<td class="ol-c"><span class="ol-f"><b>${time(hr)}</b> → ${time(hc)}</span>${hr == null ? "" : `<span class="ol-cnote">round trip ${num(roundTripMinutes(d, t.speed!), 0)} min</span>`}</td>`;
      }).join("");
      return `<tr class="ot-v-${t.verdict}"><th scope="row"><b>${esc(t.name)}</b><span class="ol-when">${t.speed == null ? "speed unknown" : `${num(t.speed, 2)} blocks a second`}</span></th><td>${t.slots.raw}</td><td class="ol-c"><span class="ol-f"><b>${raw.join(" · ")}</b></span><span class="ol-cnote">medium × ${num(ratio, 0)} the concentrate trips</span></td><td class="ol-c"><span class="ol-f"><b>${conc}</b></span></td>${times}<td class="ot-verdict"><span class="ol-tag ot-tag-${t.verdict}">${VERDICT[t.verdict]}</span></td></tr>`;
    })
    .join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table ot-table ot-haul"><thead>${head}</thead><tbody>${rows}</tbody></table></div>`;
}

/** Track for a line: per km, and for the 2 and 5 km lines, by gauge and material. */
function renderRails(): string {
  const lines = [1000, 2000, 5000];
  const head = `<tr><th>Track</th><th>A craft</th>${lines.map((d) => `<th>${d / 1000} km<span class="ol-when">${d} pieces: crafts · beams or rods · planks</span></th>`).join("")}<th>Runs</th></tr>`;
  const runs: Record<string, string> = {
    "standard-wood": "boxcar trains, capped at 16",
    "standard-metal": "boxcar trains, capped at 32; the handcar",
    "wagon-wood": "minecarts, capped at 8",
    "wagon-metal": "minecarts, capped at 14",
  };
  const rows = RAILS.map((r) => {
    const cells = lines
      .map((d) => {
        const c = railCost(r, d);
        return `<td class="ol-c"><span class="ol-f"><b>${c.binder}</b> ${r.binder} · <b>${c.planks}</b> planks</span><span class="ol-cnote">${c.crafts} crafts</span></td>`;
      })
      .join("");
    return `<tr><th scope="row"><b>${cap(r.gauge)} gauge, ${r.material}</b></th><td class="ol-c"><span class="ol-f"><b>${r.pieces}</b> pieces</span><span class="ol-cnote">${r.binderQty} ${r.binder}, ${r.planks} planks</span></td>${cells}<td>${esc(runs[`${r.gauge}-${r.material}`]!)}</td></tr>`;
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table ot-table ot-rails"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> crafts = ⌈blocks ÷ pieces a craft⌉, one piece a block of track${refs([18])}; one line serves both ways. A kilometre of metal track is ${railCost(RAILS[1], 1000).binder} iron rods, one iron ingot each, and ${railCost(RAILS[1], 1000).planks} planks; of wood, ${railCost(RAILS[0], 1000).binder} support beams and ${railCost(RAILS[0], 1000).planks} planks. Straight track only: curves, slopes and forks are made from straight pieces, and stations and signals are extra.</p>`;
}

export const OT_STYLE = `<style>
.ot-table th sup, .ot-others sup { font-size: 0.68rem; line-height: 0; margin-left: 0.1rem; }
.ot-ref { color: var(--link); text-decoration: none; }
.ot-audit { min-width: 46rem; }
.ot-loads { min-width: 44rem; }
.ot-haul { min-width: 48rem; }
.ot-rails { min-width: 40rem; }
.ot-others { font-size: 0.84rem; margin: 0.5rem 0; }
.ot-others, .ot-notes, .ot-verdicts, .ot-table td { overflow-wrap: anywhere; }
.ot-others li { margin: 0.25rem 0; }
.ot-notes { font-size: 0.74rem; color: var(--muted); margin-top: 0.6rem; }
.ot-notes li { margin: 0.15rem 0; }
.ot-notes li:target { color: var(--text); background: var(--notice-bg); }
.ot-verdict { white-space: nowrap; }
.ot-tag-holds { background: color-mix(in srgb, var(--accent) 14%, transparent); border: 1px solid var(--accent); color: var(--accent); }
.ot-tag-strained { background: var(--notice-bg); border: 1px solid var(--notice-border); }
.ot-tag-breaks { background: color-mix(in srgb, var(--critical) 12%, transparent); border: 1px solid var(--critical); color: var(--critical); }
tr.ot-v-breaks > th { box-shadow: inset 3px 0 0 var(--critical); }
.ot-verdicts { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr)); gap: 0.5rem; margin: 0.5rem 0; }
.ot-verdicts > div { background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.45rem 0.6rem; font-size: 0.84rem; }
.ot-verdicts > div.ot-bad { border-color: var(--critical); }
.ot-verdicts ul { margin: 0.25rem 0 0; }
</style>`;

/** The section's body (ore-proposal.ts wraps it in its section). */
export function renderTransport(): string {
  const ts = transports();
  const foot = ts.find((t) => t.id === "foot")!;
  const adv = ts.find((t) => t.id === "sgadvanced")!;
  const boat = ts.find((t) => t.id === "sailboat")!;
  const wide = ts.find((t) => t.id === "cartwide")!;
  const elk = ts.find((t) => t.id === "elk")!;
  const medBatch = haulSlots("raw", "medium", BATCH);
  const poorBatch = haulSlots("raw", "poor", BATCH);
  const h = (t: Transport, g: Grade, load: Load = "raw") => time(haulHours(t, load, g, 2000, BATCH));
  const breakers = ts.filter((t) => t.verdict === "breaks");
  const strained = ts.filter((t) => t.verdict === "strained");
  return `${OT_STYLE}
<p class="ol-lede">Every way the pack moves items in bulk, from the mods' assets and code, and what it does to the bulk lever: rock forms stack to ${STACK.raw} (raw) or ${STACK.chunk} (chunk), concentrate to ${STACK.concentrate} at ${UNITS_PER_PRODUCT} units an item. A transport that holds hundreds of slots and moves fast, or moves items without the player, turns "haul the rock home" from a project into a chore. Slots count what a riding player carries too (${PLAYER_SLOTS}: the hotbar and four sturdy mining bags). Footnotes give the files.</p>
<h4>The audit</h4>
${renderAudit(ts)}
<h4>What 10 ingots is as a load</h4>
<p class="ol-ores">${HAUL} units that come out of the furnace, under the proposal's smelt shares: the rock forms carry their grade's share of the metal home, concentrate carries it all.</p>
${renderLoads()}
<h4>Hauling ${HAUL} units (10 ingots)</h4>
<p class="ol-ores">Whole trips (a part load is a trip) and the hours of travel at 1, 2 and 5 km, medium raw ore against concentrate. Straight-line distance: roads, track and rivers are longer, so the hours are a floor. Land speeds are steady speeds on flat ground${refs([17])}.</p>
${renderHaul(ts, HAUL)}
<h4>Hauling a plant's batch: ${BATCH} units (100 ingots)</h4>
<p class="ol-ores">The same at ten times the haul, where trips stop rounding up to one: ${num(poorBatch, 0)} slots of poor raw ore, ${num(medBatch, 0)} of medium, ${num(haulSlots("conc", "medium", BATCH))} of concentrate.</p>
${renderHaul(ts, BATCH)}
<p class="ol-formula"><b>Formula:</b> trips = ⌈slots ÷ the transport's slots⌉; hours = trips × 2 × distance ÷ speed. Loading, unloading, fuel and the player's own walk to the vehicle are left out.</p>
<h4>Laying track</h4>
<p class="ol-ores">What a train's line costs, the one outlay the trains need that the land transports do not.</p>
${renderRails()}
<h4>Verdict</h4>
<div class="ot-verdicts">
<div><b>The lever holds for anything a player walks, rides or pumps.</b> On foot, a medium-ore batch is ${trips(medBatch, foot.slots.raw)} trips and ${h(foot, "medium")} of walking at 2 km against ${h(foot, "medium", "conc")} as concentrate; poor ore is ${trips(poorBatch, foot.slots.raw)} trips. The wide cart and the elk cut the hours to ${h(wide, "medium")} and ${h(elk, "medium")} (the cart carries ${num(wide.slots.raw / foot.slots.raw)} walkers' loads at ${num(wide.speed! / foot.speed!)} × the pace, the elk gallops at ${num(elk.speed! / foot.speed!)} ×), but not the trips: ${trips(medBatch, wide.slots.raw)} and ${trips(medBatch, elk.slots.raw)} for medium ore and ${trips(poorBatch, wide.slots.raw)} and ${trips(poorBatch, elk.slots.raw)} for poor, against one of concentrate, each needing the player along. The raft, the handcar and the cable car do less. At 10 ingots everything but poor ore fits one trip, so the lever bites at a plant's scale, not a toolkit's.</div>
<div class="ot-bad"><b>Yang's trains break it.</b> ${breakers.map((t) => `${esc(t.name)} (${t.slots.raw} slots, ${num(t.speed!, 0)} blocks a second)`).join(", ")}. The advanced train brings the whole poor-ore batch in ${trips(poorBatch, adv.slots.raw)} trips and ${h(adv, "poor")} at 2 km. A boxcar's weight is its own plus its attachments, never its contents, so a full train runs as fast as an empty one; track is cheap (a kilometre of metal track is ${railCost(RAILS[1], 1000).binder} iron rods and ${railCost(RAILS[1], 1000).planks} planks, a 5 km line ${railCost(RAILS[1], 5000).binder} rods); and conductor locusts drive a timetable (leaving a station on time or mileage; whether anything but a player fills a boxcar: unknown).</div>
<div><b>Strained:</b> ${strained.map((t) => esc(t.name)).join(" and ")}. The boat holds ${boat.slots.raw} slots of crates with no metal in it, but only where water joins mine and plant; the plane flies a walker's load at ${num(20 / foot.speed!, 0)} times the pace, for aqua vitae.</div>
<div><b>What could be adjusted</b><ul>
<li>Boxcars and minecarts: make rock forms weigh, so convoy weight grows with ore (a Harmony postfix on RailVehicleWeightSpec.ComputeTotalWeight counting stacks of ore-*, chunk and crushed), or cut sgwagon_boxcar.json's QuantitySlotsByPoint for rock, or refuse rock forms in SGStorage. Concentrate untouched either way.</li>
<li>Crates: a crate holds 25 slots of one item and goes on boats, minecarts, the handcar and the plane. Giving rock forms a storage flag crates do not take (crate storageType 189 takes flag 4, which ore has) would leave rock to chests and mining bags.</li>
<li>Resource bin: Mad Hoarding's ×24 makes a 6-plank bin 24 stacks of ore on the back. Drop *:ore-* and *:crushed-* from its restriction list (a patch on purposefulstorage:config/restrictions/resources/resourcebin.json), or keep it as the walker's tool, the one container that favours rock.</li>
<li>Chunks at ${STACK.chunk} carry four times the metal of raw ore a slot; at ${STACK.raw} they would match it.</li>
</ul></div>
</div>
<ol class="ot-notes">${Object.entries(NOTES)
    .map(([n, s]) => `<li id="ot-n${n}" value="${n}">${esc(s)}</li>`)
    .join("")}</ol>`;
}
