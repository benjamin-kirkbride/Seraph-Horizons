using System.Text;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.SteelBits;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/CrucibleFurnace/Game/BessemerStainless.cs (<c>StainlessSteel</c>, #484
/// part D): ferrochrome in Steelmaking Expanded's Bessemer converter. A whole converter (control,
/// vessel built through its stages, transmission, intake, canals, steam and blast) is more than a
/// scenario can raise, so a lone control block is placed and its heat set as smex's own code leaves
/// it, then smex's own methods are run (by reflection, through the patches): the blow completing,
/// the pour, the save, the block info, and a charge refused while the plant is not complete.
/// </summary>
public partial class SharedWorldScenarios
{
    private const string Control = "SteelmakingExpanded.BlockStructures.Converter.BlockEntities.BlockEntityConverterControl";

    [AtlasScenario]
    public async Task Ferrochrome_in_the_Bessemer_converter_makes_the_heat_pour_stainless()
    {
        if (!World.Api.ModLoader.IsModEnabled(BessemerStainless.SmexId))
        {
            output.WriteLine("Steelmaking Expanded is not installed: nothing to check");
            return;
        }
        Assert.True(BessemerStainless.Bound, "smex's converter members are not as expected (see the log)");
        Assert.True(Harmony.HasAnyPatches(BessemerStainless.HarmonyId));
        Assert.Contains(CrucibleFurnaceSystem.Of(World.Api).BessemerScrapStatus, new[] { SmexScrap.Status.Listed, SmexScrap.Status.Added });
        Assert.True(SteelBitsRules.Lists(SmexScrap.Codes(), Stainless.Ferrochrome), $"smex's scrap list is {SmexScrap.Codes()}");
        var isScrap = AccessTools.Method(Control + ":IsScrap");
        Assert.True((bool)isScrap.Invoke(null, [new ItemStack(W.GetItem(new AssetLocation(Stainless.Ferrochrome))!)])!);
        Assert.Contains("Ferrochrome makes it stainless", Lang.GetL("en", "smex:handbook-bessemer-text"));
        Assert.Equal(1530f, BessemerStainless.StainlessMeltingPoint(W));

        var pos = World.Spawn.AddCopy(-340, 30, -340);
        if (World.Api is ICoreServerAPI sapi)
            sapi.WorldManager.LoadChunkColumnPriority(pos.X / GlobalConstants.ChunkSize, pos.Z / GlobalConstants.ChunkSize);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(pos) != null, 30000);
        World.SetBlock("smex:convertercontrol-north", pos);
        await World.Ticks(5);
        var control = W.BlockAccessor.GetBlockEntity(pos) ?? throw new Xunit.Sdk.XunitException($"no converter control at {pos}");
        Assert.Equal(Control, control.GetType().FullName);
        var type = control.GetType();
        var scrap = (Dictionary<string, int>)AccessTools.Field(type, "_scrap").GetValue(control)!;

        ItemStack Molten(string code, float temperature)
        {
            var stack = new ItemStack(W.GetItem(new AssetLocation(code))!);
            stack.Collectible.SetTemperature(W, stack, temperature, false);
            return stack;
        }
        void SetHeat(string code, int units, float temperature)
        {
            AccessTools.Field(type, "_content").SetValue(control, Molten(code, temperature));
            AccessTools.Field(type, "_contentUnits").SetValue(control, units);
        }
        void Run(string method, params object[] args) => AccessTools.Method(type, method).Invoke(control, args);
        string Code() => BessemerStainless.Content(control)!.Collectible.Code.ToString();
        string Info()
        {
            var info = new StringBuilder();
            Run("AppendStructureState", null!, info);
            return info.ToString();
        }

        try
        {
            // Before the blow: 800 units of iron and 200 of ferrochrome charged as scrap (40 lumps).
            SetHeat("game:ingot-iron", 800, 1700f);
            scrap[Stainless.Ferrochrome] = 200;
            Assert.Contains(Lang.Get("seraphhorizons:bessemer-ferrochrome-onratio", "1530"), Info());
            // The blow completes: smex melts the scrap in, and the ferrochrome is in the heat.
            Run("CompleteRefining");
            Assert.Equal("game:ingot-steel", Code());
            Assert.Equal(1000, BessemerStainless.ContentUnits(control));
            Assert.Empty(scrap);
            Assert.Equal(200, BessemerStainless.Heat(control));
            Assert.Contains(Lang.Get("seraphhorizons:bessemer-info-ferrochrome", 200, 20, Lang.Get("seraphhorizons:bessemer-ferrochrome-stainless")), Info());

            // Saved and loaded (and so synced): the heat's ferrochrome comes back.
            var tree = new TreeAttribute();
            control.ToTreeAttributes(tree);
            Assert.Equal(200, tree.GetInt(BessemerStainless.HeatKey));
            BessemerStainless.SetHeat(control, 0);
            control.FromTreeAttributes(tree, W);
            Assert.Equal(200, BessemerStainless.Heat(control));

            // The pour: stainless, unit for unit, at the bath's heat.
            Run("TickPouring", 0.05f);
            Assert.Equal(Stainless.Ingot, Code());
            Assert.Equal(1000, BessemerStainless.ContentUnits(control));
            Assert.Equal(0, BessemerStainless.Heat(control));
            var content = BessemerStainless.Content(control)!;
            Assert.InRange(content.Collectible.GetTemperature(W, content), 1690f, 1700f);
            Assert.DoesNotContain("Ferrochrome:", Info());

            // Off the ratio (30 %): steel, the ferrochrome to the slag.
            SetHeat("game:ingot-steel", 1000, 1700f);
            BessemerStainless.SetHeat(control, 300);
            Assert.Contains(Lang.Get("seraphhorizons:bessemer-ferrochrome-offratio", 18, 22), Info());
            Run("TickPouring", 0.05f);
            Assert.Equal("game:ingot-steel", Code());
            Assert.Equal(700, BessemerStainless.ContentUnits(control));

            // On the ratio but under stainless's melting point: steel too.
            SetHeat("game:ingot-steel", 1000, 1515f);
            BessemerStainless.SetHeat(control, 200);
            Assert.Contains(Lang.Get("seraphhorizons:bessemer-ferrochrome-toocold", "1530"), Info());
            Run("TickPouring", 0.05f);
            Assert.Equal("game:ingot-steel", Code());
            Assert.Equal(800, BessemerStainless.ContentUnits(control));

            // After the blow, ferrochrome goes straight into the molten steel (smex refuses it as
            // scrap there). The plant here is not complete, so the click is refused with smex's word
            // and nothing taken; the charge itself, past those checks, books it in the heat.
            SetHeat("game:ingot-steel", 800, 1700f);
            BessemerStainless.SetHeat(control, 0);
            var player = (await World.JoinPlayer("bessemerhand")).Player;
            var hand = player.InventoryManager.ActiveHotbarSlot;
            hand.Itemstack = new ItemStack(W.GetItem(new AssetLocation(Stainless.Ferrochrome))!, 40);
            object?[] args = [player, null];
            Assert.True((bool)AccessTools.Method(type, "TryChargeScrap").Invoke(control, args)!);
            Assert.Equal(Lang.Get("smex:bessemer-err-incomplete"), args[1]);
            Assert.Equal(40, hand.StackSize);
            Assert.Equal(40, BessemerStainless.ChargeIntoHeat(control, hand, 100));
            Assert.Equal(1000, BessemerStainless.ContentUnits(control));
            Assert.Equal(200, BessemerStainless.Heat(control));
            Assert.Equal(BessemerHeat.Outcome.Stainless, BessemerStainless.Outcome(control));
            hand.Itemstack = null;

            // Emptied, the ledger reads as none.
            AccessTools.Field(type, "_content").SetValue(control, null);
            AccessTools.Field(type, "_contentUnits").SetValue(control, 0);
            Assert.Equal(0, BessemerStainless.Heat(control));
        }
        finally
        {
            W.BlockAccessor.SetBlock(0, pos);
        }
    }
}
