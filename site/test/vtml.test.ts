import { describe, expect, it } from "vitest";
import { decodeEntities, parseVtml, vtmlText } from "../src/lib/vtml.ts";

describe("parseVtml", () => {
  it("builds bold, italic and line breaks", () => {
    expect(parseVtml("A <strong>bold</strong> and <i>slanted</i><br>word")).toEqual([
      { t: "text", text: "A " },
      { t: "b", children: [{ t: "text", text: "bold" }] },
      { t: "text", text: " and " },
      { t: "i", children: [{ t: "text", text: "slanted" }] },
      { t: "br" },
      { t: "text", text: "word" },
    ]);
  });

  it("drops <script> with its content", () => {
    expect(parseVtml("before<script>alert(1)</script>after")).toEqual([{ t: "text", text: "beforeafter" }]);
    expect(parseVtml("x<SCRIPT type='a'>evil()")).toEqual([{ t: "text", text: "x" }]);
  });

  it("keeps only http(s) links and turns handbook item links into item codes", () => {
    expect(parseVtml('<a href="javascript:alert(1)">click</a>')).toEqual([{ t: "text", text: "click" }]);
    expect(parseVtml('<a href="https://example.org/x">site</a>')).toEqual([
      { t: "link", href: "https://example.org/x", children: [{ t: "text", text: "site" }] },
    ]);
    expect(parseVtml('<a href="handbook://item-strongtanninportion">tannin</a>')).toEqual([
      { t: "item", code: "game:strongtanninportion", children: [{ t: "text", text: "tannin" }] },
    ]);
    expect(parseVtml('<a href="handbook://block-mymod:press">press</a>')[0]).toMatchObject({ t: "item", code: "mymod:press" });
    expect(parseVtml('<a href="handbooksearch://dye">dyes</a>')).toEqual([{ t: "text", text: "dyes" }]);
  });

  it("accepts a hex font colour and ignores anything else in it", () => {
    expect(parseVtml('<font color="#84ff84">green</font>')).toEqual([
      { t: "color", color: "#84ff84", children: [{ t: "text", text: "green" }] },
    ]);
    expect(parseVtml('<font color="red;background:url(x)">plain</font>')).toEqual([{ t: "text", text: "plain" }]);
    expect(parseVtml('<font weight="bold">heavy</font>')).toEqual([{ t: "b", children: [{ t: "text", text: "heavy" }] }]);
  });

  it("keeps the text of unknown tags and ignores stray closing tags", () => {
    expect(parseVtml("<hk>F</hk> then </strong>done <icon name='x'/>")).toEqual([{ t: "text", text: "F then done " }]);
  });

  it("treats a lone < as text", () => {
    expect(parseVtml("1 < 2 & 3 > 2")).toEqual([{ t: "text", text: "1 < 2 & 3 > 2" }]);
  });
});

describe("decodeEntities and vtmlText", () => {
  it("decodes named and numeric entities, leaving unknown ones", () => {
    expect(decodeEntities("&lt;b&gt; &amp; &#65;&#x42; &bogus;")).toBe("<b> & AB &bogus;");
  });
  it("an encoded tag stays text", () => {
    expect(parseVtml("&lt;script&gt;")).toEqual([{ t: "text", text: "<script>" }]);
  });
  it("flattens to plain text", () => {
    expect(vtmlText("Line<br><strong>two</strong>")).toBe("Line\ntwo");
  });
});
