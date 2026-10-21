using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

/// <summary>
/// What every machine's scenarios ask of a machine on the shared base (<see cref="BEMachine"/>), whatever the
/// machine: its block info as a player sees it, with a wrench or without; its oil tank filled; the radians of
/// its clock that bring its work to a W; its cells; and what the player holds. Right-click held on a machine is
/// <see cref="PlainWorldScenarioBase.Hold"/>, as it waits on the world's ticks. A file of no test class
/// (ci/atlas-shards.md): a change here runs every class that uses it.
/// </summary>
internal static class MachineTest
{
    /// <summary>The block info <paramref name="player"/> sees on <paramref name="machine"/>, holding what they hold.</summary>
    public static string Info(BEMachine machine, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        machine.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    /// <summary>The block info as <paramref name="player"/> sees it holding a wrench: the detail too.</summary>
    public static string WrenchInfo(BEMachine machine, IPlayer player)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        var held = slot.Itemstack;
        slot.Itemstack = new ItemStack(machine.Api.World.GetItem(new AssetLocation("game:wrench-iron"))!);
        try { return Info(machine, player); }
        finally { slot.Itemstack = held; }
    }

    /// <summary>Sets <paramref name="machine"/>'s oil tank to <paramref name="fill"/> of its capacity (a machine is
    /// built dry, its load on the shaft multiplied).</summary>
    public static void FillOil(BEMachine machine, double fill = 1)
    {
        var oiling = machine.Oiling ?? throw new Xunit.Sdk.XunitException($"the {machine.Machine} has no oil tank");
        oiling.Tank = oiling.Tank with { Points = oiling.Tank.Capacity * fill };
    }

    /// <summary>Radians of a machine's clock that bring <paramref name="job"/> to W = <paramref name="work"/> from where
    /// it is, at <paramref name="turnsPerUnit"/> turns a unit of W (and a hair over, so the step reaches it).</summary>
    public static double RadiansTo(MachineJob job, double work, double turnsPerUnit) =>
        (work - job.Work) * 2 * Math.PI * turnsPerUnit + 1e-6;

    /// <summary>The machine's cells but the controller's (the shared machine cells), and whether each is the power cell.</summary>
    public static List<(BlockPos Pos, bool Power)> Cells(BEMachine machine) =>
        machine.CellBlocks().Select(c => (c.Pos, c.Block is BlockMachineCellPower)).ToList();

    private static IEnumerable<ItemSlot> Carried(IPlayer player) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv);

    /// <summary>How many of <paramref name="code"/> the player carries (hotbar and backpack).</summary>
    public static int Held(IPlayer player, string code) =>
        Carried(player).Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    /// <summary>The first stack of <paramref name="code"/> the player carries, or null.</summary>
    public static ItemStack? HeldStack(IPlayer player, string code) =>
        Carried(player).FirstOrDefault(s => s.Itemstack?.Collectible.Code.ToString() == code)?.Itemstack;
}
