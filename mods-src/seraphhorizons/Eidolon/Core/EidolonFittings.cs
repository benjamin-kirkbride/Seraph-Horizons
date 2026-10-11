namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>The eidolon's five layers of material, in build order: each is fitted onto the torso in
/// the gantry, complete before the next takes anything, and stripped again outermost first
/// (<see cref="EidolonFittings.StripOrder"/>). The torso (a grid recipe) and the mind (a temporal
/// gear) are not layers: neither is ever stripped.</summary>
public enum FittingLayer
{
    /// <summary>The wooden bones: support beams of one wood, with the frame's Jonas parts.</summary>
    Bones,
    /// <summary>Joints, bars and clamps: rods and nails and strips of one metal, with the moving
    /// mechanism's Jonas parts.</summary>
    Joints,
    /// <summary>The armour plates: metal plates of one metal.</summary>
    Plates,
    /// <summary>The chest sash and the fauld: leather or cloth of one colour.</summary>
    Sash,
    /// <summary>The chain skirt: chain of one metal.</summary>
    Skirt,
}

/// <summary>One ingredient of a fitting: an item's (or for a support beam, a block's) full code and
/// how many of it the layer takes.</summary>
public readonly record struct FittingIngredient(string Code, int Count, bool IsBlock = false);

/// <summary>
/// The fittings' rules (the eidolon fittings brief; Eidolon/README.md): the layers, the materials
/// each allows, the item each material is fitted as, the texture each draws with, and each layer's
/// bill. Game-independent: codes are full (<c>domain:path</c>) strings.
///
/// A material is written as a short code, stored as is in an eidolon's record
/// (<see cref="EidolonBuild"/>):
/// <list type="bullet">
/// <item>bones: the wood, the support beam's <c>wood</c> state (<c>oak</c>, <c>aged</c>, ...);</item>
/// <item>joints, plates, skirt: the game's metal code (<c>iron</c>, <c>steel</c>,
///   <c>stainlesssteel</c>, plates also <c>brass</c>);</item>
/// <item>sash: <c>leather-{colour}</c> or <c>cloth-{colour}</c>, the colour the item's own state.</item>
/// </list>
/// </summary>
public static class EidolonFittings
{
    public const string Iron = "iron";
    public const string Steel = "steel";
    public const string Stainless = "stainlesssteel";
    public const string Brass = "brass";

    public const string Leather = "leather";
    public const string Cloth = "cloth";

    /// <summary>The layers in build order (innermost first).</summary>
    public static readonly IReadOnlyList<FittingLayer> Layers =
        [FittingLayer.Bones, FittingLayer.Joints, FittingLayer.Plates, FittingLayer.Sash, FittingLayer.Skirt];

    /// <summary>The layers in the order they come off a docked eidolon (outermost first).</summary>
    public static readonly IReadOnlyList<FittingLayer> StripOrder = Layers.Reverse().ToList();

    /// <summary>The woods the game's support beams come in (<c>worldproperties/block/wood</c> plus
    /// the beam's own <c>aged</c> and <c>veryaged</c>), for lists such as the site's. A beam of any
    /// other wood a mod adds fits too (<see cref="Allows"/>); only rotten wood does not.</summary>
    public static readonly IReadOnlyList<string> KnownWoods =
    [
        "birch", "oak", "maple", "pine", "acacia", "kapok", "baldcypress", "larch", "redwood", "ebony",
        "walnut", "purpleheart", "aged", "veryaged",
    ];

    /// <summary>Woods too far gone to be bones.</summary>
    public static readonly IReadOnlySet<string> RottenWoods = new HashSet<string>(StringComparer.Ordinal) { "rotten", "veryrotten" };

    public static readonly IReadOnlyList<string> JointMetals = [Iron, Steel, Stainless];
    public static readonly IReadOnlyList<string> PlateMetals = [Iron, Steel, Stainless, Brass];
    public static readonly IReadOnlyList<string> SkirtMetals = [Iron, Steel, Stainless];

    /// <summary>The game's leather colours (<c>itemtypes/resource/leather.json</c>; normal leather
    /// only, not sturdy).</summary>
    public static readonly IReadOnlyList<string> LeatherColours =
        ["plain", "orange", "black", "red", "blue", "purple", "pink", "white", "yellow", "gray", "green"];

