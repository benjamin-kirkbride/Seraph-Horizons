using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>The game half of <see cref="Footprint.WorldPoint"/> and <see cref="Footprint.NativePoint(Int3, Int3, Double3, Side)"/>:
/// a machine's native-frame points to and from the world, in doubles (#864).</summary>
public static class MachinePoints
{
    public static Int3 ToInt3(this BlockPos pos) => new(pos.X, pos.Y, pos.Z);

    /// <summary>A native-frame point of the machine whose controller is at <paramref name="controller"/>
    /// as a world position.</summary>
    public static Vec3d WorldPoint(BlockPos controller, Float3 native, Side facing)
    {
        var d = Footprint.WorldPoint(controller.ToInt3(), native, facing);
        return new Vec3d(d.X, d.Y, d.Z);
    }

    /// <summary>A selection's hit on <paramref name="cell"/> (relative to that block) in the native frame.</summary>
    public static Float3 NativeHit(BlockPos controller, BlockPos cell, Vec3d hit, Side facing) =>
        Footprint.NativePoint(controller.ToInt3(), cell.ToInt3(), new Double3(hit.X, hit.Y, hit.Z), facing);
}
