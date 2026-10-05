// Facts checked against the vanilla 1.22.7 assets (assets/survival/recipes/...) and
// game/lang/en.json. Each test names the file it relies on.
import { V, card, cellsOf, expect, filledCells, gridCell, openItem, pause, search, test } from "./fixtures.ts";

test("search finds the crude ladder by name and its grid is laid out as grid/ladder.json says", async ({ page }) => {
  await page.goto("./");
  const results = await search(page, "Crude ladder");
  // lang: "block-ladder-stick-*": "Crude ladder"
  await expect(results.first()).toHaveAttribute("data-code", "game:ladder-stick-north");
  await results.first().click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Crude ladder");
  await expect(page.getByTestId("item-code")).toHaveText("game:ladder-stick-north");

  // grid/ladder.json, first recipe: ingredientPattern "S_S	SSS	S_S", S = stick, quantity 3.
  const c = await card(page, "madeBy", "recipes/grid/ladder.json");
  const expected = [
    ["game:stick", null, "game:stick"],
    ["game:stick", "game:stick", "game:stick"],
    ["game:stick", null, "game:stick"],
  ];
  for (const [r, row] of expected.entries()) {
    for (const [col, code] of row.entries()) expect(await gridCell(c, r, col), `cell ${r},${col}`).toBe(code);
  }
  await expect(c.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-code", "game:ladder-stick-north");
  await expect(c.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-amount", "×3");
});

test("a wildcard recipe shows the variant of the item on its page: oak ladder from oak boards", async ({ page }) => {
  // grid/ladder.json, second recipe: "P_P	PSP	P_P", P = plank-* named wood, output ladder-wood-{wood}-north ×3.
  await openItem(page, "game:ladder-wood-oak-north");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Oak ladder");
  const c = await card(page, "madeBy", "recipes/grid/ladder.json");
  await pause(c);
  const P = "game:plank-oak";
  const S = "game:stick";
  const expected = [
    [P, null, P],
    [P, S, P],
    [P, null, P],
  ];
  for (const [r, row] of expected.entries()) {
    for (const [col, code] of row.entries()) expect(await gridCell(c, r, col), `cell ${r},${col}`).toBe(code);
  }
  await expect(c.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-amount", "×3");
});

test("flint lists the flint knife blade knapping recipe under its uses", async ({ page }) => {
  await page.goto("./");
  const results = await search(page, "flint");
  // lang: "item-flint": "Flint"
  await expect(results.first()).toHaveAttribute("data-code", "game:flint");
  await results.first().click();
  // knapping/flint-knife.json: ingredient flint, output knifeblade-flint.
  const c = await card(page, "usedIn", "recipes/knapping/flint-knife.json");
  await expect(c.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-code", "game:knifeblade-flint");
  await expect(page.locator('[data-group="usedIn"][data-type="knapping"] h3')).toContainText("Knapping");
});

test("birch boards are used by the ladder recipe that asks for plank-*", async ({ page }) => {
  await openItem(page, "game:plank-birch");
  const c = await card(page, "usedIn", "recipes/grid/ladder.json");
  await pause(c);
  // The card shows the birch variant: its corner cell holds birch boards.
  expect(await gridCell(c, 0, 0)).toBe("game:plank-birch");
  await expect(c.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-code", "game:ladder-wood-birch-north");
});

test("the flint knife blade knapping pattern fills exactly the cells in knapping/flint-knife.json", async ({ page }) => {
  await openItem(page, "game:knifeblade-flint");
  const c = await card(page, "madeBy", "recipes/knapping/flint-knife.json");
  // prettier-ignore
  const pattern = [
    "_____#____",
    "_____#____",
    "____##____",
    "____##____",
    "____##____",
    "___###____",
    "___###____",
    "___###____",
    "___###____",
    "___###____",
  ];
  expect((await filledCells(c)).sort()).toEqual(cellsOf(pattern).sort());
  await expect(c.locator(".voxels [data-filled]")).toHaveCount(100);
});

test("the copper pickaxe head smithing pattern matches smithing/pickaxe.json", async ({ page }) => {
  await openItem(page, "game:pickaxehead-copper");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Copper pickaxe head");
  const c = await card(page, "madeBy", "recipes/smithing/pickaxe.json");
  await pause(c);
  const pattern = ["_____####_____", "__##########__", "#####____#####"];
  expect((await filledCells(c)).sort()).toEqual(cellsOf(pattern).sort());
  await expect(c.locator('[data-code="game:ingot-copper"]').first()).toBeVisible();
});

test("the clay bowl has two layers and the layer selector switches between them", async ({ page }) => {
  // clayforming/bowl.json: a full 5x5 base, then a ring.
  await openItem(page, "game:bowl-blue-raw");
  const c = await card(page, "madeBy", "recipes/clayforming/bowl.json");
  const layers = c.getByRole("group", { name: "Layer" }).getByRole("button");
  await expect(layers).toHaveCount(2);
  expect((await filledCells(c)).sort()).toEqual(cellsOf(["#####", "#####", "#####", "#####", "#####"]).sort());
  await layers.nth(1).click();
  await expect(layers.nth(1)).toHaveAttribute("aria-pressed", "true");
  expect((await filledCells(c)).sort()).toEqual(cellsOf(["#####", "#___#", "#___#", "#___#", "#####"]).sort());
});

test("tin bronze shows its alloy ratios from alloy/tinbronze.json", async ({ page }) => {
  await page.goto("./");
  const results = await search(page, "tin bronze ingot");
  await expect(results.first()).toHaveAttribute("data-code", "game:ingot-tinbronze");
  await results.first().click();
  const c = await card(page, "madeBy", "recipes/alloy/tinbronze.json");
  // tin 0.08-0.12, copper 0.88-0.92
  await expect(c.locator("tr", { has: page.locator('[data-code="game:ingot-copper"]') }).locator("[data-ratio]")).toHaveText("88–92%");
  await expect(c.locator("tr", { has: page.locator('[data-code="game:ingot-tin"]') }).locator("[data-ratio]")).toHaveText("8–12%");
});

test("leather from a huge hide takes 10 litres of strong tannin sealed for 108 hours and gives 5", async ({ page }) => {
  // barrel/leather.json, fourth recipe.
  await openItem(page, "game:leather-normal-plain");
  const c = await card(page, "madeBy", "recipes/barrel/leather.json", ':has([data-code="game:hide-prepared-huge"])');
  await expect(c.locator('[data-code="game:strongtanninportion"]')).toHaveAttribute("data-amount", "10 L");
  await expect(c.locator("[data-seal-hours]")).toHaveText("Seal for 108 hours");
  await expect(c.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-code", "game:leather-normal-plain");
  await expect(c.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-amount", "×5");
});

test("a recipe with variants cycles, steps and pauses", async ({ page }) => {
  // Stick is in every wood variant of the second ladder recipe, so all of them cycle.
  await openItem(page, "game:stick");
  const c = await card(page, "usedIn", "recipes/grid/ladder.json", ':has(button[aria-label="Next variant"])');
  const plank = async () => (await gridCell(c, 0, 0)) ?? "";

  // It changes on its own...
  const first = await plank();
  expect(first).toMatch(/^game:plank-/);
  await expect.poll(plank, { timeout: 6000 }).not.toBe(first);

  // ...stops when paused...
  await pause(c);
  const held = await plank();
  await page.waitForTimeout(3500);
  expect(await plank()).toBe(held);

  // ...and steps by hand, in both directions.
  const position = c.locator("[data-position]");
  const at = Number(await position.getAttribute("data-position"));
  await c.getByRole("button", { name: "Next variant" }).click();
  expect(await plank()).not.toBe(held);
  expect(await plank()).toMatch(/^game:plank-/);
  await expect(position).not.toHaveAttribute("data-position", String(at));
  await c.getByRole("button", { name: "Previous variant" }).click();
  expect(await plank()).toBe(held);
});

test("with reduced motion a recipe starts paused", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await openItem(page, "game:stick");
  const c = await card(page, "usedIn", "recipes/grid/ladder.json", ':has(button[aria-label="Next variant"])');
  await expect(c.locator("button[data-pause]")).toHaveAttribute("aria-pressed", "true");
  const held = await gridCell(c, 0, 0);
  await page.waitForTimeout(3500);
  expect(await gridCell(c, 0, 0)).toBe(held);
});

test("search ranks Copper ingot first for `ingot cop` and works from the keyboard", async ({ page }) => {
  await page.goto("./");
  await page.getByLabel("Search items").focus();
  await page.keyboard.type("ingot cop");
  const first = page.getByTestId("results").locator("a[data-code]").first();
  await expect(first).toHaveAttribute("data-code", "game:ingot-copper");
  await expect(first).toContainText("Copper ingot");
  await expect(first).toContainText("game:ingot-copper");
  // Tab past the Search button and the version picker to the first result.
  for (let i = 0; i < 10; i++) {
    await page.keyboard.press("Tab");
    if (await first.evaluate((el) => el === document.activeElement)) break;
  }
  await expect(first).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(new RegExp(`#/${V}/item/game:ingot-copper$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Copper ingot");
});

test("a torch is not a use of the fishing pole, whose line is `{line}`: flax twine or rope", async ({ page }) => {
  // grid/fishinggear.json, first recipe: S = stick, R = {line}, flaxtwine or rope. As a
  // pattern `{line}` would match any item.
  await openItem(page, "game:rope");
  const c = await card(page, "usedIn", "recipes/grid/fishinggear.json");
  await expect(c.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-code", /^game:fishingpole-simple-/);

  await openItem(page, "game:torch-basic-lit-up");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Torch");
  await expect(page.locator('[data-item="game:torch-basic-lit-up"]')).toBeVisible();
  await page.waitForLoadState("networkidle");
  await expect(page.locator('[data-recipe-id*="recipes/grid/fishinggear.json"]')).toHaveCount(0);
});

test("without an icon a search result shows the initials of its name", async ({ page }) => {
  await page.goto("./");
  const results = await search(page, "Crude ladder");
  await expect(results.first().locator("[data-icon-placeholder]")).toHaveText("CL");
});

test("the water wheel is built in place, stage by stage, with the wood of its support beams", async ({ page }) => {
  // blocktypes/mechanics/waterwheel.json: RightClickConstructable, stages 1 (supportbeam-* ×16,
  // storeWildCard wood), 2 (plank-{wood} ×48, resin ×4), ... and a last stage "Launch".
  await openItem(page, "game:waterwheel-3m-north");
  const c = await card(page, "madeBy", "construction|game:waterwheel-3m-north|");
  await pause(c);
  await expect(c).toHaveAttribute("data-shape", "construction");
  await expect(c.locator("[data-stage]")).toHaveCount(7);
  await expect(c.locator('[data-stage="0"]')).toContainText("Place it");
  await expect(c.locator('[data-stage="6"]')).toContainText("Right-click: Launch");

  const beam = await c.locator('[data-stage="1"] [data-input="0"] [data-code]').getAttribute("data-code");
  const wood = beam!.replace("game:supportbeam-", "");
  expect(["oak", "maple", "kapok", "redwood", "ebony", "walnut", "purpleheart"]).toContain(wood);
  await expect(c.locator('[data-stage="1"] [data-input="0"] [data-code]')).toHaveAttribute("data-amount", "×16");
  const plank = c.locator('[data-stage="2"] [data-input="1"] [data-code]');
  await expect(plank).toHaveAttribute("data-code", `game:plank-${wood}`);
  await expect(plank).toHaveAttribute("data-amount", "×48");

  // Each stage's label is above its items, whatever the length of their names.
  for (let s = 1; s <= 5; s++) {
    const label = await c.locator(`[data-stage="${s}"] .step`).boundingBox();
    const first = await c.locator(`[data-stage="${s}"] [data-input]`).first().boundingBox();
    expect(label!.y + label!.height, `stage ${s}`).toBeLessThanOrEqual(first!.y);
  }

  // Planks are asked for twice (48 in stage 2 and 48 in stage 5).
  await expect(c.locator(`[data-totals] [data-code="game:plank-${wood}"]`)).toHaveAttribute("data-amount", "×96");
});

test("wet sinew cures into dry sinew in two days, shown on both items' pages", async ({ page }) => {
  // butchering/itemtypes/resource/sinew.json, sinew-wet: Cure, freshHours 0, transitionHours 48,
  // into sinew-dry, ratio 1.
  await openItem(page, "butchering:sinew-wet");
  const used = await card(page, "usedIn", "curing|butchering:sinew-wet|");
  await expect(used).toHaveAttribute("data-shape", "transition");
  await expect(used.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-code", "butchering:sinew-dry");
  await expect(used.locator('[data-input="0"] [data-code]')).toHaveAttribute("data-code", "butchering:sinew-wet");
  await expect(used.locator("[data-transition]")).toHaveAttribute("data-transition", "cure");
  await expect(used.locator("[data-transition]")).toContainText("Cures after 2 days of game time");
  await expect(page.locator('[data-group="usedIn"][data-type="curing"] h3')).toContainText("Curing");

  await openItem(page, "butchering:sinew-dry");
  const made = await card(page, "madeBy", "curing|butchering:sinew-wet|");
  await expect(made.locator('[data-input="0"] [data-code]')).toHaveAttribute("data-code", "butchering:sinew-wet");
});

test("raw red meat is smoked on the smoking rack in four hours, shown on the meat's, the rack's and the smoked meat's pages", async ({ page }) => {
  // butchering/patches/items/food/smoked.json: game:itemtypes/food/redmeat.json *-raw
  // transformsWhenSmoked butchering:smoked-none-redmeat; blocktypes/smokingrack.json;
  // BlockEntityMeatHook.smokingTimeHours = 4 (decompiled Butchering 1.14.3).
  await openItem(page, "game:redmeat-raw");
  const used = await card(page, "usedIn", "smoking|game:redmeat-raw|");
  await expect(used.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-code", "butchering:smoked-none-redmeat");
  await expect(used.locator("[data-transition]")).toContainText("Smoked after 4 hours of game time");
  await expect(used.locator("[data-station] [data-code]").first()).toHaveAttribute("data-code", /^butchering:smokingrack-/);
  await expect(used).toContainText("A burning firepit directly below the rack");

  await openItem(page, "butchering:smoked-none-redmeat");
  await card(page, "madeBy", "smoking|game:redmeat-raw|");

  await openItem(page, "butchering:smokingrack-copper-north");
  await card(page, "usedIn", "smoking|game:redmeat-raw|");
});

test("raw cheese ripens, and perishing into rot is on the food's page but only counted on rot's", async ({ page }) => {
  // itemtypes/food/rawcheese.json: Ripen 0 + 336 hours into cheese-cheddar-4slice (salted),
  // then Perish 360 + 168 hours into rot, ratio 4.
  await openItem(page, "game:rawcheese-salted");
  const ripen = await card(page, "usedIn", "ripening|game:rawcheese-salted|0");
  await expect(ripen.locator("[data-transition]")).toContainText("Ripens after 14 days of game time");
  const rot = await card(page, "usedIn", "perishing|game:rawcheese-salted|1");
  await expect(rot.locator("[data-transition]")).toContainText("Spoils after 22 days of game time");
  await expect(rot.locator("[data-transition]")).toContainText("15 days before it starts, then 7 days");
  await expect(rot.locator('[data-output="0"] [data-code]')).toHaveAttribute("data-amount", "×4");

  await openItem(page, "game:rot");
  await expect(page.locator('[data-group="madeBy"][data-type="perishing"]')).toHaveCount(0);
  await expect(page.getByTestId("made-elsewhere")).toContainText(/\d+ items turn into this by perishing/);
});
