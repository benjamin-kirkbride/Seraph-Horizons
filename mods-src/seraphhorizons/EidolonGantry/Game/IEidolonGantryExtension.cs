using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>
/// A build that follows on from the gantry's own nine stages: the eidolon's body stages (#672),
/// fitted onto the spine. Implemented by a block entity behavior of the gantry's controller
/// (<c>entityBehaviors</c> in <c>blocktypes/eidolongantry/frame.json</c>), which the controller
/// (<see cref="BEEidolonGantry"/>) finds among its behaviors and asks, in their order, after its
/// winch. The behavior saves and syncs its own state (<c>ToTreeAttributes</c>/<c>FromTreeAttributes</c>),
/// writes its own block info (<c>GetBlockInfo</c>), and calls <see cref="BEEidolonGantry.MarkDirty"/>
/// when what it shows changes.
/// </summary>
public interface IEidolonGantryExtension
{
    /// <summary>A right-click on the gantry (any of its cells) that its winch did not take: every
    /// click while the winch is complete, and a click with an item that is not a winch part before.
    /// Called on both sides; the server acts. True when the click is this extension's (on the client:
    /// that it would be), which stops it going further. The extension checks
    /// <see cref="BEEidolonGantry.WinchComplete"/> itself (the body needs the spine).</summary>
    bool OnGantryInteract(BEEidolonGantry gantry, IPlayer byPlayer, ItemSlot? slot);

    /// <summary>Nothing more of this extension to fit (the creative shortcut then passes it by).</summary>
    bool Complete { get; }

    /// <summary>The creative shortcut, once the winch is complete (server side): fits this
    /// extension's next stage with nothing taken. False when there is nothing to fit.</summary>
    bool CreativeFitNext(BEEidolonGantry gantry, IPlayer? byPlayer);

    /// <summary>Whether the rig parts needing <paramref name="requires"/> are drawn (the body's
    /// stages, <c>GantryRequires.BodyStages</c>). Asked by the renderer every frame, on the client.</summary>
    bool Shows(string requires);

    /// <summary>Everything fitted that breaking the gantry gives back (server side).</summary>
    IEnumerable<ItemStack> Drops(IWorldAccessor world);

    /// <summary>Its help lines for <paramref name="forPlayer"/> looking at the gantry.</summary>
    IEnumerable<WorldInteraction> Help(BEEidolonGantry gantry, IPlayer? forPlayer);
}
