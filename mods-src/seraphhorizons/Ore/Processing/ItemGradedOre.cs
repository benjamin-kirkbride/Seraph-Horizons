using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Vanilla's graded ore and crystallised ore items with ore processing on (#686;
/// <c>patches/oreprocessing-ore.json</c> sets this class on the server, and a client builds the
/// items from the server's, so it follows the server's switch). Everything vanilla's
/// <see cref="ItemOre"/> does, except:
/// <list type="bullet">
/// <item>its name: poor and medium ore are raw ore ("Raw galena ore (medium)"), rich and bountiful
/// chunks ("Galena chunk (rich)"); crystallised ore keeps vanilla's;</item>
/// <item>its text: no "crush with hammer to extract nuggets", but what the form does instead;</item>
/// <item>a hammer no longer breaks it into nuggets where it lies on the ground: it spalls it, a
/// left-click a blow, into crushed ore by the 5-unit rule (#747, <see cref="OreSpalling"/>), and
/// shift + right-click on a placed ore with another in hand sets that one on the next block.</item>
/// </list>
/// The stack sizes, crushing and smelting are set on the server by <see cref="OreProcessingSystem"/>.
/// </summary>
public class ItemGradedOre : ItemOre, IContainedInteractable
{
    private bool IsGradedOre => FirstCodePart() == "ore" && OreProducts.FormOfGrade(Variant["grade"]) != null;

    public override string GetHeldItemName(ItemStack itemStack)
    {
        if (!IsGradedOre)
            return base.GetHeldItemName(itemStack);
        string grade = Variant["grade"];
        string ore = Lang.Get("ore-" + Variant["ore"]);
        return OreProducts.FormOfGrade(grade) == OreForm.Raw
            ? Lang.Get("seraphhorizons:oreprocessing-rawore", ore, Lang.Get("seraphhorizons:oreprocessing-grade-" + grade))
            : Lang.Get("seraphhorizons:oreprocessing-chunk", ore, Lang.Get("seraphhorizons:oreprocessing-grade-" + grade));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        var own = new StringBuilder();
        base.GetHeldItemInfo(inSlot, own, world, withDebugInfo);
        string hammer = Lang.Get("Crush with hammer to extract nuggets");
        string text = own.ToString();
        string instead = Lang.Get(OreProducts.FormOfGrade(Variant["grade"]) == OreForm.Raw
            ? "seraphhorizons:oreprocessing-rawore-info"
            : "seraphhorizons:oreprocessing-chunk-info");
        dsc.Append(text.Contains(hammer) ? text.Replace(hammer, instead) : text + instead + "\n");
    }

    /// <summary>Whether this item spalls (#747): a graded ore or crystallised ore of a grade with a
    /// form.</summary>
    public bool Spalls => Spalling.Spalls(FirstCodePart(), Variant["grade"]);

    // A hammer broke vanilla's ore into nuggets where it lay (ItemOre, a ground storage interaction:
    // shift + right-click with a hammer). With ore processing that is gone: a hammer spalls it with a
    // left-click instead (CollectibleBehaviorSpalling). The interaction here is shift + right-click
    // with another spalling ore in hand: it goes on the next block, as the ground holds one ore a block.
    public new bool OnContainedInteractStart(BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!Spalls || byPlayer?.Entity?.Controls.ShiftKey != true
            || byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is not ItemGradedOre { Spalls: true })
            return false;
        // Taken either way, so a full hand never picks the placed ore up instead.
        OreSpalling.PlaceBeside(be.Api.World, be.Pos, byPlayer, blockSel);
        return true;
    }

    public new bool OnContainedInteractStep(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel) => false;

    public new void OnContainedInteractStop(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
    }

    public new bool OnContainedInteractCancel(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason) => true;

    public new WorldInteraction[] GetContainedInteractionHelp(BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!Spalls || api == null)
            return [];
        return ObjectCacheUtil.GetOrCreate(api, "seraphhorizons-spalling-help", () =>
        {
            // The hammers that spall: those the server gave the behaviour (ore processing on).
            var hammers = api.World.Items
                .Where(i => i?.Code != null && !i.IsMissing && i.HasBehavior<CollectibleBehaviorSpalling>())
                .Select(i => new ItemStack(i)).ToArray();
            return hammers.Length == 0
                ? Array.Empty<WorldInteraction>()
                : [new WorldInteraction { ActionLangCode = "seraphhorizons:spalling-strike", MouseButton = EnumMouseButton.Left, Itemstacks = hammers }];
        });
    }
}
