// The ore review page's "Processing line" tab (scripts/standalone/ore-review.json): one flowchart per
// ore class, from mined ore to metal, each step in its hand, water and mill form, drawn as HTML boxes
// and CSS arrows on the site's colour tokens (light and dark). The design is the ore processing epic's
// (#684) as its newer issues and the recovery core's three tiers (#765) have it; where the epic's own
// tables differ, the issues win. Figures are the issues' starting values for playtesting.
//
// The default export is the tab's HTML fragment (scripts/standalone-tabs.ts includes it).

export type Tier = "hand" | "water" | "mill";
export const TIERS: Tier[] = ["hand", "water", "mill"];

export interface Box {
  name: string;
  /** A recovery or rate, shown beside the name. */
  figure?: string;
  detail?: string;
  issues: number[];
  /** "none": no device at this tier (drawn dashed); "unsure": the issues leave it open (marked ?). */
  kind?: "none" | "unsure";
}

export interface Row {
  stage: string;
  /** What skipping or lacking this step costs, or another word on the step. */
  note?: string;
  /** One box across all three tiers, or each tier's boxes (alternatives, one of them used). */
  span?: Box;
  lanes?: Record<Tier, Box[]>;
}

export interface Chart {
  id: string;
  title: string;
  ores: string;
  lede?: string;
  rows: Row[];
  /** A worked recovery per tier, from the issues' figures. */
  outcome?: { label: string; tiers: Record<Tier, string> };
  notes?: string[];
}

export interface Chain {
  id: string;
  title: string;
  steps: Box[];
  note?: string;
}

const REPO = "https://github.com/benjamin-kirkbride/Seraph-Horizons";

// ---- the shared steps

const mined: Row = {
  stage: "Mined",
  span: {
    name: "Raw ore or chunk",
    detail: "Poor and medium ore become heavy raw ore (stack 4), which does not smelt; rich and bountiful stay chunks (stack 16), which smelt at 50 %.",
    issues: [686],
  },
};

const crush: Row = {
  stage: "Crush",
  note: "No loss: 5 units a crushed item. Poor gives fine grain, the rest coarse.",
  lanes: {
    hand: [{ name: "Spalling", detail: "raw ore on the ground, broken by hammer blows", issues: [747] }],
    water: [
      { name: "Pulverizer", detail: "vanilla's, patched to the 5-unit rule", issues: [687] },
      { name: "Stamp mill L1", detail: "stamp battery, a hopper's worth a load", issues: [728] },
    ],
    mill: [{ name: "Stamp mill L2", detail: "rock breaker (jaw crusher) in front, chute-fed; oversize back to the breaker", issues: [728] }],
  },
};

const classify: Row = {
  stage: "Classify",
  note: "Unclassified feed: × 0.85 at the concentrator.",
  lanes: {
    hand: [{ name: "Riddle", detail: "hand sieve", issues: [714] }],
    water: [
      { name: "Riddle", detail: "with the pulverizer and the sluice, until the stamp mill", issues: [714] },
      { name: "Stamp mill L1 screen", detail: "on the battery's discharge", issues: [728] },
    ],
    mill: [{ name: "Stamp mill L2 screen", detail: "continuous, chute out", issues: [728] }],
  },
};

const grind: Row = {
  stage: "Grind",
  note: "Poor (fine) ore only. Unground poor ore: × 0.4. No loss.",
  lanes: {
    hand: [{ name: "No hand grinder", detail: "poor ore keeps its × 0.4", issues: [732], kind: "none" }],
    water: [{ name: "Grinder L1", detail: "arrastra: drag stones on a sweep", issues: [732] }],
    mill: [{ name: "Grinder L2", detail: "ball mill, chute-fed; middlings back from the concentrator", issues: [732] }],
  },
};

