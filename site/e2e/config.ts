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
