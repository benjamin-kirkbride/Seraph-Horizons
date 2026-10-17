using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Sluice;

/// <summary>
/// The sluice (#734): the first concentrator, a continuous ore machine on the shared code
/// (<see cref="BEOreMachine"/>) with no power: water and gravity. Crushed or ground ore, placer gravel and
/// bog iron (#701) come down a vanilla chute into the head box (the infeed, the head cell's top) or in by
/// hand; with an item's water in its reservoir (10 L a rock from a ppex pipe on the head's north face) it
/// washes <c>machines.sluice.perHour</c> (16) an in-game hour (<see cref="WorkHours"/>), each through
/// <see cref="ConcentratorWash"/> at r = 0.55: what the riffles catch leaves continuously by the
/// concentrate chute out of cell 2's bottom face, whole items carried over from rock to rock
/// (<see cref="UnitCarry"/>), and the rock goes on as tailings, the same item carrying the rest, by the
/// tailings chute at the tail's south face, or discarded beside it as gravel in its rock (#694). Nothing in
/// it wears out. The concentrate is drawn in the riffles while it works.
/// </summary>
public class BESluice : BEOreMachine
{
    public const string Concentrate = "concentrate";

    /// <summary>Where the launder pours down into the concentrate chute (the rig's <c>concentrateSpout</c>).</summary>
    private static readonly Float3 ConcentrateSpout = new(0.5f, 0.512f, 2.4615f);
    private static readonly AssetLocation PourSound = new("game", "sounds/block/sand");

    private readonly UnitCarry _carry = new();
    private WorkHours _hours = WorkHours.None;

    public override OreMachine? Kind => OreMachine.Sluice;

    protected override IReadOnlyList<string> OutputAnchors => [Concentrate, Tailings];

    /// <summary>Crushed or ground ore (a rock form carrying an ore), rich gravel, or bog iron. Raw ore needs
    /// breaking first; roasted rock and concentrate are past the concentrators.</summary>
    public override bool Takes(ItemStack stack) => SluiceSystem.TakesFeed(stack);

    /// <summary>The concentrate is drawn in the riffles while the sluice works; the rest as the build goes.</summary>
    public override bool ShowsPart(string? requires) =>
        requires == Concentrate ? Running && base.ShowsPart("riffles") : base.ShowsPart(requires);

    protected override bool WorkUnpowered(float dt)
    {
        if (Spec is not { ItemsPerHour: > 0 } spec)
            return false;
        var calendar = Api.World.Calendar;
        double hours = dt * calendar.SpeedOfTime * calendar.CalendarSpeedMul / 3600.0;
        (_hours, int items) = _hours.Add(hours, spec.ItemsPerHour);
        bool changed = false;
        for (int i = 0; i < items; i++)
        {
            if (!WorkItem())
            {
                _hours = WorkHours.None;
                break;
            }
            changed = true;
        }
        return changed;
    }

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        var recovery = OreProcessingSystem.RecoveryFor(Api);
        if (SluiceSystem.StreamOf(fed) is not { } stream)
        {
            yield return (Tailings, fed);
            yield break;
        }
        string code = fed.Collectible.Code.ToString();
        var wash = ConcentratorWash.Wash(recovery, OreMachine.Sluice, stream, () => PlacerTable.For(Api, code));
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
            Api.World.Logger.Warning("[seraphhorizons] Sluice: {0} is not in the game; {1} of it are lost", code, count);
            return null;
        }
        return new ItemStack(c, count);
    }

    protected override void OnWorked(ItemStack fed)
    {
        var at = WorldPoint(ConcentrateSpout);
        Api.World.PlaySoundAt(PourSound, at.X, at.Y, at.Z, null, true, 12, 0.4f);
    }

    protected override ItemStack[] FeedStacks(IWorldAccessor world) => SluiceSystem.FeedExamples(world);

    protected override void AppendContents(StringBuilder dsc)
    {
        base.AppendContents(dsc);
        if (Built && !InfeedSlot.Empty && Running)
            dsc.AppendLine(Lang.Get("seraphhorizons:sluice-info-washing", (int)Math.Floor(_hours.Progress * 100)));
    }

    // ---- Saving ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("hours", _hours.Items);
        var carry = new TreeAttribute();
        foreach (var (key, units) in _carry.Held)
            carry.SetDouble(key, units);
        tree["carry"] = carry;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _hours = WorkHours.Restore(tree.GetDouble("hours"));
        var held = new List<KeyValuePair<string, double>>();
        if (tree["carry"] is ITreeAttribute carry)
            foreach (var (key, value) in carry)
                if (value is DoubleAttribute d)
                    held.Add(new(key, d.value));
        _carry.Restore(held);
    }
}
