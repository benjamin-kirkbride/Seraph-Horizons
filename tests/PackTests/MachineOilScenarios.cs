using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.MachineOil;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

/// <summary>A machine of the game's on a granite floor and a player oiling it with their own hands,
/// through the block's <c>OnBlockInteractStart</c> as the game calls it for a right-click. Shared
/// with <see cref="SwitchesOffScenarios"/>.</summary>
internal sealed class OilSite(IWorldSession world, BlockPos pos, IPlayer player)
{
    public const string Pulverizer = "game:pulverizerframe-north";
    public const string Toggle = "game:woodentoggle-ns";
    public const string Helve = "game:helvehammerbase-north";
    public const string Tallow = "game:fat-rendered";
    public const string FlaxOil = "game:oilportion-flax";
    public const string Bucket = "game:woodbucket";

    public BlockPos Pos { get; } = pos;
    private IWorldAccessor W => world.Api.World;

    public async Task Clear()
    {
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        {
            world.SetBlock("game:rock-granite", Pos.AddCopy(dx, -1, dz));
            for (int dy = 0; dy < 4; dy++)
                world.SetBlock("game:air", Pos.AddCopy(dx, dy, dz));
        }
        await world.Ticks(2);
    }

    public ItemStack Stack(string code, int size = 1) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)
        : new ItemStack(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}"), size);

    /// <summary>A wooden bucket holding <paramref name="items"/> of <paramref name="liquid"/>.</summary>
    public ItemStack BucketOf(string liquid, int items)
    {
        var bucket = Stack(Bucket);
        ((BlockLiquidContainerBase)bucket.Block).SetContent(bucket, Stack(liquid, items));
        return bucket;
    }

    public ItemSlot Hand => player.InventoryManager.ActiveHotbarSlot;

    /// <summary>Right-clicks <paramref name="at"/> holding <paramref name="held"/>; returns what the
    /// game's interaction returned.</summary>
    public bool RightClick(BlockPos at, ItemStack? held)
    {
        Hand.Itemstack = held;
        Hand.MarkDirty();
        var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        return W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel);
    }

    public static string Info(BlockEntity be, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        be.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    /// <summary>Whether <paramref name="method"/> carries a patch of machine oil's.</summary>
    public static bool Patched(MethodBase? method) =>
        method != null && Harmony.GetPatchInfo(method) is { } info
        && info.Owners.Contains(MachineOilSystem.HarmonyId);

    public static bool AnyPatched() => Harmony.HasAnyPatches(MachineOilSystem.HarmonyId);
}

/// <summary>
/// mods-src/seraphhorizons, MachineOil: the heavy machines start dry and load their shaft three
/// times as hard until oiled; oil goes in from a held container or by the lump; jobs drain it.
/// </summary>
public partial class SharedWorldScenarios
{
    // The fragility guard: a mod update that renames a method fails here instead of silently
    // leaving a machine without its drain, its load or its pouring.
    [AtlasScenario]
    public void Every_machine_oil_patch_target_resolves_and_is_patched()
    {
        Assert.True(ForeignMachines.WoodworkingBound, "Immersive Woodworking's sawmill and chopper are not oiled");
        var targets = ForeignMachines.Targets;
        Assert.Equal(27, targets.Count);
        foreach (var machine in new[] { OilMachine.HelveHammer, OilMachine.Pulverizer, OilMachine.Sawmill, OilMachine.Chopper })
            Assert.Contains(targets, t => t.Machine == machine);
        foreach (var target in targets)
        {
            Assert.True(target.Method != null, $"{target.Name} was not found");
            Assert.True(OilSite.Patched(target.Method), $"{target.Name} is not patched");
            var info = Harmony.GetPatchInfo(target.Method!)!;
            if (target.Prefix != "")
                Assert.Contains(info.Prefixes, p => p.owner == MachineOilSystem.HarmonyId && p.PatchMethod.Name == target.Prefix);
            if (target.Postfix != "")
                Assert.Contains(info.Postfixes, p => p.owner == MachineOilSystem.HarmonyId && p.PatchMethod.Name == target.Postfix);
        }
        // The defaults name oils that exist in the pack.
        foreach (var code in new[] { "game:oilportion-flax", "game:oilportion-olive", "expandedfoods:foodoilportion-sunflower",
                     "expandedfoods:foodoilportion-walnut", "expandedfoods:lard", "expandedfoods:hardlardliquid" })
        {
            Assert.True(W.GetItem(new AssetLocation(code)) != null, $"no {code}");
            Assert.True(MachineOilSystem.Of(World.Api).Liquids.Matches(code), $"{code} is not an oil");
        }
        Assert.NotNull(W.GetItem(new AssetLocation(OilSite.Tallow)));
    }