const concentrate: Row = {
  stage: "Concentrate",
  note: "Recovery is the device's base times the factors; density per ore, capped at 100 %.",
  lanes: {
    hand: [
      { name: "Gold pan", figure: "45 %", detail: "vanilla's (wpanning)", issues: [716] },
      { name: "Rocker", figure: "55 %", detail: "rocked with a bucket poured on it; no pipe", issues: [716] },
    ],
    water: [
      { name: "Concentrator L1", figure: "65 %", detail: "long-tom sluice, 2 L/s; the player feeds and empties it", issues: [734] },
      { name: "Concentrator L2", figure: "80 %", detail: "jig, 4 L/s; hopper in, output collects", issues: [734] },
    ],
    mill: [
      { name: "Concentrator L3", figure: "92 %", detail: "shaking table, 8 L/s, chute-fed", issues: [734] },
      { name: "Concentrator L4", figure: "99 %", detail: "table and slime vanner, 4 L/s recirculated, unattended", issues: [734] },
    ],
  },
};

const amalgamate: Row = {
  stage: "Amalgamate",
  note: "Free gold and silver not amalgamated: × 0.7.",
  lanes: {
    hand: [{ name: "Amalgam pan", detail: "concentrate and mercury to amalgam", issues: [718] }],
    water: [
      { name: "Amalgam pan", detail: "with the sluice (L1)", issues: [718] },
      { name: "Amalgamation plates", detail: "optional fitted part, from the jig (L2) on", issues: [734] },
    ],
    mill: [{ name: "Amalgamation plates", detail: "on the table (L3, L4)", issues: [734] }],
  },
};

const retort: Row = {
  stage: "Retort",
  note: "Mercury goes back round to the pan or the plates.",
  span: {
    name: "Still: cooking pot and condenser",
    detail: "Amalgam's mercury distils off, 90 % of it back; the gold or silver stays in the pot. The same at every tier: no machine retort.",
    issues: [726],
  },
};

const roast: Row = {
  stage: "Roast",
  note: "Required: unroasted sulfide concentrate does not smelt.",
  lanes: {
    hand: [{ name: "Firepit", figure: "85 %", detail: "vanilla's, about 600 °C, any fuel; no sulfur", issues: [720] }],
    water: [{ name: "Roaster L1", figure: "92 %", detail: "stall roaster; sulfur from here", issues: [736] }],
    mill: [{ name: "Roaster L2", figure: "100 %", detail: "reverberatory, chute-fed; sulfur", issues: [736] }],
  },
};

const smelt = (name: string, detail: string): Row => ({ stage: "Smelt", span: { name, detail, issues: [688] } });

const cupellation: Row = {
  stage: "Part",
  note: "Cupellation. Not parted, the by-product is lost.",
  lanes: {
    hand: [{ name: "Bone-ash cupel", figure: "85 %", detail: "in crucibulum's forge, the blast gate open; single use", issues: [722] }],
    water: [{ name: "Parting furnace L1", figure: "95 %", detail: "cupellation furnace", issues: [740] }],
    mill: [{ name: "Parting furnace L2", figure: "100 %", detail: "Parkes kettle, chute-fed; uses zinc, mostly returned", issues: [740] }],
  },
};

// ---- the charts

