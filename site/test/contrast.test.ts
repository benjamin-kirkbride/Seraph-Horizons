import { describe, expect, it } from "vitest";
import { contrastRatio, parseColor, relativeLuminance } from "../src/lib/contrast.ts";

// Expected values worked out by hand from the WCAG 2.x definitions.
describe("relative luminance", () => {
  it("is 0 for black and 1 for white", () => {
    expect(relativeLuminance([0, 0, 0])).toBe(0);
    expect(relativeLuminance([255, 255, 255])).toBeCloseTo(1, 10);
  });

  it("weights green most and blue least", () => {
    expect(relativeLuminance([255, 0, 0])).toBeCloseTo(0.2126, 10);
    expect(relativeLuminance([0, 255, 0])).toBeCloseTo(0.7152, 10);
    expect(relativeLuminance([0, 0, 255])).toBeCloseTo(0.0722, 10);
  });

  it("uses the linear segment below the threshold", () => {
    // 10/255 = 0.0392 is under 0.04045, so it is divided by 12.92: 0.003035.
    expect(relativeLuminance([10, 10, 10])).toBeCloseTo(0.0030353, 6);
  });

  it("gives #777777 about 0.184", () => {
    // ((0.4667 + 0.055) / 1.055) ^ 2.4 = 0.1845
    expect(relativeLuminance([0x77, 0x77, 0x77])).toBeCloseTo(0.1845, 3);
  });
});

describe("contrast ratio", () => {
  const white = relativeLuminance([255, 255, 255]);
  const black = relativeLuminance([0, 0, 0]);

  it("is 21 for black on white and 1 for white on white", () => {
    expect(contrastRatio(black, white)).toBeCloseTo(21, 10);
    expect(contrastRatio(white, white)).toBe(1);
  });

  it("does not depend on order", () => {
    expect(contrastRatio(white, black)).toBe(contrastRatio(black, white));
  });

  it("puts #777777 on white just under 4.5", () => {
    const r = contrastRatio(relativeLuminance([0x77, 0x77, 0x77]), white);
    expect(r).toBeCloseTo(4.48, 2);
    expect(r).toBeLessThan(4.5);
  });
});

describe("parseColor", () => {
  it("reads the forms getComputedStyle returns", () => {
    expect(parseColor("rgb(46, 44, 39)")).toEqual({ rgb: [46, 44, 39], alpha: 1 });
    expect(parseColor("rgba(0, 0, 0, 0)")).toEqual({ rgb: [0, 0, 0], alpha: 0 });
    expect(parseColor("rgb(10 20 30 / 50%)")).toEqual({ rgb: [10, 20, 30], alpha: 0.5 });
    expect(parseColor("#2E2C27")).toEqual({ rgb: [46, 44, 39], alpha: 1 });
  });

  it("returns null for what it does not know", () => {
    expect(parseColor("transparent")).toBeNull();
    expect(parseColor("oklch(0.5 0.1 100)")).toBeNull();
  });
});
