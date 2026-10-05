using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>
/// What a renderer needs to draw the rosser, implemented by its controller's block entity
/// (<see cref="BERosser"/>). Read on the client; everything here is synced from the server except
/// <see cref="ClientTravel"/>, which the client advances between syncs and interpolates per frame,
/// and the shaft's angle and speed, which come from the mechanical power network.
/// </summary>
public interface IRosserVisualState
{
    /// <summary>The rosser's facing, its block's <c>side</c> variant. South is the native frame of
    /// rig.json and the shape.</summary>
    Side Side { get; }

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn (the rig's
    /// <c>requires</c> vocabulary, <see cref="RosserRequires"/>; null always).</summary>
    bool Fitted(string? requires);

    /// <summary>The scraper heads' metal (the tips' texture); null without heads (spent heads are gone).</summary>
    string? HeadMetal { get; }

    /// <summary>The trunk in the rosser as its whole item stack, or null: as loaded until it is
    /// delivered, debarked after. Shown as the model of its class (<see cref="TrunkBox"/>).</summary>
    ItemStack? Trunk { get; }

    /// <summary>The trunk's class, k.</summary>
    TrunkClass TrunkClass { get; }

    /// <summary>T, blocks: the client's estimate, interpolated between its last two ticks for the
    /// frame being drawn.</summary>
    double ClientTravel { get; }

    /// <summary>Where the trunk is, from the client's trip.</summary>
    RosserState State { get; }

    /// <summary>Complete and turning at the server's MinSpeed or faster: the feed runs.</summary>
    bool Running { get; }

    /// <summary>Whether the next log would be scraped wet (synced): drip particles only.</summary>
    bool Wet { get; }

    /// <summary>The power ghost's shaft angle in radians, as the mechanical power behavior reports
    /// it; 0 when unconnected.</summary>
    float ShaftAngle { get; }

    /// <summary>The power ghost's shaft speed (the consumer's TrueSpeed); 0 when unconnected.</summary>
    float ShaftSpeed { get; }
}
