using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Sluice;
using SeraphHorizons.Mod.Vanner.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Vanner;

/// <summary>
/// The vanner (#923): the last of the concentrators, a continuous ore machine on the shared code
/// (<see cref="BEOreMachine"/>), driven by an axle onto its countershaft at the back. Crushed or ground ore
/// (the shaking table's tailings), and placer gravel or bog iron carried down the line, come in by a vanilla
/// chute into the feed box (the infeed, the top of the middle west cell) or by hand; with an item's water in
/// its reservoir (15 L a rock, a ppex pipe down onto the front east cell) it vans
/// <c>machines.vanner.perHour</c> (3) an in-game hour at speed 0.6, in proportion to speed, each through
/// <see cref="Vanning.Wash"/> at r = 0.80. The concentrate leaves by the chute under the head, whole items
/// carried over from rock to rock (<see cref="UnitCarry"/>; a whole-route concentrate marked so,
/// <see cref="OreStreamAttributes.MarkWhole"/>); the rock goes on as tailings, the same item carrying the
/// rest, by the chute off the foot, or is discarded beside it as sand in its rock (#694). The leather belt
/// wears a use a rock and is renewed by right-click.
/// </summary>
public class BEVanner : BEOreMachine
{
    public const string Concentrate = "concentrate";

    /// <summary>Where the concentrate drops off the head into its box (the rig's head roller, native blocks).</summary>
    private static readonly Float3 HeadBox = new(0.35f, 0.35f, 0.3f);
    private static readonly AssetLocation WashSound = new("game", "sounds/block/sand");

    private readonly UnitCarry _carry = new();

    public override OreMachine? Kind => OreMachine.Vanner;

    protected override IReadOnlyList<string> OutputAnchors => [Concentrate, Tailings];

    /// <summary>What the concentrators wash: crushed or ground ore, rich gravel and bog iron
    /// (<see cref="SluiceSystem.StreamOf"/>). Raw ore needs breaking first; roasted rock and concentrate are
    /// past the concentrators.</summary>
    public override bool Takes(ItemStack stack) => SluiceSystem.TakesFeed(stack);

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        if (SluiceSystem.StreamOf(fed) is not { } stream)
        {
            yield return (Tailings, fed);
            yield break;
        }
        string code = fed.Collectible.Code.ToString();
        var wash = Vanning.Wash(OreProcessingSystem.RecoveryFor(Api), stream, () => PlacerTable.For(Api, code));
        foreach (var product in wash.Products)
        {
            bool whole = wash.Whole;
            int n = _carry.Add(Vanning.CarryKey(product.Code, whole), product.Units * fed.StackSize, product.UnitsPerItem);
            if (n > 0 && ProductStack(product.Code, n) is { } stack)
            {
                OreStreamAttributes.MarkWhole(stack, whole);
                yield return (Concentrate, stack);
            }
        }
        var tailings = fed.Clone();
        OreStreamAttributes.Write(tailings, wash.Tailings);
        yield return (Tailings, tailings);
    }

    /// <summary>A product's stack: the item or block of its code.</summary>
    private ItemStack? ProductStack(string code, int count)
    {
        var loc = new AssetLocation(code);
        CollectibleObject? c = (CollectibleObject?)Api.World.GetItem(loc) ?? Api.World.GetBlock(loc);
        if (c is not { Id: > 0, IsMissing: false })
        {
            Api.World.Logger.Warning("[seraphhorizons] Vanner: {0} is not in the game; {1} of it are lost", code, count);
            return null;
        }
        return new ItemStack(c, count);
    }

    protected override void OnWorked(ItemStack fed)
    {
        var at = WorldPoint(HeadBox);
        Api.World.PlaySoundAt(WashSound, at.X, at.Y, at.Z, null, true, 12, 0.3f);
    }

    protected override ItemStack[] FeedStacks(IWorldAccessor world) => SluiceSystem.FeedExamples(world);

    // ---- Saving ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var carry = new TreeAttribute();
        foreach (var (key, units) in _carry.Held)
            carry.SetDouble(key, units);
        tree["carry"] = carry;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var held = new List<KeyValuePair<string, double>>();
        if (tree["carry"] is ITreeAttribute carry)
            foreach (var (key, value) in carry)
                if (value is DoubleAttribute d)
                    held.Add(new(key, d.value));
        _carry.Restore(held);
    }
}