export const CHARTS: Chart[] = [
  {
    id: "oxide",
    title: "Oxide and carbonate ores",
    ores:
      "Hematite, magnetite, limonite, malachite, azurite, cassiterite (density × 1.05), chromite (× 1.05), ilmenite, smithsonite (× 0.9), hemimorphite, cerussite, vanadinite, wulfenite, uraninite, rhodochrosite. Native copper (× 1.1) and native platinum take this line too: native, but not free metal, so no amalgamation.",
    rows: [mined, crush, classify, grind, concentrate, smelt("Any furnace", "Concentrate smelts at 100 % in the firepit's crucible, crucibulum, the crucible furnace, the bloomery and smex.")],
    outcome: {
      label: "Poor hematite (#765)",
      tiers: { hand: "22 %: rocker 55 % × unground 0.4", water: "80 %: jig, ground", mill: "99 %: table and vanner" },
    },
    notes: ["Medium ore needs no grinding: hand 55 % (rocker), water 80 % (jig), mill 99 %.", "Tailings: 1 layer of gravel or sand per raw ore at the hand and water tiers, 0.5 at the mill (#694)."],
  },
  {
    id: "sulfide",
    title: "Sulfide ores",
    ores:
      "Galena, chalcopyrite, chalcocite, sphalerite, pentlandite, bismuthinite, pyrite, sperrylite; and the by-product sulfides below (tetrahedrite, freibergite, teallite, franckeite, argentiferous galena).",
    lede: "Concentrated like an oxide, then roasted before it smelts. Sulfur comes off from the stall roaster on, and feeds acid parting.",
    rows: [mined, crush, classify, grind, concentrate, roast, smelt("Any furnace", "Roasted concentrate smelts at 100 %.")],
    outcome: {
      label: "Medium ore",
      tiers: { hand: "47 %: rocker 55 % × firepit 85 %", water: "74 %: jig 80 % × stall 92 %", mill: "99 %: table and vanner × reverberatory" },
    },
  },
  {
    id: "native",
    title: "Native and free-milling gold and silver",
    ores: "Gold quartz and silver quartz: free metal, which floats past a gravity concentrator unless it is amalgamated.",
    lede: "Mercury comes from cinnabar retorted in the same still (#726).",
    rows: [
      mined,
      crush,
      classify,
      grind,
      concentrate,
      amalgamate,
      retort,
      smelt("Gold or silver", "Retorted gold or silver. Gold quartz carries 15 % silver, won only by acid parting (below); smelted unparted it gives gold at 85 %."),
    ],
    outcome: {
      label: "Medium silver quartz, amalgamated",
      tiers: { hand: "55 %: rocker (38.5 % unamalgamated)", water: "80 %: jig with plates", mill: "99 %: table and vanner with plates" },
    },
  },
  {
    id: "placer",
    title: "Placer gravel",
    ores: "Wilderlands' rich gravel and the pack's placer fields: free grains already, 1.1–4.1 units a block by rock (the pan's yield ÷ 0.45).",
    lede: "Joins the line at classification: no crushing or grinding. Output is each metal in the gravel table's proportions, as nuggets, with stone, flint, quartz and gems at the pan's rates.",
    rows: [
      { stage: "Dug", span: { name: "Gravel block", detail: "rich gravel or a placer field", issues: [701] } },
      { stage: "Crush", span: { name: "No crushing", detail: "gravel skips the stamps", issues: [701, 728], kind: "none" } },
      {
        stage: "Classify",
        lanes: {
          hand: [{ name: "Riddle?", detail: "the issues name no hand classifier for gravel", issues: [714], kind: "unsure" }],
          water: [{ name: "Stamp mill L1 screen", detail: "the screen only", issues: [728] }],
          mill: [{ name: "Stamp mill L2 screen", detail: "the screen only, chute out", issues: [728] }],
        },
      },
      { stage: "Grind", span: { name: "No grinding", detail: "already free grains", issues: [701], kind: "none" } },
      {
        stage: "Concentrate",
        note: "Yield relative to the gold pan; the line has no one-item cap.",
        lanes: {
          hand: [
            { name: "Gold pan", figure: "1×", detail: "wpanning: at most one item a pan", issues: [701] },
            { name: "Rocker", figure: "1.2×", issues: [716, 701] },
          ],
          water: [
            { name: "Concentrator L1", figure: "1.4×", detail: "sluice", issues: [734, 701] },
            { name: "Concentrator L2", figure: "1.8×?", detail: "jig: not given; 80 % ÷ 45 % would be 1.78×", issues: [734, 701], kind: "unsure" },
          ],
          mill: [
            { name: "Concentrator L3", figure: "2.0×", detail: "table", issues: [734, 701] },
            { name: "Concentrator L4", figure: "2.2×", detail: "table and vanner", issues: [734, 701] },
          ],
        },
      },
      { ...amalgamate, stage: "Amalgamate", note: "The gold, as for gold quartz (× 0.7 without)." },
      retort,
      { stage: "Result", span: { name: "Nuggets and gold", detail: "Each metal as nuggets; the gold from the still.", issues: [701] } },
    ],
  },
  {
    id: "galena",
    title: "Argentiferous galena and galena: silver by cupellation",
    ores: "Argentiferous galena (vanilla's silver galena, now a lead ore) carries 38 % silver; plain galena 3 % (#690). Smelted, both give only lead.",
    rows: [
      { stage: "Concentrate", span: { name: "Sulfide line", detail: "crush, classify, grind, concentrate and roast, as above", issues: [690] } },
      cupellation,
      { stage: "Result", span: { name: "Silver and litharge", detail: "Silver; the lead soaks into the cupel as litharge, which smelts back to lead at a small loss.", issues: [722, 690] } },
    ],
  },
  {
    id: "tetrahedrite",
    title: "Tetrahedrite and freibergite: cupellation with lead",
    ores: "Tetrahedrite carries 5 % silver; freibergite 30 % copper.",
    rows: [
      { stage: "Concentrate", span: { name: "Sulfide line", detail: "to roasted concentrate, as above; lead is added to the charge", issues: [722] } },
      cupellation,
      { stage: "Result", span: { name: "Main metal and by-product", detail: "Silver; freibergite's copper comes out with the litharge as its own nugget share (to settle when built).", issues: [722, 740] } },
    ],
  },
  {
    id: "teallite",
    title: "Teallite and franckeite: lead by liquation, tin first",
    ores: "Teallite carries 40 % lead, franckeite 30 %. Tin melts at 232 °C and lead at 327 °C, so a gentle heat sweats the tin out and leaves the lead.",
    rows: [
      { stage: "Concentrate", span: { name: "Sulfide line", detail: "to roasted concentrate, as above", issues: [724] } },
      {
        stage: "Part",
        note: "Liquation. Over 327 °C the two run together and the lead is lost.",
        lanes: {
          hand: [{ name: "Clay liquation pan", figure: "85 %", detail: "in a firepit or forge, kept between 240 and 327 °C", issues: [724] }],
          water: [{ name: "Parting furnace L1", figure: "95 %", detail: "liquation furnace", issues: [740] }],
          mill: [{ name: "Parting furnace L2", figure: "100 %", detail: "the Parkes kettle level, all chute-fed", issues: [740] }],
        },
      },
      { stage: "Result", span: { name: "Tin, then lead", detail: "Tin runs off first; the lead stays in the pan. Unparted, these ores give tin only.", issues: [724] } },
    ],
  },
  {
    id: "goldquartz",
    title: "Gold quartz: silver by acid parting",
    ores: "Gold quartz carries 15 % silver.",
    rows: [
      { stage: "Concentrate", span: { name: "Free-milling line", detail: "concentrate, amalgamate and retort, as above", issues: [718, 726] } },
      {
        stage: "Part",
        note: "Acid parting: aqua fortis from saltpeter (#742) and sulfuric acid, its sulfur from the stall roaster (#736).",
        lanes: {
          hand: [{ name: "No hand parting", detail: "gold at 85 %, the silver lost", issues: [740], kind: "none" }],
          water: [{ name: "Parting furnace L1", figure: "95 %", detail: "acid parting vessel", issues: [740] }],
          mill: [{ name: "Parting furnace L2", figure: "100 %", detail: "the Parkes kettle level, all chute-fed", issues: [740] }],
        },
      },
      { stage: "Result", span: { name: "Gold and silver", issues: [740] } },
    ],
  },
];

