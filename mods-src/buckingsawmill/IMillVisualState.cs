using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace BuckingSawmill;

/// <summary>
/// What a renderer needs to draw the mill, implemented by its controller's block entity
/// (<see cref="BEBuckingMill"/>). Read on the client; everything here is synced from the server
/// except <see cref="ClientProgress"/>, which the client advances between syncs, and the shaft's
/// angle and speed, which come from the mechanical power network.
/// </summary>
public interface IMillVisualState
{
    /// <summary>The mill's facing, its block's <c>side</c> variant. South is the native frame of
    /// rig.json and the shape.</summary>
    BlockFacing Facing { get; }

    /// <summary>Sashes fitted, 0 to 2.</summary>
    int SashCount { get; }

    bool HasCrankshaft { get; }

    /// <summary>Blade kits fitted, 0 to 2, never more than <see cref="SashCount"/>.</summary>
    int BladeCount { get; }

    /// <summary>The blade kits' metal (the <c>metal</c> variant of
    /// <c>immersivewoodworking:sawmillblade-{metal}</c>), or null without blades.</summary>
    string? BladeMetal { get; }

    /// <summary>The loaded Logging Expanded trunk as its whole item stack, or null.</summary>
    ItemStack? Trunk { get; }

    /// <summary>How far the loaded trunk is cut, 0 to 1; 0 without a trunk.</summary>
    float ClientProgress { get; }

    /// <summary>The power ghost's shaft angle in radians (0 to 2π), as the mechanical power
    /// behavior reports it; 0 when unconnected.</summary>
    float ShaftAngle { get; }

    /// <summary>The power ghost's shaft speed (the consumer's TrueSpeed, unsigned); 0 when
    /// unconnected.</summary>
    float ShaftSpeed { get; }
}
