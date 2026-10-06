using Atlas.XUnit;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.MachineOil;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, MachineOil on the bucking sawmill, the pack's own machine, which keeps
/// its tank itself: built dry with three times its load, oiled from a bucket on any of its cells,
/// and a cut costing its stored logs' worth (with the helpers of <c>BuckingSawmillScenarios.cs</c>).
/// </summary>
public partial class WoodworkingScenarios
{
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task The_bucking_mill_starts_dry_takes_oil_on_any_cell_and_a_cut_drains_it()
    {
        var pos = Sky(-90, 120);
        var player = await Player("millgreaser");
        var mill = await PlaceMill(pos, "south");
        Assert.True(mill.Oiling!.Dry, "a new mill is not dry");
        Assemble(mill, player);
        // Assemble oils the mill for the other scenarios; this one starts from a new, dry mill.
        mill.Oiling!.Tank = OilTank.Empty(mill.Oiling.Tank.Capacity);
        var oil = MachineOilSystem.Of(World.Api).Config;
        var power = W.BlockAccessor.GetBlockEntity(mill.CellPos(Rig.PowerCell))!.GetBehavior<BEBehaviorMillMP>()!;

        Assert.NotNull(mill.Oiling);
        Assert.True(mill.Oiling!.Dry);
        Assert.Equal(oil.BuckingMill.Tank, mill.Oiling.Tank.Capacity);
        Assert.Equal(Mod.Config.Resistance * oil.DryResistanceMultiplier, power.GetResistance(), 4);
        Assert.Contains("Dry", Info(mill, player));

        // A bucket of olive oil on the power cell: the click is the oil's, whatever the cell does.
        var bucket = new ItemStack(BlockOf("game:woodbucket"));
        ((BlockLiquidContainerBase)bucket.Block).SetContent(bucket, ItemOf("game:oilportion-olive", 500));
        var left = Click(player, mill.CellPos(Rig.PowerCell), bucket);
        Assert.Equal(500, mill.Oiling.Tank.Points, 3);
        Assert.Null(((BlockLiquidContainerBase)left!.Block).GetContent(left));
        Assert.Equal(Mod.Config.Resistance, power.GetResistance(), 4);

        // A four-log trunk costs ceil(4 × DrainPerJob).
        TurnToTop(mill);
        Assert.Null(Click(player, pos, Trunk("oak", 4)));
        Assert.NotNull(mill.Trunk);
        await Power(mill);
        await World.Until(() => mill.Trunk == null, 6000);
        Assert.Equal(500 - OilDrain.PerTrunk(4, oil.BuckingMill.DrainPerJob), mill.Oiling.Tank.Points, 3);
        Assert.Contains($"Oil: {500 - OilDrain.PerTrunk(4, oil.BuckingMill.DrainPerJob)} of 1000", Info(mill, player));
        await Unpower(mill);
    }
}
