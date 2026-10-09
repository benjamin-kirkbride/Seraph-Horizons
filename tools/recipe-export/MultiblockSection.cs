using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace SeraphHorizons.RecipeExport;

/// <summary>
/// Writes `multiblocks`: every structure a player builds block by block and a block checks
/// against a layout (the game's <c>multiblockStructure</c> attribute, which vanilla, exlib and
/// so smex and ppex all use), with the shape of a block that fits each cell.
/// docs/recipe-browser/multiblocks.md describes the data.
/// </summary>
public static class MultiblockSection
{
    /// <summary>The attribute every layout is read from (the game's MultiblockStructure).</summary>
    public const string StructureAttribute = "multiblockStructure";

    /// <summary>
    /// A layout that comes in sizes (the pack's crucible furnace): <c>{ "label", "default",
    /// "sizes": [{ "label", "multiblockStructure" }] }</c> on the block that checks it.
    /// </summary>
    public const string SizesAttribute = "multiblockSizes";

    /// <summary>How many of the blocks a cell accepts are listed; the rest are counted.</summary>
    public const int MaxAccepts = 24;

    private static readonly AssetLocation Air = new("game:air");

    public static void Fill(ICoreServerAPI api, JObject root)
    {
        using var english = new EnglishLocale();
        var mods = new ModIndex(api);
        var blocks = api.World.Blocks.Where(b => b?.Code != null && !b.IsMissing).ToList();

        // One structure per distinct layout: a block's variants (its facings, its tiers)
        // usually carry the same one.
        var byLayout = new Dictionary<string, List<Block>>();
        var order = new List<string>();
        foreach (var b in blocks)
        {
            var key = LayoutKey(b);
            if (key == null) continue;
            if (!byLayout.TryGetValue(key, out var list))
            {
                byLayout[key] = list = new();
                order.Add(key);
            }
            list.Add(b);
        }

        var structures = new JArray();
        var shapes = new SortedDictionary<string, JToken>(StringComparer.Ordinal);
        var ids = new HashSet<string>();
        var matchCache = new Dictionary<string, List<Block>>();
        foreach (var key in order)
        {
            var group = byLayout[key];
            var owner = Preferred(group);
            var sizes = Sizes(owner);
            if (sizes.Count == 0) continue;
            var parts = new List<(string Pattern, JObject Part)>();
            var sizeArray = new JArray();
            foreach (var (label, layout) in sizes)
            {
                var cells = Cells(api, blocks, layout, parts, shapes, matchCache);
                if (cells == null) { sizeArray = null; break; }
                var size = new JObject();
                if (label != null) size["label"] = label;
                size["cells"] = cells;
                sizeArray.Add(size);
            }
            if (sizeArray == null || sizeArray.Count == 0) continue;

            var id = owner.Code.Domain + ":" + owner.Code.FirstCodePart();
            for (var n = 2; !ids.Add(id); n++) id = owner.Code.Domain + ":" + owner.Code.FirstCodePart() + "-" + n;
            var record = new JObject
            {
                ["id"] = id,
                ["code"] = owner.Code.ToString(),
                ["name"] = ItemRecords.Name(owner),
                ["mod"] = mods.ModForCollectible(owner),
                ["codes"] = new JArray(group.Select(b => b.Code.ToString()).Order(StringComparer.Ordinal)),
                ["parts"] = new JArray(parts.Select(p => p.Part)),
                ["sizes"] = sizeArray,
            };
            var meta = owner.Attributes?[SizesAttribute];
            if (meta is { Exists: true })
            {
                if (meta["label"].AsString() is { Length: > 0 } sizeLabel) record["sizeLabel"] = sizeLabel;
                var def = meta["default"].AsInt(0);
                if (def > 0 && def < sizeArray.Count) record["defaultSize"] = def;
            }
            structures.Add(record);
        }

        root["multiblocks"] = new JObject
        {
            ["structures"] = new JArray(structures.OrderBy(s => (string)s["id"]!, StringComparer.Ordinal)),
            ["shapes"] = new JObject(shapes.Select(kv => new JProperty(kv.Key, kv.Value))),
        };
    }

    /// <summary>The block's layout as canonical JSON, the key it is grouped by; null when it has none.</summary>
    private static string? LayoutKey(Block b)
    {
        var a = b.Attributes;
        if (a == null) return null;
        var sizes = a[SizesAttribute];
        if (sizes is { Exists: true }) return "sizes|" + sizes.Token.ToString(Formatting.None);
        var s = a[StructureAttribute];
        if (s is not { Exists: true } || s.Token is not JObject o || o["offsets"] is not JArray) return null;
        return "one|" + o.ToString(Formatting.None);
    }

