using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Trading.Deliveries;

/// <summary>
/// <c>seraphhorizons:package</c> (#454): a sealed package for a delivery, stack size 1, with no way
/// to open it (the linen sack's model without its bag behaviours). The delivery is in its
/// attributes, written by <see cref="DeliveriesSystem"/>: <see cref="AttrId"/>, the sender and
/// receiver ids, the receiver's type and position, the deadline in total game days, and
/// <see cref="AttrFailed"/> once the delivery has failed, after which it is worthless junk. The
/// value table doesn't list it, so no trader buys it.
/// </summary>
public class ItemPackage : Item
{
    public const string ClassName = "seraphhorizons.Package";
    public static readonly AssetLocation PackageCode = new("seraphhorizons", "package");
    public const string AttrId = "deliveryId";
    public const string AttrFrom = "from";
    public const string AttrTo = "to";
    public const string AttrToType = "toType";
    public const string AttrToX = "toX";
    public const string AttrToZ = "toZ";
    public const string AttrDeadline = "deadline";
    public const string AttrFailed = "failed";

    public static int IdOf(ItemStack? stack) =>
        stack?.Collectible is ItemPackage ? stack.Attributes.GetInt(AttrId, 0) : 0;

    public override string GetHeldItemName(ItemStack itemStack)
    {
        var a = itemStack.Attributes;
        if (a.GetBool(AttrFailed)) return Lang.Get("seraphhorizons:package-name-failed");
        string type = a.GetString(AttrToType, "");
        return type.Length == 0 ? base.GetHeldItemName(itemStack)
            : Lang.Get("seraphhorizons:package-name", Lang.Get("seraphhorizons:trading-type-" + type));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var a = inSlot.Itemstack?.Attributes;
        if (a is null || a.GetInt(AttrId, 0) == 0)
        {
            dsc.AppendLine(Lang.Get("seraphhorizons:package-info-blank"));
            return;
        }
        if (a.GetBool(AttrFailed))
        {
            dsc.AppendLine(Lang.Get("seraphhorizons:package-info-failed"));
            return;
        }
        // Positions as the game shows them to players: relative to the world spawn.
        var spawn = world.DefaultSpawnPosition;
        int x = (int)(a.GetDouble(AttrToX) - (spawn?.X ?? 0)), z = (int)(a.GetDouble(AttrToZ) - (spawn?.Z ?? 0));
        dsc.AppendLine(Lang.Get("seraphhorizons:package-info-to", Lang.Get("seraphhorizons:trading-type-" + a.GetString(AttrToType, "")), x, z));
        double left = a.GetDouble(AttrDeadline) - world.Calendar.TotalDays;
        dsc.AppendLine(left >= 0
            ? Lang.Get("seraphhorizons:package-info-due", Math.Round(left * world.Calendar.HoursPerDay, 1))
            : Lang.Get("seraphhorizons:package-info-late"));
        dsc.AppendLine(Lang.Get("seraphhorizons:package-info-use"));
    }
}