export const CHAINS: Chain[] = [
  {
    id: "coal",
    title: "Coal",
    steps: [
      { name: "Raw coal", detail: "the raw ore stack rule", issues: [686] },
      { name: "Concentrator", detail: "washes coal at its water and mill levels; no hand form", issues: [734] },
      { name: "Washed coal", detail: "less ash, more heat an item", issues: [734] },
    ],
    note: "Expanded Matter's grid crushing of coal is left to the washing (#687).",
  },
  {
    id: "leach",
    title: "Borax, saltpeter and alum",
    steps: [
      { name: "Raw mineral", detail: "heavy raw form, stack 4", issues: [742, 686] },
      { name: "Barrel", detail: "water and raw mineral soak into a crude liquor over some hours; the rock is lost", issues: [742] },
      { name: "Cooking pot", detail: "on a firepit, the liquor evaporates", issues: [742] },
      { name: "Powdered borax, saltpeter, powdered alum", detail: "today's items, so every recipe using them still works", issues: [742] },
    ],
    note: "Hand only: no machine form.",
  },
  {
    id: "sulfur",
    title: "Sulfur",
    steps: [
      { name: "Sulfur-bearing rock", issues: [744] },
      { name: "Sulfur kiln", detail: "sketched only; model #745", issues: [744] },
      { name: "Sulfur", detail: "also from roasting sulfides at the stall and reverberatory roasters", issues: [744, 736] },
    ],
  },
  {
    id: "mercury",
    title: "Mercury",
    steps: [
      { name: "Cinnabar", detail: "powder or crushed", issues: [726] },
      { name: "Still", detail: "cooking pot on a firepit, the copper condenser beside it", issues: [726] },
      { name: "Mercury", detail: "Expanded Matter's mercury liquid; its cooking recipe is removed", issues: [726] },
    ],
  },
];