    /// <summary>The game's cloth colours (<c>itemtypes/resource/cloth.json</c>) but mordant, an
    /// undyed cloth on its way to a dye, which has no texture of its own.</summary>
    public static readonly IReadOnlyList<string> ClothColours =
        ["plain", "blue", "red", "yellow", "green", "purple", "pink", "orange", "brown", "gray", "black", "white"];

    /// <summary>The sash's materials, as <see cref="SashMaterial"/> writes them: every leather, then every cloth.</summary>
    public static IReadOnlyList<string> SashMaterials { get; } =
        LeatherColours.Select(c => SashMaterial(Leather, c)).Concat(ClothColours.Select(c => SashMaterial(Cloth, c))).ToList();

    public static string SashMaterial(string kind, string colour) => kind + "-" + colour;

    /// <summary>The materials a layer is offered in, for lists: for bones the game's woods.</summary>
    public static IReadOnlyList<string> Materials(FittingLayer layer) => layer switch
    {
        FittingLayer.Bones => KnownWoods,
        FittingLayer.Joints => JointMetals,
        FittingLayer.Plates => PlateMetals,
        FittingLayer.Sash => SashMaterials,
        FittingLayer.Skirt => SkirtMetals,
        _ => [],
    };

    /// <summary>Whether <paramref name="material"/> is one the layer takes. Bones take any wood but the
    /// rotten ones (a mod's wood too); the rest only their lists.</summary>
    public static bool Allows(FittingLayer layer, string? material)
    {
        if (string.IsNullOrEmpty(material))
            return false;
        return layer switch
        {
            FittingLayer.Bones => IsWoodCode(material) && !RottenWoods.Contains(material),
            FittingLayer.Sash => SashMaterials.Contains(material),
            _ => Materials(layer).Contains(material),
        };
    }

    // A wood state is a plain lowercase code: no domain, no further variant parts.
    private static bool IsWoodCode(string s) => s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');

    /// <summary>The layer's texture code in the eidolon's shape (and the gantry's): its name in lower case.</summary>
    public static string TextureCode(FittingLayer layer) => layer.ToString().ToLowerInvariant();

    /// <summary>The layer for a texture code, or null.</summary>
    public static FittingLayer? LayerOfTextureCode(string? code) =>
        Layers.Cast<FittingLayer?>().FirstOrDefault(l => TextureCode(l!.Value) == code);

    /// <summary>The texture a layer of <paramref name="material"/> draws with (a full asset path
    /// under <c>textures/</c>, without the extension), or null when the layer does not take it.
    /// The metals use the game's plate textures, the skirt its generic chain armour; the game has no
    /// stainless chain, so the pack ships one (the steel chain lightened and desaturated).</summary>
    public static string? TexturePath(FittingLayer layer, string? material)
    {
        if (!Allows(layer, material))
            return null;
        string m = material!;
        switch (layer)
        {
            case FittingLayer.Bones:
                return $"game:block/wood/debarked/{m}";
            case FittingLayer.Joints:
            case FittingLayer.Plates:
                return $"game:block/metal/plate/{m}";
            case FittingLayer.Sash:
                var (kind, colour) = SplitSash(m);
                return kind == Leather
                    ? $"game:block/leather/{colour}"
                    : $"game:block/cloth/basic/{(colour == "plain" ? "normal" : colour)}";
            case FittingLayer.Skirt:
                return m == Stainless
                    ? StainlessChainTexture
                    : $"game:block/metal/armor-generic/chain-{m}";
            default:
                return null;
        }
    }

    /// <summary>The pack's stainless chain texture (the skirt's), shipped in the mod.</summary>
    public const string StainlessChainTexture = "seraphhorizons:block/metal/armor-generic/chain-stainlesssteel";

    private static (string Kind, string Colour) SplitSash(string material)
    {
        int dash = material.IndexOf('-');
        return dash < 0 ? (material, "") : (material[..dash], material[(dash + 1)..]);
    }

