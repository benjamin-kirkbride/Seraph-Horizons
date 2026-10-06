using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

/// <summary>A creative rotor driving a consumer (a wooden toggle, what a helve hammer is driven
/// by) through MPE Gearbox's 1:5 gearbox, in a row running east on a granite floor: the rotor at
/// the west end, its output facing east. Blocks go in with <c>SetBlock</c>, then connect as their
/// own placement code connects them: the gearbox through <c>TryConnectFromPlacement</c> toward the
/// rotor (MPE Gearbox's <c>TryPlaceBlock</c>), the toggle through <c>tryConnect</c> toward the
/// gearbox (vanilla's <c>WasPlaced</c>). The rotor connects by itself: its behavior creates and
/// discovers its network when it is initialized. Shared with <see cref="SwitchesOffScenarios"/>.</summary>
internal sealed class GearboxTrain(IWorldSession world, BlockPos origin)
{
    public const string Rotor = "game:creativerotor-west";
    public const string Consumer = "game:woodentoggle-we";

    /// <summary>The gearbox with its low side toward the rotor (west), or its high side.</summary>
    public static string Gearbox(bool lowSideToRotor) => $"mpegearbox:gearbox15-{(lowSideToRotor ? "west" : "east")}";

    public BlockPos RotorPos { get; } = origin;
    public BlockPos GearboxPos { get; } = origin.EastCopy();
    public BlockPos ConsumerPos { get; } = origin.EastCopy(2);

    private IWorldAccessor W => world.Api.World;

    /// <summary>Clears a 5 x 3 x 3 room of air around the row on a granite floor.</summary>
    public async Task Clear()
    {
        for (int dx = -1; dx <= 3; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            world.SetBlock("game:rock-granite", RotorPos.AddCopy(dx, -1, dz));
            for (int dy = 0; dy < 3; dy++)
                world.SetBlock("game:air", RotorPos.AddCopy(dx, dy, dz));
        }
        await world.Ticks(2);
    }

    public async Task PlaceRotor()
    {
        world.SetBlock(Rotor, RotorPos);
        await world.Ticks(2);
        Assert.NotNull(Device(RotorPos));
    }

    public async Task PlaceGearbox(bool lowSideToRotor)
    {
        world.SetBlock(Gearbox(lowSideToRotor), GearboxPos);
        await world.Ticks(2);
        var gearbox = Device(GearboxPos);
        Assert.Equal(GearboxCoupling.GearboxBehaviorType, gearbox.GetType().FullName);
        AccessTools.Method(gearbox.GetType(), "TryConnectFromPlacement", [typeof(BlockFacing)])
            .Invoke(gearbox, [BlockFacing.WEST]);
        await world.Ticks(2);
    }

    public async Task PlaceConsumer()
    {
        world.SetBlock(Consumer, ConsumerPos);
        await world.Ticks(2);
        Device(ConsumerPos).tryConnect(BlockFacing.WEST);
        await world.Ticks(2);
    }

    /// <summary>Breaks the consumer: the game rebuilds the network from its sources.</summary>
    public async Task RemoveConsumer()
    {
        W.BlockAccessor.SetBlock(0, ConsumerPos);
        await world.Ticks(2);
    }

    public BEBehaviorMPBase Device(BlockPos pos) =>
        W.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()
        ?? throw new Xunit.Sdk.XunitException($"no mechanical power behavior at {pos}");

    /// <summary>Each block's stored <c>GearedRatio</c> (what it saves as <c>g</c>), in the row's
    /// order, the consumer's only when it is there.</summary>
    public float[] Ratios(bool consumer = true) =>
        (consumer ? new[] { RotorPos, GearboxPos, ConsumerPos } : new[] { RotorPos, GearboxPos })
        .Select(p => Device(p).GearedRatio).ToArray();

    /// <summary>Every block in the row on one network.</summary>
    public void AssertOneNetwork(bool consumer = true)
    {
        var rotor = Device(RotorPos).Network;
        Assert.NotNull(rotor);
        Assert.Same(rotor, Device(GearboxPos).Network);
        if (consumer)
            Assert.Same(rotor, Device(ConsumerPos).Network);
    }