    private static List<(string? Label, JObject Layout)> Sizes(Block owner)
    {
        var list = new List<(string?, JObject)>();
        var sizes = owner.Attributes[SizesAttribute];
        if (sizes is { Exists: true })
        {
            if (sizes["sizes"].Token is JArray arr)
                foreach (var s in arr.OfType<JObject>())
                    if (s[StructureAttribute] is JObject layout) list.Add(((string?)s["label"], layout));
            return list;
        }
        if (owner.Attributes[StructureAttribute].Token is JObject one) list.Add((null, one));
        return list;
    }

    /// <summary>
    /// The layout's cells as [x, y, z, part], parts added to <paramref name="parts"/> by pattern;
    /// null when the layout cannot be read.
    /// </summary>
    private static JArray? Cells(ICoreServerAPI api, List<Block> blocks, JObject layout,
        List<(string Pattern, JObject Part)> parts, SortedDictionary<string, JToken> shapes, Dictionary<string, List<Block>> matchCache)
    {
        if (layout["blockNumbers"] is not JObject numbers || layout["offsets"] is not JArray offsets) return null;
        var chosen = parts.Where(p => p.Part["block"] != null).Select(p => (string)p.Part["block"]!).ToHashSet();
        var partOfNumber = new Dictionary<int, int>();
        foreach (var prop in numbers.Properties())
        {
            if (prop.Value.Type != JTokenType.Integer) continue;
            var number = (int)prop.Value;
            var pattern = new AssetLocation(prop.Name).ToString();
            var index = parts.FindIndex(p => p.Pattern == pattern);
            if (index < 0)
            {
                index = parts.Count;
                parts.Add((pattern, Part(api, blocks, pattern, shapes, matchCache, chosen)));
            }
            partOfNumber[number] = index;
        }
        var cells = new JArray();
        foreach (var o in offsets.OfType<JObject>())
        {
            int? x = (int?)o["x"], y = (int?)o["y"], z = (int?)o["z"], w = (int?)o["w"];
            if (x == null || y == null || z == null || w == null) continue;
            if (!partOfNumber.TryGetValue(w.Value, out var part)) continue;
            cells.Add(new JArray(x.Value, y.Value, z.Value, part));
        }
        return cells;
    }

    private static JObject Part(ICoreServerAPI api, List<Block> blocks, string pattern, SortedDictionary<string, JToken> shapes, Dictionary<string, List<Block>> matchCache, ISet<string> chosen)
    {
        var loc = new AssetLocation(pattern);
        if (!matchCache.TryGetValue(pattern, out var matches))
        {
            matches = blocks.Where(b => Matches(loc, b.Code)).ToList();
            matchCache[pattern] = matches;
        }
        var air = matches.Any(b => b.Code.Equals(Air)) || Matches(loc, Air);
        var solid = matches.Where(b => !b.Code.Equals(Air)).ToList();
        var part = new JObject { ["pattern"] = pattern };
        if (air) part["air"] = true;
        if (solid.Count > 0)
        {
            // A block another cell of the structure is already drawn with, if this cell takes
            // it too (the smoke stack's "any brick" cells take its refractory bricks).
            var rep = solid.FirstOrDefault(b => chosen.Contains(b.Code.ToString())) ?? Preferred(solid);
            var code = rep.Code.ToString();
            chosen.Add(code);
            part["block"] = code;
            var name = ItemRecords.Name(rep);
            if (!ItemRecords.IsLangKey(name, rep)) part["name"] = name;
            part["accepts"] = new JArray(solid.Take(MaxAccepts).Select(b => b.Code.ToString()));
            if (solid.Count > MaxAccepts) part["acceptsMore"] = solid.Count - MaxAccepts;
            if (!shapes.ContainsKey(code)) shapes[code] = BlockShape(api, rep);
        }
        return part;
    }

    private static bool Matches(AssetLocation pattern, AssetLocation code)
    {
        try { return WildcardUtil.Match(pattern, code); }
        catch (ArgumentException) { return false; }   // a malformed @regex
    }

    /// <summary>The block a cell is drawn with: a north-facing variant when there is one, else the first.</summary>
    private static Block Preferred(IReadOnlyList<Block> blocks)
    {
        static bool North(Block b) => b.Variant.Values.Any(v => v is "north" or "n");
        return blocks.FirstOrDefault(North) ?? blocks[0];
    }