    /// <summary>The item (for bones the block) each piece of a layer of <paramref name="material"/>
    /// is, by part: one code for most layers, two for the joints (rods, then nails and strips).
    /// Empty when the layer does not take the material.</summary>
    public static IReadOnlyList<string> MaterialCodes(FittingLayer layer, string? material)
    {
        if (!Allows(layer, material))
            return [];
        string m = material!;
        switch (layer)
        {
            case FittingLayer.Bones:
                return [$"game:supportbeam-{m}"];
            case FittingLayer.Joints:
                return [$"game:rod-{m}", $"game:metalnailsandstrips-{m}"];
            case FittingLayer.Plates:
                return [$"game:metalplate-{m}"];
            case FittingLayer.Sash:
                var (kind, colour) = SplitSash(m);
                return [kind == Leather ? $"game:leather-normal-{colour}" : $"game:cloth-{colour}"];
            case FittingLayer.Skirt:
                return [$"game:metalchain-{m}"];
            default:
                return [];
        }
    }

    /// <summary>Which layer and material an item (or block) code is fitted as, or null when it is no
    /// layer's material. A code with no domain is the game's. Jonas parts are not materials (they
    /// belong to their layer whatever its material: <see cref="JonasParts"/>).</summary>
    public static (FittingLayer Layer, string Material)? Identify(string? code)
    {
        code = Normalise(code);
        if (code == null || !code.StartsWith("game:", StringComparison.Ordinal))
            return null;
        string path = code["game:".Length..];
        foreach (var (layer, prefix) in MaterialPrefixes)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string rest = path[prefix.Length..];
            string material = layer == FittingLayer.Sash
                ? SashMaterial(prefix == "cloth-" ? Cloth : Leather, rest)
                : rest;
            if (Allows(layer, material))
                return (layer, material);
        }
        return null;
    }

    private static readonly (FittingLayer Layer, string Prefix)[] MaterialPrefixes =
    [
        (FittingLayer.Bones, "supportbeam-"),
        (FittingLayer.Joints, "rod-"),
        (FittingLayer.Joints, "metalnailsandstrips-"),
        (FittingLayer.Plates, "metalplate-"),
        (FittingLayer.Sash, "leather-normal-"),
        (FittingLayer.Sash, "cloth-"),
        (FittingLayer.Skirt, "metalchain-"),
    ];

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Contains(':') ? code : "game:" + code;

    // ---- The bill ----

    public const string MetalParts = "game:metal-parts";
    public const string StainlessGear = "seraphhorizons:gear-stainless";
    public const string Vessel = "game:rustypart-eidolon2tr";
    public const string TemporalGear = "game:gear-temporal";
    public const string TorsoCode = "seraphhorizons:eidolontorso";

    private static FittingIngredient One(string code) => new(code, 1);

    /// <summary>What the torso is crafted from in the grid: its tanks and pump and the mechanism's
    /// gears; its plates and nails are the layers'.</summary>
    public static readonly IReadOnlyList<FittingIngredient> TorsoRecipe =
    [
        One("game:jonasparts-pumphead"), One("game:jonasparts-tank01"), One("game:jonasparts-tank02"),
        new(MetalParts, 4), new(StainlessGear, 3),
    ];

    /// <summary>The mind: one temporal gear, its first charge, fitted after the five layers.</summary>
    public static readonly IReadOnlyList<FittingIngredient> Mind = [One(TemporalGear)];

    /// <summary>The Jonas parts (and the machine parts that go with them) each layer takes whatever
    /// its material, and gives back when stripped: the frame and the mechanism in it with the bones,
    /// the moving mechanism with the joints, nothing with the outer three.</summary>
    public static IReadOnlyList<FittingIngredient> JonasParts(FittingLayer layer) => layer switch
    {
        FittingLayer.Bones =>
        [
            One("game:jonasframes-gearbox01"), One("game:jonasframes-gearbox02"), One("game:jonasframes-oscillator01"),
            One("game:jonasparts-cylinder02"), One("game:jonasparts-connector01"), One(Vessel),
        ],
        FittingLayer.Joints =>
        [
            new("game:jonasframes-joint01", 2), new("game:jonasframes-spring01", 2),
            One("game:jonasframes-gears01"), One("game:jonasframes-gears02"),
            One("game:jonasparts-cylinder01"), One("game:jonasparts-valve01"),
            new(StainlessGear, 9), new(MetalParts, 12),
        ],
        _ => [],
    };

    /// <summary>A layer's whole bill in <paramref name="material"/>: its material (counts from
    /// <see cref="FittingCounts"/>), then its Jonas parts. What stripping it gives back. Empty when the
    /// layer does not take the material.</summary>
    public static IReadOnlyList<FittingIngredient> Bill(FittingLayer layer, string? material)
    {
        var codes = MaterialCodes(layer, material);
        if (codes.Count == 0)
            return [];
        var counts = FittingCounts.Of(layer);
        var bill = new List<FittingIngredient>();
        for (int i = 0; i < codes.Count; i++)
            bill.Add(new FittingIngredient(codes[i], counts[i], IsBlock: layer == FittingLayer.Bones));
        bill.AddRange(JonasParts(layer));
        return bill;
    }
}

