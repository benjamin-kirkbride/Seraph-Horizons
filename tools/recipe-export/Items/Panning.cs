using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// What one pan block (BlockPan) gives for each block a player can pan, read the way the
/// survival mod's BlockPan of game 1.22.7 decides it (TryTakeMaterial, CreateDrop). The Panning
/// Machine reads the same table from game:pan-wooden, so its outputs are these too. See
/// item-data.md, "Panning".
/// </summary>
internal sealed class Panning
{
    // BlockPan.OnLoaded parses attributes.panningDrops into this field, drops the manMade
    // entries when the world's loreContent is off and resolves every code without {rocktype}.
    private static readonly FieldInfo DropsField =
        typeof(BlockPan).GetField("dropsBySourceMat", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new RecipeExportException("BlockPan has no dropsBySourceMat field; panning cannot be read");

    private readonly IWorldAccessor _world;
    private readonly Dictionary<string, PanningDrop[]> _table;

    public BlockPan Pan { get; }

    private Panning(IWorldAccessor world, BlockPan pan, Dictionary<string, PanningDrop[]> table)
    {
        _world = world;
        Pan = pan;
        _table = table;
    }

    public static List<Panning> Pans(ICoreServerAPI api) => api.World.Blocks
        .OfType<BlockPan>()
        .Where(p => p.Code != null && !p.IsMissing)
        .Select(p => DropsField.GetValue(p) is Dictionary<string, PanningDrop[]> table ? new Panning(api.World, p, table) : null)
        .OfType<Panning>()
        .ToList();

    /// <summary>
    /// The block whose code the pan stores when it takes material from <paramref name="block"/>:
    /// the block itself, or for a block with a "layer" variant (what is left of a panned block)
    /// the block named by its first two code parts, in the game domain, as TryTakeMaterial does.
    /// </summary>
    public Block? Material(Block block) => block.Variant["layer"] == null
        ? block
        : _world.GetBlock(new AssetLocation(block.FirstCodePart(0) + "-" + block.FirstCodePart(1)));

    /// <summary>
    /// What a full (layerless) pannable block turns into when panned: attributes.pannedBlock, or
    /// its own code with layer 7. Null when that block does not exist; the pan then refuses it.
    /// </summary>
    public Block? PannedBlock(Block block)
    {
        var code = block.Attributes?["pannedBlock"].AsString(null);
        return _world.GetBlock(code == null ? block.CodeWithVariant("layer", "7") : AssetLocation.Create(code, block.Code.Domain));
    }

    /// <summary>The drop list for a material code: the last table key that matches wins (CreateDrop).</summary>
    public PanningDrop[]? DropsFor(Block material)
    {
        var code = material.Code.ToShortString();
        PanningDrop[]? found = null;
        foreach (var (key, drops) in _table)
            if (WildcardUtil.Match(key, code)) found = drops;
        return found;
    }

    /// <summary>
    /// The stack a drop gives when panning <paramref name="material"/>. "{rocktype}" in the code's
    /// path is replaced by the material's rock variant and the result looked up in the game
    /// domain; null when the code does not resolve (the game then skips the drop).
    /// </summary>
    public ItemStack? Stack(PanningDrop drop, Block material)
    {
        if (!drop.Code.Path.Contains("{rocktype}")) return drop.ResolvedItemstack;
        var code = new AssetLocation(drop.Code.Path.Replace("{rocktype}", material.Variant["rock"]));
        CollectibleObject? c = drop.Type == EnumItemClass.Block ? _world.GetBlock(code) : _world.GetItem(code);
        return c == null ? null : new ItemStack(c, 1);
    }

    /// <summary>
    /// Chance that one pan gives drop <paramref name="i"/>. CreateDrop shuffles the list, rolls
    /// each drop in turn against its chance and stops at the first hit that resolved, so a pan
    /// gives at most one item. With the drop at a uniformly random position, the drops before it
    /// are a uniformly random subset of the others, and it is reached when all of them miss.
    /// Player stat modifiers (dropModbyStat) are taken as 1.
    /// </summary>
    public static double ChancePerPan(IReadOnlyList<double> chances, int i)
    {
        var n = chances.Count;
        // e[k]: elementary symmetric polynomial of degree k over the miss chances of the others.
        var e = new double[n];
        e[0] = 1;
        var m = 0;
        for (var j = 0; j < n; j++)
        {
            if (j == i) continue;
            var q = 1 - chances[j];
            m++;
            for (var k = m; k > 0; k--) e[k] += e[k - 1] * q;
        }
        double reached = 0, binom = 1;
        for (var k = 0; k < n; k++)
        {
            reached += e[k] / binom;
            binom = binom * (n - 1 - k) / (k + 1);
        }
        return chances[i] * reached / n;
    }

    /// <summary>
    /// The chance that one roll of a drop hits: the roll is a uniform double below the drop's
    /// NatFloat sample, clamped to [0, 1]. Exact for var 0 (every drop in this pack); for a
    /// varying chance the clamped sample is averaged over its distribution's range as if uniform.
    /// </summary>
    public static double RollChance(NatFloat chance)
    {
        if (chance.var == 0) return Math.Clamp(chance.avg + chance.offset, 0, 1);
        const int steps = 1000;
        double sum = 0;
        for (var s = 0; s < steps; s++)
        {
            var u = (s + 0.5) / steps * 2 - 1;
            sum += Math.Clamp(chance.offset + chance.avg + u * chance.var, 0, 1);
        }
        return sum / steps;
    }
}