    /// <summary>
    /// How a block is drawn: <c>{ "draw": "cube" }</c>, <c>{ "draw": "none" }</c>, or
    /// <c>{ "draw": "shape", "shapes": [...] }</c>, its shape and overlays with their elements
    /// (voxels, the shape file's frame) and the block's rotation of each. A block with a
    /// renderer of its own is drawn with its JSON shape all the same.
    /// </summary>
    public static JObject BlockShape(ICoreAPI api, Block b)
    {
        switch (b.DrawType)
        {
            case EnumDrawType.Empty:
                return new JObject { ["draw"] = "none" };
            case EnumDrawType.JSON:
            case EnumDrawType.JSONAndWater:
            case EnumDrawType.JSONAndSnowLayer:
                break;
            default:
                return new JObject { ["draw"] = "cube" };
        }
        var composites = new List<CompositeShape>();
        if (b.Shape?.Base != null) composites.Add(b.Shape);
        if (b.Shape?.Overlays != null) composites.AddRange(b.Shape.Overlays.Where(o => o?.Base != null));
        var list = new JArray();
        var loaded = false;
        foreach (var cs in composites)
        {
            var path = cs.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            Shape? shape;
            try { shape = Shape.TryGet(api, path); }
            catch (Exception) { shape = null; }
            if (shape?.Elements == null) continue;
            loaded = true;
            var elements = Elements(shape.Elements);
            if (elements.Count == 0) continue;
            var o = new JObject { ["elements"] = elements };
            if (cs.rotateX != 0) o["rotateX"] = Json.Round(cs.rotateX);
            if (cs.rotateY != 0) o["rotateY"] = Json.Round(cs.rotateY);
            if (cs.rotateZ != 0) o["rotateZ"] = Json.Round(cs.rotateZ);
            if (cs.offsetX != 0 || cs.offsetY != 0 || cs.offsetZ != 0)
                o["offset"] = new JArray(Json.Round(cs.offsetX), Json.Round(cs.offsetY), Json.Round(cs.offsetZ));
            if (cs.Scale != 1) o["scale"] = Json.Round(cs.Scale);
            list.Add(o);
        }
        // A shape without elements is an invisible block (the upper half of a door); one that
        // did not load is drawn as a cube rather than not at all.
        if (list.Count == 0) return new JObject { ["draw"] = loaded ? "none" : "cube" };
        return new JObject { ["draw"] = "shape", ["shapes"] = list };
    }

    private static readonly string[] FaceNames = { "north", "east", "south", "west", "up", "down" };

    private static JArray Elements(IEnumerable<ShapeElement> elements)
    {
        var arr = new JArray();
        foreach (var e in elements)
        {
            if (e?.From == null || e.To == null) continue;
            var o = new JObject
            {
                ["name"] = e.Name ?? "",
                ["from"] = Vec(e.From),
                ["to"] = Vec(e.To),
            };
            if (e.RotationOrigin != null && e.RotationOrigin.Any(v => v != 0)) o["rotationOrigin"] = Vec(e.RotationOrigin);
            if (e.RotationX != 0) o["rotationX"] = Round(e.RotationX);
            if (e.RotationY != 0) o["rotationY"] = Round(e.RotationY);
            if (e.RotationZ != 0) o["rotationZ"] = Round(e.RotationZ);
            // FacesResolved is in BlockFacing order (north, east, south, west, up, down).
            var resolved = e.FacesResolved;
#pragma warning disable CS0618 // Faces: read only when the shape was not resolved
            var byName = e.Faces;
#pragma warning restore CS0618
            List<string>? on = null;
            if (resolved is { Length: 6 })
                on = FaceNames.Where((_, i) => resolved[i] is { Enabled: true }).ToList();
            else if (byName != null)
                on = FaceNames.Where(f => byName.TryGetValue(f, out var face) && face is { Enabled: true }).ToList();
            if (on != null && on.Count < FaceNames.Length) o["faces"] = new JArray(on);
            if (e.Children is { Length: > 0 }) o["children"] = Elements(e.Children);
            arr.Add(o);
        }
        return arr;
    }

    private static JArray Vec(double[] v) => new(v.Take(3).Select(Round));

    private static double Round(double d) => double.IsFinite(d) ? Math.Round(d, 4) : 0;
}
