using SeraphHorizons.Mod.Jig.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Sluice;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.Jig;

/// <summary>
/// The jig (#919): the second concentrator, a continuous ore machine on the shared code
/// (<see cref="BEOreMachine"/>), worked by an axle on its eccentric shaft. Crushed or ground ore (the
/// sluice's tailings among them; rich gravel and bog iron too, as the sluice takes them) comes down a
/// vanilla chute onto its feed trough (the infeed, the front top cell's top) or in by hand; with a rock's
/// water in its reservoir (70 L from a ppex pipe into the plunger compartment) it jigs
/// <c>machines.jig.perHour</c> (8) an hour at speed 0.6, in proportion to its speed, each through
/// <see cref="Jigging.Wash"/> at r = 0.60: what sinks through the sieve leaves by the concentrate chute out
/// of the front face, whole items carried over from rock to rock (<see cref="UnitCarry"/>), and the rock
/// goes on over the weir as tailings, the same item carrying the rest, by the tailings chute on the west
/// face, or is discarded beside it as gravel in its rock (#694). Its load is steady (0.1 once built).
/// Nothing in it wears out. Its water and ore bed are drawn while it holds water and works.
/// </summary>
public class BEJig : BEOreMachine
{
    public const string Concentrate = "concentrate";

    /// <summary>The middle of the sieve's top (the rig's <c>sieve.pos</c>): where it splashes.</summary>
    private static readonly Float3 SievePoint = new(0.5625f, 1.1f, 0.6562f);
    private static readonly AssetLocation SplashSound = new("game", "sounds/environment/smallsplash");

    private readonly UnitCarry _carry = new();

    public override OreMachine? Kind => OreMachine.Jig;

    protected override IReadOnlyList<string> OutputAnchors => [Concentrate, Tailings];

    /// <summary>What the sluice washes (<see cref="SluiceSystem.TakesFeed"/>): crushed or ground ore, rich
    /// gravel, bog iron, and their tailings carrying their streams. Raw ore needs breaking first.</summary>
    public override bool Takes(ItemStack stack) => SluiceSystem.TakesFeed(stack);

    /// <summary>The water while it holds some, the bed while it works; the rest as the build goes.</summary>
    public override bool ShowsPart(string? requires) =>
        Jigging.ShowsContent(requires, Built, Water.Litres > 0, Running) ?? base.ShowsPart(requires);

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        if (SluiceSystem.StreamOf(fed) is not { } stream)
        {
            yield return (Tailings, fed);
            yield break;
        }
        string code = fed.Collectible.Code.ToString();
        var wash = Jigging.Wash(OreProcessingSystem.RecoveryFor(Api), stream, () => PlacerTable.For(Api, code));
        foreach (var product in wash.Products)
        {
            int n = _carry.Add(product.Code, product.Units * fed.StackSize, product.UnitsPerItem);
            if (n > 0 && ProductStack(product.Code, n) is { } stack)
                yield return (Concentrate, stack);
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
            Api.World.Logger.Warning("[seraphhorizons] Jig: {0} is not in the game; {1} of it are lost", code, count);
            return null;
        }
        return new ItemStack(c, count);
    }

    protected override void OnWorked(ItemStack fed)
    {
        var at = WorldPoint(SievePoint);
        Api.World.PlaySoundAt(SplashSound, at.X, at.Y, at.Z, null, true, 12, 0.4f);
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
