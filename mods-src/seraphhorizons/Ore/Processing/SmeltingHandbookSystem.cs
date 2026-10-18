using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Patches <see cref="BloomeryBullion"/> (both sides, once per process: the client says why the bloomery
/// refuses) and <see cref="SmeltingHandbook"/> (client). Neither is switched: without ore processing there
/// is no bullion to refuse, and the handbook's counts are the game's numbers made readable.
/// </summary>
public class SmeltingHandbookSystem : ModSystem
{
    private Harmony? _bloomery, _handbook;

    public override void Start(ICoreAPI api)
    {
        if (Harmony.HasAnyPatches(BloomeryBullion.HarmonyId))
            return;
        var harmony = new Harmony(BloomeryBullion.HarmonyId);
        if (BloomeryBullion.Patch(harmony, api.Logger))
            _bloomery = harmony;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (Harmony.HasAnyPatches(SmeltingHandbook.HarmonyId))
            return;
        var harmony = new Harmony(SmeltingHandbook.HarmonyId);
        try
        {
            if (!SmeltingHandbook.Patch(harmony, api.Logger))
                return;
        }
        catch (Exception e)
        {
            harmony.UnpatchAll(harmony.Id);
            api.Logger.Warning("[seraphhorizons] Smelting handbook: could not patch the handbook ({0}); it shows the game's smelting ratios", e.Message);
            return;
        }
        SmeltingHandbook.Bind(api);
        _handbook = harmony;
    }

    public override void Dispose()
    {
        _bloomery?.UnpatchAll(_bloomery.Id);
        _bloomery = null;
        if (_handbook != null)
        {
            _handbook.UnpatchAll(_handbook.Id);
            SmeltingHandbook.Unbind();
        }
        _handbook = null;
    }
}