    public static bool Patched() =>
        Harmony.GetPatchInfo(AccessTools.DeclaredMethod(typeof(BEBehaviorMPBase),
                nameof(BEBehaviorMPBase.CreateJoinAndDiscoverNetwork), [typeof(BlockFacing)]))
            ?.Postfixes.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) == true;
}

/// <summary>
/// mods-src/seraphhorizons, GearboxSourceRatio (#462): a power source that creates its network
/// through an MPE Gearbox gearbox takes the ratio of the side it touches. Without the patch the
/// rotor placed after its gearbox (or rebuilt after a block on its network is broken) takes the far
/// side's: 5 on the low side, 0.2 on the high side.
/// </summary>
public partial class SharedWorldScenarios
{
    private static readonly float[] LowSide = [1f, 1f, 5f];
    private static readonly float[] HighSide = [1f, 0.2f, 0.2f];

    private void AssertRatios(float[] expected, float[] actual)
    {
        output.WriteLine($"ratios (rotor, gearbox, consumer): {string.Join(", ", actual)}");
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) < 1e-4f,
                $"expected {string.Join(", ", expected)}, got {string.Join(", ", actual)}");
    }

    // The order the game already handles: the gearbox and the consumer join the rotor's network
    // through tryConnect. Here as the reference the other orders must match.
    [AtlasScenario]
    public async Task Gearbox_placed_after_the_rotor_takes_the_rotor_ratio()
    {
        Assert.True(GearboxTrain.Patched());
        var train = new GearboxTrain(World, World.Spawn.AddCopy(0, 12, -60));
        await train.Clear();
        await train.PlaceRotor();
        await train.PlaceGearbox(lowSideToRotor: true);
        await train.PlaceConsumer();
        train.AssertOneNetwork();
        AssertRatios(LowSide, train.Ratios());
    }

    [AtlasScenario]
    public async Task Rotor_placed_after_the_gearbox_on_its_low_side_keeps_ratio_1()
    {
        Assert.True(GearboxTrain.Patched());
        var train = new GearboxTrain(World, World.Spawn.AddCopy(0, 12, -64));
        await train.Clear();
        await train.PlaceGearbox(lowSideToRotor: true);
        await train.PlaceConsumer();
        await train.PlaceRotor();
        train.AssertOneNetwork();
        AssertRatios(LowSide, train.Ratios());
    }

    [AtlasScenario]
    public async Task Rotor_placed_after_the_gearbox_on_its_high_side_keeps_ratio_1()
    {
        Assert.True(GearboxTrain.Patched());
        var train = new GearboxTrain(World, World.Spawn.AddCopy(0, 12, -68));
        await train.Clear();
        await train.PlaceGearbox(lowSideToRotor: false);
        await train.PlaceConsumer();
        await train.PlaceRotor();
        train.AssertOneNetwork();
        AssertRatios(HighSide, train.Ratios());
    }

    // MechanicalPowerMod.RebuildNetwork: every block leaves the network, and each source creates
    // and discovers a new one, the path the patch corrects.
    [AtlasScenario]
    public async Task Rotor_rebuilt_after_a_block_is_broken_keeps_ratio_1()
    {
        Assert.True(GearboxTrain.Patched());
        var train = new GearboxTrain(World, World.Spawn.AddCopy(0, 12, -72));
        await train.Clear();
        await train.PlaceRotor();
        await train.PlaceGearbox(lowSideToRotor: true);
        await train.PlaceConsumer();
        AssertRatios(LowSide, train.Ratios());

        await train.RemoveConsumer();
        train.AssertOneNetwork(consumer: false);
        AssertRatios(LowSide[..2], train.Ratios(consumer: false));

        await train.PlaceConsumer();
        train.AssertOneNetwork();
        AssertRatios(LowSide, train.Ratios());
    }
}
