using SeraphHorizons.Mod.BuckingSawmill.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// What a renderer needs to draw the mill, implemented by its controller's block entity
/// (<see cref="BEBuckingMill"/>). Read on the client; everything here is synced from the server
/// except <see cref="ClientProgress"/> and <see cref="ClientSawDepth"/>, which the client advances
/// between syncs, and the shaft's angle and speed, which come from the mechanical power network.
/// </summary>
public interface IMillVisualState
{
    /// <summary>The mill's facing, its block's <c>side</c> variant. South is the native frame of
    /// rig.json and the shape.</summary>
    BlockFacing Facing { get; }

    /// <summary>Sashes fitted, 0 to 2.</summary>
    int SashCount { get; }

    bool HasCrankshaft { get; }

    /// <summary>Whether the levers (Immersive Woodworking's <c>sawmilllevers</c>) are fitted: the
    /// linkage that trips the windlass when the saws bottom out.</summary>
    bool HasLevers { get; }

    /// <summary>Whether the blade kit is fitted: one kit, a blade in each saw, fitted only with both
    /// sashes.</summary>
    bool HasBladeKit { get; }

    /// <summary>The blade kit's metal (the <c>metal</c> variant of
    /// <c>immersivewoodworking:sawmillblade-{metal}</c>), or null without one.</summary>
    string? BladeMetal { get; }

    /// <summary>The loaded Logging Expanded trunk as its whole item stack, or null. The renderer
    /// shows it as the model of its class (<see cref="TrunkBox"/>), not of its own size.</summary>
    ItemStack? Trunk { get; }

    /// <summary>How far the loaded trunk is cut, 0 to 1; 0 without a trunk.</summary>
    float ClientProgress { get; }

    /// <summary>What the mill is doing: Stopped unless complete and turning, else Raising while the
    /// saws are wound up, Cutting with a trunk loaded, else Sinking. From the server's synced state.</summary>
    MillPhase Phase { get; }

    /// <summary>Whether the saws are being wound up: the client's estimate, which turns at the top
    /// and the bottom of the travel as the client's depth does.</summary>
    bool ClientRising { get; }

    /// <summary>The saws' depth, 0 at the top to 1 at the bed through the trunk: the
    /// client's estimate, advanced with the shaft between syncs and eased toward the server's value
    /// when a sync moves it (a new trunk's drop takes a fraction of a second).</summary>
    float ClientSawDepth { get; }

    /// <summary>The power ghost's shaft angle in radians (0 to 2π), as the mechanical power
    /// behavior reports it; 0 when unconnected.</summary>
    float ShaftAngle { get; }

    /// <summary>The power ghost's shaft speed (the consumer's TrueSpeed, unsigned); 0 when
    /// unconnected.</summary>
    float ShaftSpeed { get; }
}