export const OPEN_QUESTIONS: string[] = [
  "Unroasted sulfide concentrate: #688 smelts it at 60 %, #720 (newer) says it does not smelt at all. The chart follows #720.",
  "Freibergite: #685 and the config give it a copper by-product (30 %) parted by cupellation, but #740 lists \"silver from … freibergite\". Which is its main metal and which the by-product?",
  "Galena's silver: #685 and today's ore-processing.json have 3 % in veins and 15 % in districts; #690 replaces that with plain galena 3 % and argentiferous galena 38 %. The chart follows #690.",
  "Parting at the mill tier: #740 names only the Parkes kettle (cupellation). Do liquation and acid parting also reach 100 % at that level, and with what device?",
  "Parting tiers in ore-processing.json are still the old five (hand, tier 1 to 4; acid parting from tier 2); #765 is to rework them to hand, water, mill.",
  "Placer gravel: the jig's yield relative to the pan is not given in #701 (rocker 1.2×, sluice 1.4×, table 2.0×, table and vanner 2.2×). Does placer feed count as classified, and is there a hand classifier for it (the riddle is for crushed ore, #714)?",
  "Gold pan on crushed ore: #716 puts the gold pan (45 %) before the rocker on crushed ore, but vanilla's pan works gravel and sand blocks. Is a pan for crushed ore part of #716?",
  "Gold quartz's unparted 85 %: does it apply after amalgamation and retorting too, so the hand tier's gold quartz always loses its 15 %?",
  "Coal: does it need crushing or classifying before washing, and does the hand tier wash it at all (the rocker)?",
  "Sulfur: how much the stall and reverberatory roasters give per roasted concentrate (open in the epic), and the sulfur kiln (#744) is only sketched.",
  "The pulverizer is a water-tier crusher in #687 and #728, but it runs on mechanical power, not ppex water; the tiers say how a machine is powered doesn't matter, so this is a naming point only.",
];

// ---- rendering