    [AtlasScenario]
    public async Task A_pulverizer_starts_dry_takes_oil_and_drains_by_the_item()
    {
        var p = await World.JoinPlayer("oiler");
        var pos = World.Spawn.AddCopy(100, 12, 100);
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        var site = new OilSite(World, pos, p.Player);
        await site.Clear();
        World.SetBlock(OilSite.Pulverizer, pos);
        await World.Ticks(2);
        var be = Assert.IsType<BEPulverizer>(W.BlockAccessor.GetBlockEntity(pos));
        be.hasAxle = true;
        var mp = be.GetBehavior<BEBehaviorMPPulverizer>()!;
        var config = MachineOilSystem.Of(World.Api).Config;
        var cfg = config.Pulverizer;

        // Built dry: three times the game's 0.085.
        var state = ForeignMachines.StateOf(be)!;
        Assert.True(state.Dry);
        Assert.Equal(cfg.Tank, state.Tank.Capacity);
        Assert.Equal(0.085f * 3, mp.GetResistance(), 4);
        // The Dry line gives the load the shaft last asked, against the oiled one.
        Assert.Contains("Dry: a load of 0.255 on its shaft, 3× the 0.085 it takes oiled", OilSite.Info(be, p.Player));

        // Four lumps of tallow, half a litre each.
        Assert.True(site.RightClick(pos, site.Stack(OilSite.Tallow, 4)));
        Assert.Null(site.Hand.Itemstack);
        Assert.Equal(200, state.Tank.Points, 3);
        Assert.Equal(0.085f, mp.GetResistance(), 4);
        Assert.Contains("Oil: 200 of 1000", OilSite.Info(be, p.Player));
        Assert.DoesNotContain("Dry", OilSite.Info(be, p.Player));

        // A full bucket of flax oil: as much as fits, the rest stays in the bucket.
        var bucket = site.BucketOf(OilSite.FlaxOil, 1000);
        Assert.True(site.RightClick(pos, bucket));
        Assert.Equal(1000, state.Tank.Points, 3);
        var left = site.Hand.Itemstack!;
        Assert.Equal(2f, ((BlockLiquidContainerBase)left.Block).GetCurrentLitres(left), 3);
        // Full: nothing more goes in, and the click is still the oil's.
        Assert.True(site.RightClick(pos, left));
        Assert.Equal(2f, ((BlockLiquidContainerBase)site.Hand.Itemstack!.Block).GetCurrentLitres(site.Hand.Itemstack), 3);

        // Saved and synced in the block entity's tree, and read back.
        var tree = new TreeAttribute();
        be.ToTreeAttributes(tree);
        Assert.Equal(1000, Oil.Read(tree, OilMachine.Pulverizer)!.Tank.Points, 3);

        // Jobs: each item crushed costs DrainPerJob; two items from a tank of one point run it dry.
        var crush = AccessTools.DeclaredMethod(typeof(BEPulverizer), "Crush");
        ItemStack Bauxite() => site.Stack("game:stone-bauxite", 1);
        be.Inventory[0].Itemstack = Bauxite();
        crush.Invoke(be, [0, 5, 0.0]);
        Assert.Equal(1000 - cfg.DrainPerJob, state.Tank.Points, 3);
        state.Tank = state.Tank with { Points = 2 * cfg.DrainPerJob - 0.1 };
        for (int i = 0; i < 2; i++)
        {
            be.Inventory[0].Itemstack = Bauxite();
            Assert.False(state.Dry);
            crush.Invoke(be, [0, 5, 0.0]);
        }
        Assert.True(state.Dry);
        Assert.Equal(0.085f * 3, mp.GetResistance(), 4);

        // Lost with the machine: a new one is dry.
        state.Tank = state.Tank.Fill(500);
        World.SetBlock("game:air", pos.UpCopy());
        World.SetBlock("game:air", pos);
        await World.Ticks(2);
        World.SetBlock(OilSite.Pulverizer, pos);
        await World.Ticks(2);
        Assert.True(ForeignMachines.StateOf(W.BlockAccessor.GetBlockEntity(pos))!.Dry);
    }

    [AtlasScenario]
    public async Task A_helve_hammer_loads_its_toggle_three_times_as_hard_until_oiled()
    {
        var p = await World.JoinPlayer("helveoiler");
        var pos = World.Spawn.AddCopy(100, 12, 110);
        await p.TeleportTo(pos.AddCopy(2, 0, 2));
        var site = new OilSite(World, pos, p.Player);
        await site.Clear();
        World.SetBlock(OilSite.Toggle, pos);
        await World.Ticks(2);
        var toggle = W.BlockAccessor.GetBlockEntity(pos)!.GetBehavior<BEBehaviorMPToggle>()!;
        var side = ((BlockPos[])AccessTools.Field(typeof(BEBehaviorMPToggle), "sides").GetValue(toggle)!)[0];
        World.SetBlock(OilSite.Helve, side);
        await World.Ticks(2);
        var hammer = Assert.IsType<BEHelveHammer>(W.BlockAccessor.GetBlockEntity(side));
        // Without a hammer head the toggle carries next to nothing, dry or not.
        Assert.Equal(0.0005f, toggle.GetResistance(), 5);
        hammer.HammerStack = site.Stack("game:helvehammer-iron");
        Assert.Equal(0.125f * 3, toggle.GetResistance(), 4);
        Assert.Contains("Dry: a load of 0.375 on its shaft, 3× the 0.125 it takes oiled", OilSite.Info(hammer, p.Player));

        Assert.True(site.RightClick(side, site.BucketOf(OilSite.FlaxOil, 300)));
        Assert.Equal(300, ForeignMachines.StateOf(hammer)!.Tank.Points, 3);
        Assert.Equal(0.125f, toggle.GetResistance(), 4);
        Assert.Contains("Oil: 300 of 1000", OilSite.Info(hammer, p.Player));
    }
}
