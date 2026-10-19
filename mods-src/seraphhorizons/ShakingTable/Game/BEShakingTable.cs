using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.ShakingTable.Core;
using SeraphHorizons.Mod.Sluice;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.ShakingTable;

/// <summary>
/// The shaking table (#921): the third concentrator, a continuous ore machine on the shared code
/// (<see cref="BEOreMachine"/>), its axle at the controller. Crushed or ground ore (and rich gravel and bog iron,
/// as the sluice and jig take them), fresh or the jig's tailings, comes in by a vanilla chute onto the feed box
/// (the infeed, the top of the head end's middle cell) or by hand; with an item's water in its reservoir (20 L a
/// rock from a ppex pipe down onto the water line) it washes <c>machines.table.perHour</c> (7 an hour at shaft
/// speed 0.6, in proportion to speed), each through <see cref="ShakingTabling"/> at r = 0.70. Three outlets: the
/// concentrate off the far end, whole items carried over from rock to rock (<see cref="UnitCarry"/>); the
/// middlings off the far end's lower corner, the fed item carrying its locked units, for the ball mill; the
/// tailings off the low side, the fed item carrying the rest, on to the vanner or discarded beside it as sand in
/// its rock (#694). Its load is steady. Nothing in it wears out.
/// </summary>
public class BEShakingTable : BEOreMachine
{
    private static readonly AssetLocation WashSound = new("game", "sounds/block/sand");

    /// <summary>Over the middle of the deck, where the sound is made.</summary>
    private static readonly Float3 Deck = new(1f, 0.6f, 1.75f);

    private readonly UnitCarry _carry = new();

    public override OreMachine? Kind => OreMachine.Table;

    /// <summary>Oiled, its job a rock washed (<see cref="BEOreMachine.WorkItem"/>).</summary>
    protected override OilMachine? OilKind => OilMachine.ShakingTable;

    protected override IReadOnlyList<string> OutputAnchors =>
        [ShakingTabling.Concentrate, ShakingTabling.Middlings, ShakingTabling.Tailings];

    public override bool Takes(ItemStack stack) => ShakingTableSystem.TakesFeed(stack);

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        if (SluiceSystem.StreamOf(fed) is not { } stream)
        {
            yield return (ShakingTabling.Tailings, fed);
            yield break;
        }
        string code = fed.Collectible.Code.ToString();
        var outlets = ShakingTabling.Wash(OreProcessingSystem.RecoveryFor(Api), stream, () => PlacerTable.For(Api, code));
        foreach (var product in outlets.Concentrate)
        {
            int n = _carry.Add(product.Code, product.Units * fed.StackSize, product.UnitsPerItem);
            if (n > 0 && ProductStack(product.Code, n) is { } stack)
            {
                OreStreamAttributes.MarkWhole(stack, outlets.Whole);
                yield return (ShakingTabling.Concentrate, stack);
            }
        }
        if (outlets.Middlings is { } middlings)
        {
            var m = fed.Clone();
            OreStreamAttributes.Write(m, middlings);
            yield return (ShakingTabling.Middlings, m);
        }
        var tailings = fed.Clone();
        OreStreamAttributes.Write(tailings, outlets.Tailings);
        yield return (ShakingTabling.Tailings, tailings);
    }

    /// <summary>A product's stack: the item or block of its code.</summary>
    private ItemStack? ProductStack(string code, int count)
    {
        var loc = new AssetLocation(code);
        CollectibleObject? c = (CollectibleObject?)Api.World.GetItem(loc) ?? Api.World.GetBlock(loc);
        if (c is not { Id: > 0, IsMissing: false })
        {
            Api.World.Logger.Warning("[seraphhorizons] Shaking table: {0} is not in the game; {1} of it are lost", code, count);
            return null;
        }
        return new ItemStack(c, count);
    }

    protected override void OnWorked(ItemStack fed)
    {
        var at = WorldPoint(Deck);
        Api.World.PlaySoundAt(WashSound, at.X, at.Y, at.Z, null, true, 12, 0.35f);
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