const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const issueLink = (n: number) => `<a class="ol-issue" href="${REPO}/issues/${n}" target="_blank" rel="noopener noreferrer">#${n}</a>`;
/** "#123" in prose becomes a link. */
const linked = (s: string) => esc(s).replace(/#(\d{3,4})\b/g, (_m, n: string) => issueLink(Number(n)));

const TIER_LABEL: Record<Tier, [string, string]> = {
  hand: ["Hand", "stations the player works"],
  water: ["Water", "machines on ppex water, fed by the player"],
  mill: ["Mill", "continuous, chute-fed, unattended"],
};

function box(b: Box, tier: Tier | "span"): string {
  const cls = ["ol-box", `ol-t-${tier}`, b.kind ? `ol-${b.kind}` : ""].filter(Boolean).join(" ");
  const fig = b.figure ? ` <span class="ol-fig">${esc(b.figure)}</span>` : "";
  const detail = b.detail ? `<span class="ol-detail">${linked(b.detail)}</span>` : "";
  return `<div class="${cls}"><span class="ol-name">${esc(b.name)}${fig}</span>${detail}<span class="ol-issues">${b.issues.map(issueLink).join(" ")}</span></div>`;
}

function cell(boxes: Box[], tier: Tier, first: boolean): string {
  return `<div class="ol-cell ol-lane-${tier}${first ? " ol-first" : ""}">${boxes.map((b) => box(b, tier)).join('<span class="ol-or">or</span>')}</div>`;
}

export function renderChart(c: Chart): string {
  const head = `<div class="ol-corner"></div>${TIERS.map((t) => `<div class="ol-tier ol-tier-${t}"><b>${TIER_LABEL[t][0]}</b><span>${TIER_LABEL[t][1]}</span></div>`).join("")}`;
  const rows = c.rows
    .map((r, i) => {
      const stage = `<div class="ol-stage${i === 0 ? " ol-first" : ""}"><b>${esc(r.stage)}</b>${r.note ? `<span>${linked(r.note)}</span>` : ""}</div>`;
      if (r.span) return `${stage}<div class="ol-cell ol-span${i === 0 ? " ol-first" : ""}">${box(r.span, "span")}</div>`;
      if (!r.lanes) throw new Error(`${c.id}: row ${r.stage} has neither span nor lanes`);
      return stage + TIERS.map((t) => cell(r.lanes![t], t, i === 0)).join("");
    })
    .join("\n");
  const outcome = c.outcome
    ? `<div class="ol-outcome"><b>${linked(c.outcome.label)}</b><ul>${TIERS.map((t) => `<li><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}: ${esc(c.outcome!.tiers[t])}</li>`).join("")}</ul></div>`
    : "";
  const notes = (c.notes ?? []).map((n) => `<p class="ol-note">${linked(n)}</p>`).join("");
  return `<section class="ol-chart" id="ol-${c.id}" aria-labelledby="ol-${c.id}-h">
<h3 id="ol-${c.id}-h">${esc(c.title)}</h3>
<p class="ol-ores">${linked(c.ores)}</p>${c.lede ? `<p class="ol-lede">${linked(c.lede)}</p>` : ""}
<div class="ol-grid">${head}
${rows}
</div>${outcome}${notes}
</section>`;
}

export function renderChain(c: Chain): string {
  return `<section class="ol-chain" id="ol-${c.id}" aria-labelledby="ol-${c.id}-h"><h4 id="ol-${c.id}-h">${esc(c.title)}</h4>
<div class="ol-steps">${c.steps.map((s) => box(s, "span")).join('<span class="ol-arrow" aria-hidden="true">↓</span>')}</div>${c.note ? `<p class="ol-note">${linked(c.note)}</p>` : ""}</section>`;
}

