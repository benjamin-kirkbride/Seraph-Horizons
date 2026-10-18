using HarmonyLib;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Trading.Values;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Big piles, big crates, the rusty gear's stack and no clutter piles (README "Big piles", "Big
/// crates", "The rusty gear stacks to 64", "Clutter and chiseled blocks do not pile"), and the
/// handbook's pile line: <see cref="BigPiles"/>, <see cref="BigCrates"/>,
/// <see cref="RustyGearStack"/>, <see cref="ClutterPiles"/>, <see cref="PileHandbook"/>.
///
/// ExecuteOrder 0.2, so its <c>AssetsFinalize</c> runs after More Piles' (its <c>Core</c>, the
/// default 0.1), which gives most blocks and some items their piles there, and after
/// <see cref="RockStackSystem"/> (0.15), which cuts rock and earth stacks there: a pile's capacity
/// is 64 of the final stack. <c>BigPilesScenarios</c> checks every stacking pile of the loaded pack
/// is big, so a mod that added piles later than this would fail it.
/// </summary>
public class StorageTweaksSystem : ModSystem
{
    /// <summary>Stacking piles made big on this side.</summary>
    public int PilesMadeBig { get; private set; }

    /// <summary>Clutter blocks whose More Piles pile was taken away on this side (<see cref="ClutterPiles"/>).</summary>
    public int ClutterPilesTaken { get; private set; }

    /// <summary>Crate blocks given more slots (server side).</summary>
    public int CratesMadeBig { get; private set; }

    private Harmony? _handbookHarmony;

    public static StorageTweaksSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<StorageTweaksSystem>();

    public override double ExecuteOrder() => 0.2;

    public override void Start(ICoreAPI api)
    {
        // Whatever the switch: a crate placed with it on keeps its slots when it is turned off.
        BigCrates.KeepSavedSlots(api.Logger);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        var harmony = new Harmony(PileHandbook.HarmonyId);
        if (PileHandbook.Patch(harmony, api))
            _handbookHarmony = harmony;
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        if (api.Side == EnumAppSide.Server)
        {
            // Before the piles: the rusty gear's pile is 64 of its new stack.
            if (config.RustyGearStack)
                api.Logger.Notification(RustyGearStack.Apply(api)
                    ? "[seraphhorizons] Rusty gear stack: the rusty gear stacks to {0}"
                    : "[seraphhorizons] Rusty gear stack: the game has no rusty gear; nothing to do", RustyGearStack.Stack);
            else
                api.Logger.Notification("[seraphhorizons] Rusty gear stack: switched off; the rusty gear stacks as the game ships it");
            if (config.BigCrates)
            {
                CratesMadeBig = BigCrates.Apply(api);
                api.Logger.Notification("[seraphhorizons] Big crates: {0} crate blocks hold more", CratesMadeBig);
            }
            else
                api.Logger.Notification("[seraphhorizons] Big crates: switched off; crates hold what their mods give them");
        }
        // Before the piles, so no clutter pile is made big.
        if (ServerSwitch(api, nameof(SeraphHorizonsConfig.NoClutterPiles), config.NoClutterPiles))
        {
            ClutterPilesTaken = ClutterPiles.Apply(api);
            api.Logger.Notification("[seraphhorizons] No clutter piles: {0} clutter, chiseled and other blocks not drawn as a cube lost More Piles' cube pile on this side", ClutterPilesTaken);
        }
        else
            api.Logger.Notification("[seraphhorizons] No clutter piles: switched off; clutter piles as More Piles gives it");
        if (!ServerSwitch(api, nameof(SeraphHorizonsConfig.BigPiles), config.BigPiles))
        {
            api.Logger.Notification("[seraphhorizons] Big piles: switched off; piles hold what their mods give them");
            return;
        }
        PilesMadeBig = BigPiles.Apply(api);
        api.Logger.Notification("[seraphhorizons] Big piles: {0} stacking piles hold {1} stacks a block on this side",
            PilesMadeBig, BigPileRules.StacksPerPile);
    }

    /// <summary>A switch as the server has it: the server's own setting; on a client, the server's
    /// list of switches that are off in the world config (<see cref="ValueHandbook.OffKey"/>, written
    /// in the server's <c>Start</c>), or the client's own setting when the world has none.</summary>
    public static bool ServerSwitch(ICoreAPI api, string name, bool own)
    {
        if (api.Side == EnumAppSide.Server) return own;
        string? off = api.World?.Config?.GetString(ValueHandbook.OffKey);
        return off == null ? own : !SwitchOwnership.DecodeOff(off).Contains(name);
    }

    public override void Dispose()
    {
        if (_handbookHarmony != null)
        {
            _handbookHarmony.UnpatchAll(PileHandbook.HarmonyId);
            _handbookHarmony = null;
            PileHandbook.Unbind();
        }
    }
}
