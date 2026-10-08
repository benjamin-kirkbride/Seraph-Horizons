// Shared by the Playwright config, the test server and the tests.
export const PORT = Number(process.env.E2E_PORT ?? 4317);
export const SUB_PATH = "/Seraph-Horizons/";
export const NO_ICONS_PATH = "/noicons/";
export const E2E_VERSIONS = [
  { id: "next", label: "next (e2e)" },
  { id: "v0.0.0-e2e", label: "0.0.0 (e2e)" },
];
/** The one item the test server gives an icon. */
export const ICON_CODE = "game:ingot-copper";
/**
 * Real icons from the repository's icons/, committed under e2e/icons/ as plain files: the
 * repository's copies are Git LFS objects and CI checks out without LFS. Both are dark
 * items a reader reported as hard to see.
 */
export const REAL_ICONS: Record<string, string> = {
  "game:rod-iron": "rod-iron.png",
  "yangtransport:steamengine-standard-north": "steamengine-standard-north.png",
};
/** The icon every item value is shown with, from the repository's icons/ like REAL_ICONS. */
export const GEAR_ICON = { code: "game:gear-rusty", file: "gear-rusty.png" };
/**
 * Item values the test server writes over the export's, so the tests know them whatever the
 * export says; null takes an item's value away. An export without values (one from before
 * the pack priced its items) gets a made-up value on nine items in ten besides, so the
 * values page is tested at full size. See serve.ts.
 */
export const E2E_VALUES: Record<string, { value: number; floorZero?: true; valuePerLitre?: true } | null> = {
  "game:gear-rusty": { value: 1 },
  "game:ingot-copper": { value: 2.5 },
  "game:stick": { value: 0.002, floorZero: true },
  // A liquid, priced per litre.
  "game:ciderportion-apple": { value: 4, valuePerLitre: true },
  "game:rot": null,
  // A block's orientations (blocktypes/wood/chest.json) are one row on the values page.
  "game:chest-east": { value: 6 },
  "game:chest-north": { value: 6 },
  "game:chest-south": { value: 6 },
  "game:chest-west": { value: 6 },
  // The members of E2E_GROUPS: three ingots share a price and lead stands apart.
  "game:ingot-tin": { value: 4 },
  "game:ingot-zinc": { value: 4 },
  "game:ingot-bismuth": { value: 4 },
  "game:ingot-lead": { value: 6 },
  "game:plank-oak": { value: 0.75 },
  "game:plank-birch": { value: 0.75 },
  "game:plank-pine": { value: 0.75 },
};
/**
 * Variant groups the test server adds to the export's (`variantGroups`), so the values page's
 * group rows are tested whether or not the export has groups of its own. Their members, and
 * every item of E2E_VALUES, are taken out of the export's groups first, so the tests know
 * which row each is in. Members are in rank order, as the exporter writes them.
 */
export const E2E_GROUPS: Record<string, { title: string; members: string[] }> = {
  "e2e:ingots": { title: "E2E ingots", members: ["game:ingot-tin", "game:ingot-zinc", "game:ingot-lead", "game:ingot-bismuth"] },
  "e2e:planks": { title: "E2E planks", members: ["game:plank-oak", "game:plank-birch", "game:plank-pine"] },
};