const STYLE = `<style>
.ore-line { --ol-hand: var(--series-4); --ol-water: var(--series-1); --ol-mill: var(--series-3); --ol-line: var(--muted); }
.ore-line h2 { font-size: 1.3rem; margin: 0.2rem 0 0.4rem; }
.ore-line h3 { font-size: 1.12rem; margin: 0 0 0.3rem; }
.ore-line h4 { font-size: 1rem; margin: 0 0 0.4rem; }
.ore-line p { margin: 0.3rem 0; }
.ol-intro { max-width: 52rem; }
.ol-jump { display: flex; flex-wrap: wrap; gap: 0.3rem 0.5rem; margin: 0.6rem 0 1rem; font-size: 0.88rem; }
.ol-jump a { color: var(--link); }
.ol-legend { display: grid; grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr)); gap: 0.5rem; margin: 0.6rem 0; }
.ol-legend > div { background: var(--surface); border: 1px solid var(--border); border-top: 4px solid var(--ol-c); border-radius: var(--radius); padding: 0.4rem 0.6rem; font-size: 0.86rem; }
.ol-factors { display: flex; flex-wrap: wrap; gap: 0.4rem; margin: 0.4rem 0 0.8rem; padding: 0; list-style: none; font-size: 0.86rem; }
.ol-factors li { background: var(--notice-bg); border: 1px solid var(--notice-border); border-radius: 999px; padding: 0.1rem 0.6rem; }
.ol-chart, .ol-chain { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.8rem; margin: 0 0 1rem; }
.ol-ores, .ol-lede, .ol-note { font-size: 0.88rem; color: var(--muted); }
.ol-grid { display: grid; grid-template-columns: minmax(6rem, 10rem) repeat(3, minmax(0, 1fr)); column-gap: 0.6rem; row-gap: 1.3rem; margin-top: 0.7rem; }
.ol-tier { border-top: 4px solid var(--ol-c); padding-top: 0.2rem; font-size: 0.85rem; line-height: 1.25; }
.ol-tier span { display: block; color: var(--muted); font-size: 0.78rem; }
.ol-tier-hand, .ol-lane-hand, .ol-t-hand, .ol-legend .ol-l-hand, .ol-dot-hand { --ol-c: var(--ol-hand); }
.ol-tier-water, .ol-lane-water, .ol-t-water, .ol-legend .ol-l-water, .ol-dot-water { --ol-c: var(--ol-water); }
.ol-tier-mill, .ol-lane-mill, .ol-t-mill, .ol-legend .ol-l-mill, .ol-dot-mill { --ol-c: var(--ol-mill); }
.ol-t-span { --ol-c: var(--border); }
.ol-stage { font-size: 0.86rem; line-height: 1.3; padding-top: 0.15rem; }
.ol-stage b { display: block; font-size: 0.95rem; }
.ol-stage span { color: var(--muted); font-size: 0.8rem; }
/* each lane's rail, under its boxes, so a short step still joins the next */
.ol-cell { position: relative; display: flex; flex-direction: column; gap: 0.2rem; background: linear-gradient(var(--ol-line), var(--ol-line)) center / 2px 100% no-repeat; }
.ol-cell.ol-first { background-position: center 0.5rem; }
.ol-span { grid-column: 2 / -1; }
/* the arrow into each step, from the row above */
.ol-cell::before { content: ""; position: absolute; left: 50%; top: -1.3rem; height: 1.3rem; border-left: 2px solid var(--ol-line); transform: translateX(-1px); }
.ol-cell::after { content: ""; position: absolute; left: 50%; top: -0.45rem; transform: translateX(-5px); border: 5px solid transparent; border-top: 7px solid var(--ol-line); border-bottom: 0; }
.ol-cell.ol-first::before, .ol-cell.ol-first::after { display: none; }
.ol-box { position: relative; background: var(--bg); border: 1px solid var(--border); border-left: 4px solid var(--ol-c); border-radius: var(--radius); padding: 0.3rem 0.45rem; font-size: 0.84rem; line-height: 1.3; }
.ol-span > .ol-box { background: var(--surface-2); }
.ol-name { display: block; font-weight: 600; }
.ol-fig { display: inline-block; font-weight: 700; color: var(--accent); margin-left: 0.15rem; }
.ol-detail { display: block; color: var(--muted); font-size: 0.8rem; }
.ol-issues { display: block; font-size: 0.78rem; }
.ol-issue { color: var(--link); white-space: nowrap; }
.ol-none { border-style: dashed; border-left-style: dashed; background: var(--surface); }
.ol-none .ol-name { font-weight: 500; color: var(--muted); }
.ol-unsure { border-style: dashed; }
.ol-or { align-self: center; background: var(--surface); padding: 0 0.3rem; font-size: 0.72rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.05em; line-height: 1; }
.ol-outcome { margin-top: 0.8rem; font-size: 0.86rem; }
.ol-outcome ul { list-style: none; margin: 0.2rem 0 0; padding: 0; display: flex; flex-wrap: wrap; gap: 0.2rem 1.2rem; }
.ol-dot { display: inline-block; width: 0.7rem; height: 0.7rem; border-radius: 2px; background: var(--ol-c); margin-right: 0.35rem; vertical-align: -0.05rem; }
.ol-steps { display: flex; flex-direction: column; align-items: stretch; gap: 0.1rem; }
.ol-arrow { align-self: center; color: var(--ol-line); font-size: 1.1rem; line-height: 1.2; }
.ol-chains { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 30rem), 1fr)); gap: 0 1rem; }
.ol-questions { font-size: 0.9rem; }
.ol-questions li { margin: 0.25rem 0; }
@media (max-width: 640px) {
  .ol-chart, .ol-chain { padding: 0.6rem; }
  .ol-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); column-gap: 0.35rem; row-gap: 0.3rem; }
  .ol-corner { display: none; }
  .ol-stage { grid-column: 1 / -1; margin-top: 0.9rem; border-top: 1px solid var(--border); padding: 0.3rem 0 0.5rem; }
  .ol-stage.ol-first { margin-top: 0.3rem; }
  .ol-span { grid-column: 1 / -1; }
  /* each stage has its own heading here, so a step gets an arrowhead, not a rail */
  .ol-cell, .ol-cell.ol-first { background: none; }
  .ol-cell::before { display: none; }
  .ol-cell::after { top: -0.5rem; }
  .ol-box { font-size: 0.76rem; padding: 0.25rem 0.3rem; overflow-wrap: anywhere; }
  .ol-detail { font-size: 0.72rem; }
  .ol-tier span { display: none; }
}
</style>`;

