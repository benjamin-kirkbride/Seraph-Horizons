// The ore routes page against the real export's oreProcessing section. Its maths is the unit
// tests' (test/ore-routes.test.ts); here the page is checked against the export's own machine
// records: the rocker's figure for crushed, unsorted medium malachite (one item through the
// rocker, as RockerExport reads it from the mod) is what the page shows for the rocker without
// riddling. An export from before the section skips.
import { readFileSync } from "node:fs";
import type { RecipeExport } from "../src/lib/export.ts";
import { fmtUnits } from "../src/lib/ore-routes.ts";
import { V, expect, test } from "./fixtures.ts";

const exp = JSON.parse(readFileSync(process.env.RECIPE_EXPORT!, "utf8")) as RecipeExport;

test.describe("ore routes", () => {
  test.skip(!exp.oreProcessing?.ores.some((o) => o.ore === "malachite"), "the export has no ore processing figures for malachite");

  test("opens from the header with the glossary collapsed, and shows malachite's routes", async ({ page }) => {
    await page.goto(`./#/${V}`);
    await page.getByRole("link", { name: "Ore routes", exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`#/${V}/ores$`));
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Ore routes");
    const glossary = page.getByTestId("ores-glossary");
    await expect(glossary).not.toHaveAttribute("open");
    await expect(glossary.getByText("Concentrate", { exact: true })).toBeHidden();
    await expect(page.getByTestId("ore-picker")).toHaveValue("malachite");
    await expect(page.getByTestId("ore-tree")).toContainText("Raw ore, as mined");
    await expect(page.getByTestId("ores-summary")).toContainText("(100 %)");
  });

  test("shows the export's own rocker figure for unsorted crushed ore", async ({ page }) => {
    const rocker = exp.recipes.find((r) => r.type === "rocker" && r.ingredients[0]?.code === "seraphhorizons:crushedore-malachite-medium");
    test.skip(!rocker, "the export has no rocker record for crushed medium malachite");
    const units = rocker!.outputs.find((o) => o.code === "seraphhorizons:concentrate-malachite")!.quantity * exp.oreProcessing!.concentrateUnits;
    await page.goto(`./#/${V}/ores?ore=malachite`);
    const step = page.locator('[data-step="rocker"]').filter({ hasText: "without riddling" });
    await expect(step).toContainText(`Smelt now: ${fmtUnits(units)} units of copper`);
  });

  test("keeps the ore and grade in the address", async ({ page }) => {
    await page.goto(`./#/${V}/ores`);
    await page.getByTestId("ore-picker").selectOption("galena");
    await expect(page).toHaveURL(new RegExp(`#/${V}/ores\\?ore=galena$`));
    await page.getByTestId("grade-picker").selectOption("poor");
    await expect(page).toHaveURL(new RegExp(`#/${V}/ores\\?ore=galena&grade=poor$`));
    await expect(page.getByTestId("ore-tree")).toContainText("Bone-ash cupel");
  });
});
