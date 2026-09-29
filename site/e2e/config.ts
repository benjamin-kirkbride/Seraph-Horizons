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
