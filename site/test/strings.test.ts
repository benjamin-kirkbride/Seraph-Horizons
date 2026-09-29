import { describe, expect, it } from "vitest";
import { t } from "../src/lib/strings.ts";

describe("densityHint", () => {
  it("says items at or below water's density float and heavier ones sink", () => {
    expect(t.densityHint(400)).toMatch(/^Floats/);
    expect(t.densityHint(1000)).toMatch(/^Floats/);
    expect(t.densityHint(1001)).toMatch(/^Sinks/);
    expect(t.densityHint(9999)).toMatch(/^Sinks/);
  });
});
