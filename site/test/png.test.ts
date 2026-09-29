import { readFileSync } from "node:fs";
import { deflateSync } from "node:zlib";
import { describe, expect, it } from "vitest";
import { decodePng, pixelAt } from "../e2e/png.ts";

const icon = (name: string) => readFileSync(new URL(`../e2e/icons/${name}`, import.meta.url));

/** A PNG with the given colour type and raw scanlines (each starting with its filter byte). */
function png(width: number, height: number, colour: number, raw: number[]): Buffer {
  const chunk = (type: string, body: Buffer) => {
    const len = Buffer.alloc(4);
    len.writeUInt32BE(body.length);
    // The decoder does not check CRCs.
    return Buffer.concat([len, Buffer.from(type, "latin1"), body, Buffer.alloc(4)]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr.set([8, colour, 0, 0, 0], 8);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(Buffer.from(raw))),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

describe("decodePng", () => {
  // Expected pixels read from the same files with Pillow.
  it("reads the committed steam engine icon", () => {
    const img = decodePng(icon("steamengine-standard-north.png"));
    expect([img.width, img.height]).toEqual([64, 64]);
    expect(pixelAt(img, 0, 0)).toEqual([0, 0, 0, 0]);
    expect(pixelAt(img, 32, 32)).toEqual([38, 32, 28, 255]);
    expect(pixelAt(img, 20, 40)).toEqual([50, 46, 43, 255]);
    expect(pixelAt(img, 45, 17)).toEqual([105, 91, 85, 255]);
  });

  it("reads the committed iron rod icon", () => {
    expect(pixelAt(decodePng(icon("rod-iron.png")), 32, 32)).toEqual([95, 85, 79, 255]);
  });

  it("undoes each filter type on RGB rows", () => {
    // Two pixels per row, (10,20,30) then (40,50,60), under filters 0 to 4 in turn.
    // Each filtered row was worked out by hand from the PNG specification.
    const rows = [
      [0, 10, 20, 30, 40, 50, 60], // none
      [1, 10, 20, 30, 30, 30, 30], // sub: minus the pixel to the left
      [2, 0, 0, 0, 0, 0, 0], // up: minus the pixel above
      [3, 5, 10, 15, 15, 15, 15], // average of left and above: (0+10)/2=5, (40+10)/2=25 -> 40-25=15
      [4, 0, 0, 0, 0, 0, 0], // Paeth: above is the predictor for both pixels
    ].flat();
    const img = decodePng(png(2, 5, 2, rows));
    for (let y = 0; y < 5; y++) {
      expect(pixelAt(img, 0, y), `row ${y}`).toEqual([10, 20, 30, 255]);
      expect(pixelAt(img, 1, y), `row ${y}`).toEqual([40, 50, 60, 255]);
    }
  });

  it("refuses what it cannot read", () => {
    expect(() => decodePng(png(1, 1, 3, [0, 0]))).toThrow(/unsupported/);
    expect(() => decodePng(Buffer.from("not a png at all"))).toThrow(/not a PNG/);
  });
});
