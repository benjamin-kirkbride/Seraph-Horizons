using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>A creative player's middle click on a trunk entity, client to server, on
/// <see cref="TrunkPick.Channel"/>.</summary>
[ProtoContract]
public class TrunkPickRequest
{
    /// <summary>The trunk entity picked.</summary>
    [ProtoMember(1)]
    public long EntityId { get; set; }

    /// <summary>The hotbar slot the client chose, as the game's own pick does (the active one if
    /// empty, else the first empty one, else the active one).</summary>
    [ProtoMember(2)]
    public int Slot { get; set; }
}

/// <summary>
/// The game's "pickblock" hotkey (middle click) for trunk entities, which it only does for blocks:
/// in creative, middle-clicking a trunk entity puts a copy of its exact stack (wood, size, branches,
/// logs) in the hotbar, into the slot the game's own pick would use. A client listens for the
/// hotkey beside the game's handler (never in its place) and asks; the server checks the mode again,
/// so a survival player gets nothing. Placing that item from the hotbar lays a copy down
/// (<see cref="OldTrunkBlocks"/>; creative does not use it up).
/// </summary>
public static class TrunkPick
{
    public const string Channel = "seraphhorizons:trunkpick";

    /// <summary>The game's pick hotkey code.</summary>
    public const string HotkeyCode = "pickblock";

    /// <summary>Registers the channel; on both sides, from <see cref="TrunkEntitySystem.Start"/>.</summary>
    internal static void Register(ICoreAPI api) => api.Network.RegisterChannel(Channel).RegisterMessageType<TrunkPickRequest>();

    internal static void StartServer(ICoreServerAPI api) =>
        api.Network.GetChannel(Channel).SetMessageHandler<TrunkPickRequest>((player, request) => Give(player, request.EntityId, request.Slot));

    internal static void StartClient(ICoreClientAPI api)
    {
        var channel = api.Network.GetChannel(Channel);
        api.Input.AddHotkeyListener((code, combination) =>
        {
            if (code == HotkeyCode && !combination.OnKeyUp && Request(api) is { } request)
                channel.SendPacket(request);
        });
    }

    /// <summary>The request for the client's current middle click, or null: not creative, or no
    /// trunk entity selected. Moves the client's active slot as the game's pick does.</summary>
    private static TrunkPickRequest? Request(ICoreClientAPI api)
    {
        var player = api.World.Player;
        if (player?.WorldData?.CurrentGameMode != EnumGameMode.Creative || player.CurrentEntitySelection?.Entity is not EntityTrunk { Alive: true } trunk
            || trunk.Trunk is null || player.InventoryManager.GetHotbarInventory() is not { } hotbar)
            return null;
        int slot = SlotFor(player, hotbar);
        if (slot < 0)
            return null;
        player.InventoryManager.ActiveHotbarSlotNumber = slot;
        return new TrunkPickRequest { EntityId = trunk.EntityId, Slot = slot };
    }

    /// <summary>The hotbar slot the game's pick fills: the active one if empty, else the first empty
    /// one, else the active one. -1 without a hotbar.</summary>
    public static int SlotFor(IPlayer player, IInventory hotbar)
    {
        int active = player.InventoryManager.ActiveHotbarSlotNumber;
        if (Usable(hotbar, active) && hotbar[active]?.Empty == true)
            return active;
        for (int i = 0; i < hotbar.Count; i++)
            if (Usable(hotbar, i) && hotbar[i]?.Empty == true)
                return i;
        return Usable(hotbar, active) ? active : -1;
    }

    private static bool Usable(IInventory hotbar, int i) =>
        i >= 0 && i < hotbar.Count && hotbar[i] is { } s && (s.StorageType & (EnumItemStorageFlags.Backpack | EnumItemStorageFlags.Offhand)) == 0;

    /// <summary>Gives a creative <paramref name="player"/> a copy of trunk entity
    /// <paramref name="entityId"/>'s stack into hotbar slot <paramref name="slot"/> (the slot the
    /// game's pick would use when that one is not a hotbar slot). Nothing for anyone else, or for
    /// what is not a trunk entity. Server side; true when given.</summary>
    public static bool Give(IServerPlayer player, long entityId, int slot)
    {
        if (player?.Entity is not { } by || player.WorldData?.CurrentGameMode != EnumGameMode.Creative
            || by.World.GetEntityById(entityId) is not EntityTrunk { Alive: true } trunk || trunk.Trunk is not { } stack
            || !TrunkPockets.MayGive(player, stack) || player.InventoryManager.GetHotbarInventory() is not { } hotbar)
            return false;
        if (!Usable(hotbar, slot))
            slot = SlotFor(player, hotbar);
        if (slot < 0)
            return false;
        var copy = stack.Clone();
        copy.StackSize = 1;
        if (hotbar[slot] is not { } target)
            return false;
        target.Itemstack = copy;
        target.MarkDirty();
        by.World.Logger.Audit("{0} picked a trunk entity in creative: {1} into hotbar slot {2}", player.PlayerName, copy.Collectible?.Code, slot);
        return true;
    }
}