/// <summary>
/// How much material each layer takes, by part (<see cref="EidolonFittings.MaterialCodes"/>'s
/// order): the one place the counts are kept. They are scaled to the model (Eidolon/tools/make_shape.py,
/// <c>layer_bill</c>; <c>config/eidolon-stages.json</c>'s <c>measure</c> carries the numbers, held to
/// these by a test): a layer's material count is the face area its own elements show (half a box's
/// surface, model voxels squared) over the area one item covers, rounded. Metal sets the scale: the
/// limb-by-limb bill before fittings, 13 plates, 8 rods and 36 nails and strips, was 43 ingots (a plate
/// 2, a rod 1, nails and strips 4 to the ingot) and bought the plates' and joints' area, so an ingot
/// covers 115.65; a plate, a chain, a leather or a cloth (a sheet) two ingots', 231.31; a support beam
/// is the game's 16 × 4 × 4 beam, 144.
/// <list type="bullet">
/// <item>bones: 1439.4 of wood / 144 = 10.0, 10 support beams;</item>
/// <item>joints: rods 411.0 / 115.7 = 3.55, 4; nails and strips 531.3 / 28.9 (a quarter ingot) = 18.38, 20
///   (rounded up to a whole ingot's four);</item>
/// <item>plates: 4030.8 / 231.3 = 17.43, 17;</item>
/// <item>sash: 527.8 / 231.3 = 2.28, 2, raised to the floor of 4;</item>
/// <item>skirt: 434.0 / 231.3 = 1.88, 2, raised to the floor of 4.</item>
/// </list>
/// The sash and the skirt are small on the model, so they take at least 4 (<c>SMALL_LAYER_FLOOR</c> in the
/// generator), the owner's call.
/// </summary>
public static class FittingCounts
{
    public static readonly IReadOnlyDictionary<FittingLayer, IReadOnlyList<int>> ByLayer =
        new Dictionary<FittingLayer, IReadOnlyList<int>>
        {
            [FittingLayer.Bones] = [10],     // support beams
            [FittingLayer.Joints] = [4, 20], // rods, nails and strips
            [FittingLayer.Plates] = [17],    // metal plates
            [FittingLayer.Sash] = [4],       // leather or cloth (floor)
            [FittingLayer.Skirt] = [4],      // chain (floor)
        };

    public static IReadOnlyList<int> Of(FittingLayer layer) => ByLayer[layer];
}

/// <summary>
/// An eidolon's fitted layers: each layer's material, or none while it is off (being built, or
/// stripped in the gantry). The layers on are always the first ones in build order (each is fitted
/// complete before the next, and stripped outermost first), so this keeps that: <see cref="With"/>
/// fits only the next layer, <see cref="Stripped"/> takes off only the outermost.
///
/// Stored as a tree of strings under <see cref="AttributeKey"/>, one per layer by its texture code
/// (<c>bones</c>, <c>joints</c>, <c>plates</c>, <c>sash</c>, <c>skirt</c>), a layer that is off
/// absent: on the eidolon entity (its watched attributes, so the client draws it) and on the
/// gantry's hung body. An eidolon with no record at all is from before fittings and is
/// <see cref="OldBuild"/>.
/// </summary>
public sealed class EidolonBuild : IEquatable<EidolonBuild>
{
    /// <summary>The attribute (a tree) the record is kept under.</summary>
    public const string AttributeKey = "eidolonFittings";

    private readonly string?[] _materials = new string?[EidolonFittings.Layers.Count];

    /// <summary>Nothing fitted: a bare torso.</summary>
    public static EidolonBuild Bare => new();

    /// <summary>How every eidolon was built before fittings, and so what one from then loads as:
    /// oak bones, steel joints and plates, a plain leather sash and a steel skirt.</summary>
    public static EidolonBuild OldBuild { get; } = Of(
        "oak", EidolonFittings.Steel, EidolonFittings.Steel,
        EidolonFittings.SashMaterial(EidolonFittings.Leather, "plain"), EidolonFittings.Steel);

