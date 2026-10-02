using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.TidyVariants;

/// <summary>
/// Client-only creative inventory UI for Tidy Variants (#256; docs/variant-grouping/creative.md): hides
/// hidden variants, collapses groups into tiles after search, expands them on alt+click or a hotkey.
///
/// Its Harmony patches are applied by hand here, with their own id, on the client only: the main mod
/// system's <c>PatchAll</c> runs on both sides and GUI patches must never be applied on a dedicated server.
/// Nothing here uses <c>[HarmonyPatch]</c> attributes, so <c>PatchAll</c> never picks it up.
/// </summary>
public sealed class TidyCreativeModSystem : ModSystem
{
    public const string HarmonyId = "tidyvariants.creative";
    public const string HotkeyCode = "tidyvariants-togglegroup";

    private Harmony? _harmony;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        CreativeUi.Init(api);
        try
        {
            api.Input.RegisterHotKey(HotkeyCode, Lang.Get("tidyvariants:creative-hotkey-toggle"), GlKeys.G, HotkeyType.CreativeTool, altPressed: false, ctrlPressed: true);
            api.Input.SetHotKeyHandler(HotkeyCode, CreativeUi.OnToggleHotkey);
        }
        catch (Exception ex)
        {
            api.Logger.Error("[tidyvariants] could not register the creative group hotkey: {0}", ex);
        }

        _harmony = new Harmony(HarmonyId);
        int applied = CreativePatches.Apply(_harmony, api.Logger);
        api.Logger.Notification("[tidyvariants] creative inventory: {0}/{1} patches applied", applied, CreativePatches.Count);
    }

    public override void Dispose()
    {
        try { _harmony?.UnpatchAll(HarmonyId); }
        catch { /* shutting down */ }
        _harmony = null;
        CreativeUi.Reset();
    }
}
