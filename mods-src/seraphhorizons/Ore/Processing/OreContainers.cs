using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>What the cupel (#722) and the liquation pan (#724) share: reading a roasted concentrate in
/// their charge, the charge's heat, and giving a player the items a spent container breaks into.</summary>
public static class OreContainers
{
    public const string RoastedPrefix = "roastedconcentrate-";

    /// <summary>The ore and metal units of a roasted concentrate stack; null for anything else.</summary>
    public static (string Ore, double Units)? Roasted(OreRecovery r, ItemStack stack)
    {
        var code = stack.Collectible.Code;
        if (code.Domain != OreProducts.Domain || !code.Path.StartsWith(RoastedPrefix, StringComparison.Ordinal)) return null;
        double per = stack.Collectible.Attributes?["metalUnits"].AsDouble(r.ConcentrateUnits) ?? r.ConcentrateUnits;
        return (code.Path[RoastedPrefix.Length..], stack.StackSize * per);
    }

    /// <summary>The charge's heat: its coolest stack's, as the firepit and the game's crucible read it;
    /// null with nothing in it.</summary>
    public static float? ChargeTemperature(IWorldAccessor world, ISlotProvider provider)
    {
        float? coolest = null;
        foreach (var slot in provider.Slots)
            if (slot?.Itemstack is { } stack)
            {
                float t = stack.Collectible.GetTemperature(world, stack);
                coolest = coolest is { } c ? Math.Min(c, t) : t;
            }
        return coolest;
    }

    /// <summary>Gives a player items by code, on the server; what their bags can't hold drops at their feet.</summary>
    public static void Give(IPlayer? byPlayer, IEnumerable<(string Code, int Count)> items)
    {
        var world = byPlayer?.Entity?.World;
        if (world == null || world.Side != EnumAppSide.Server) return;
        foreach (var (code, count) in items)
        {
            if (count <= 0 || world.GetItem(new AssetLocation(code)) is not { } item) continue;
            var stack = new ItemStack(item, count);
            if (!byPlayer!.InventoryManager.TryGiveItemstack(stack, slotNotifyEffect: true))
                world.SpawnItemEntity(stack, byPlayer.Entity.Pos.XYZ);
        }
    }
}