export default function oreLine(): string {
  const all = [...CHARTS.map((c) => ({ id: c.id, title: c.title })), { id: "nonmetals", title: "Coal and non-metals" }];
  return `<div class="ore-line">
${STYLE}
<h2>The processing line, by ore class</h2>
<p class="ol-intro">From mined ore to metal, each step in its three forms: <b>hand</b>, <b>water</b> and <b>mill</b>. Read down a column for one tier; a player can mix tiers, so a step can come from another column. Where a cell offers two, either does the step (L1, L2… are a machine's levels, fitted in place). Overall recovery is the product of the steps' factors. Every box links its issue; the figures are the issues' starting values for playtesting. Where the epic (${issueLink(684)}) and the newer issues differ, this follows the issues and the recovery core's three tiers (${issueLink(765)}).</p>
<div class="ol-legend">${TIERS.map((t) => `<div class="ol-l-${t}"><b>${TIER_LABEL[t][0]}</b>: ${TIER_LABEL[t][1]}.</div>`).join("")}</div>
<ul class="ol-factors" aria-label="Recovery factors">
<li>Unclassified × 0.85</li><li>Poor ore not ground × 0.4</li><li>Free metal not amalgamated × 0.7</li><li>Density: cassiterite, chromite × 1.05; native copper × 1.1; smithsonite × 0.9</li><li>Crushing and grinding never lose metal</li>
</ul>
<nav class="ol-jump" aria-label="Ore classes">${all.map((c) => `<a href="#ol-${c.id}">${esc(c.title)}</a>`).join("")}</nav>
${CHARTS.slice(0, 4).map(renderChart).join("\n")}
<h2>By-product ores: the parting step</h2>
<p class="ol-intro">Each carries a second metal won only at a parting step (${issueLink(740)}); unparted, the by-product is lost.</p>
${CHARTS.slice(4).map(renderChart).join("\n")}
<h2 id="ol-nonmetals">Coal and non-metals</h2>
<div class="ol-chains">${CHAINS.map(renderChain).join("\n")}</div>
<details class="ol-questions" open><summary><b>Open questions from the issues</b></summary><ul>${OPEN_QUESTIONS.map((q) => `<li>${linked(q)}</li>`).join("")}</ul></details>
</div>`;
}
