using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
/// <item>a hammer no longer breaks it into nuggets where it lies on the ground: crushing goes by the
/// 5-unit rule (the pulverizer now, the spalling station of #747 later).</item>
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

    // A hammer breaks vanilla's ore into nuggets where it lies (ItemOre, a ground storage
    // interaction); with ore processing it does nothing, so ore turns into metal only by the rule.
    public new bool OnContainedInteractStart(BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel) => false;

    public new bool OnContainedInteractStep(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel) => false;

    public new void OnContainedInteractStop(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
    }

    public new bool OnContainedInteractCancel(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason) => true;

    public new WorldInteraction[] GetContainedInteractionHelp(BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel) => [];
}