    /// <summary>A complete build; throws when a layer does not take its material.</summary>
    public static EidolonBuild Of(string bones, string joints, string plates, string sash, string skirt)
    {
        var b = Bare;
        foreach (var (layer, material) in EidolonFittings.Layers.Zip([bones, joints, plates, sash, skirt]))
            b = b.With(layer, material) ?? throw new ArgumentException($"{layer} does not take {material}");
        return b;
    }

    /// <summary>The material of <paramref name="layer"/>, or null while it is off.</summary>
    public string? this[FittingLayer layer] => _materials[(int)layer];

    /// <summary>Every layer on: it can be woken (with the mind fitted).</summary>
    public bool Complete => _materials.All(m => m != null);

    public bool Empty => _materials.All(m => m == null);

    /// <summary>The next layer to fit (the first that is off), or null when all are on.</summary>
    public FittingLayer? NextToFit => EidolonFittings.Layers.Cast<FittingLayer?>().FirstOrDefault(l => this[l!.Value] == null);

    /// <summary>The next layer to strip (the outermost on), or null when none is.</summary>
    public FittingLayer? NextToStrip => EidolonFittings.StripOrder.Cast<FittingLayer?>().FirstOrDefault(l => this[l!.Value] != null);

    /// <summary>This build with <paramref name="layer"/> fitted in <paramref name="material"/>, or
    /// null when it is not the next layer or does not take the material.</summary>
    public EidolonBuild? With(FittingLayer layer, string? material)
    {
        if (NextToFit != layer || !EidolonFittings.Allows(layer, material))
            return null;
        var b = Copy();
        b._materials[(int)layer] = material;
        return b;
    }

    /// <summary>This build with its outermost layer off, or null when nothing is on.</summary>
    public EidolonBuild? Stripped()
    {
        if (NextToStrip is not { } layer)
            return null;
        var b = Copy();
        b._materials[(int)layer] = null;
        return b;
    }

    /// <summary>Whether a docked eidolon may be stripped: only at full health (a damaged one is
    /// repaired first), and only when it has a layer on.</summary>
    public bool CanStrip(double health, double maxHealth) =>
        NextToStrip != null && double.IsFinite(health) && health >= maxHealth;

    /// <summary>The texture each layer that is on draws with, by texture code (for a per-entity
    /// texture override; a layer that is off keeps the shape's default).</summary>
    public IReadOnlyDictionary<string, string> Textures() =>
        EidolonFittings.Layers.Where(l => this[l] != null)
            .ToDictionary(EidolonFittings.TextureCode, l => EidolonFittings.TexturePath(l, this[l])!);

    /// <summary>The record as stored: a layer that is on by its texture code.</summary>
    public IReadOnlyDictionary<string, string> ToAttributes() =>
        EidolonFittings.Layers.Where(l => this[l] != null).ToDictionary(EidolonFittings.TextureCode, l => this[l]!);

    /// <summary>The record from storage; null (no record) is <see cref="OldBuild"/>. Layers are read in
    /// build order and stop at the first that is missing or not a material it takes, so what is
    /// read is always a valid build.</summary>
    public static EidolonBuild FromAttributes(IReadOnlyDictionary<string, string?>? stored)
    {
        if (stored == null)
            return OldBuild;
        var b = Bare;
        foreach (var layer in EidolonFittings.Layers)
        {
            if (!stored.TryGetValue(EidolonFittings.TextureCode(layer), out var material)
                || b.With(layer, material) is not { } next)
                break;
            b = next;
        }
        return b;
    }

    private EidolonBuild Copy()
    {
        var b = new EidolonBuild();
        _materials.CopyTo(b._materials, 0);
        return b;
    }

    public bool Equals(EidolonBuild? other) => other != null && _materials.SequenceEqual(other._materials);

    public override bool Equals(object? obj) => Equals(obj as EidolonBuild);

    public override int GetHashCode() => _materials.Aggregate(17, (h, m) => h * 31 + (m?.GetHashCode() ?? 0));

    public override string ToString() =>
        string.Join(", ", EidolonFittings.Layers.Select(l => $"{EidolonFittings.TextureCode(l)} {this[l] ?? "-"}"));
}
