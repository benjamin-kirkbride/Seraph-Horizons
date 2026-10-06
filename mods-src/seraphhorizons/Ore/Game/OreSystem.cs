using System.Text;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Ore cells and the rest of the ore worldgen changes (epic #435; README "Ore cells"), each with
/// its switch in <see cref="SeraphHorizonsConfig"/>:
/// <list type="bullet">
/// <item><c>OreCells</c> (#438): one deposit per metal per cell, at a spot from the seed
/// (<see cref="OreCellPlacement"/>).</item>
/// <item><c>NoSurfaceCopper</c> (#440): <c>patches/ore-nosurfacecopper.json</c>.</item>
/// <item><c>SmallerDeposits</c> (#439): <see cref="DepositSizes"/>.</item>
/// <item><c>RarerDistricts</c> (#441): <c>patches/ore-rarerdistricts.json</c>.</item>
/// <item><c>PlacerFields</c> (#442): <see cref="PlacerFields"/>.</item>
/// </list>
/// With ore cells or placer fields on, it also keeps the deposit registry (#443,
/// <see cref="Deposits"/>) and makes maps (#444, <see cref="Maps"/>), for the traders (#455).
///
/// All of them change how chunks generate, so they are decided per world, once: a world created
/// with a switch on keeps it (unless the config switches it off later), and one created with it
/// off, or before this existed, never gets it (<see cref="OreWorldRecord"/>, saved under
/// <see cref="WorldRecordKey"/>). The savegame is open before mods start, so this is decided in
/// <see cref="Start"/>, early enough to empty the JSON patches the world doesn't get before the
/// game applies patches. Server only: worldgen happens there.
/// </summary>
public class OreSystem : ModSystem
{
    public const string WorldRecordKey = "seraphhorizons:oreworld";
    public const string CellBookKey = "seraphhorizons:orecells";
    public const string HarmonyId = "seraphhorizons.ore";

    public static readonly AssetLocation NoSurfaceCopperPatch = new("seraphhorizons", "patches/ore-nosurfacecopper.json");
    public static readonly AssetLocation RarerDistrictsPatch = new("seraphhorizons", "patches/ore-rarerdistricts.json");

    private Harmony? _harmony;
    private OreCommands? _commands;
    private DepositCommands? _depositCommands;

    /// <summary>What is in force in this world this run.</summary>
    public OreWorldRecord World { get; private set; } = OreWorldRecord.AllOff;

    /// <summary>Whether the world was created this run.</summary>
    public bool NewWorld { get; private set; }

    /// <summary>The ore cell rule, if it is on and bound.</summary>
    public OreCellPlacement? Placement { get; private set; }

    /// <summary>The placer field rule, if it is on and bound.</summary>
    public PlacerFields? Placer { get; private set; }

    /// <summary>The deposit registry's service: listing, verifying and reserving deposits and gravel
    /// fields (docs/oregen.md). Null when neither ore cells nor placer fields are bound.</summary>
    public DepositService? Deposits { get; private set; }

    /// <summary>Makes ore and gravel maps; null with <see cref="Deposits"/>.</summary>
    public MapIssuer? Maps { get; private set; }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void Start(ICoreAPI api)
    {
        var sapi = (ICoreServerAPI)api;
        var config = SeraphHorizonsSystem.ConfigFor(api);
        var wanted = new OreWorldRecord(config.OreCells, config.OreCellSizeMetres,
            new Dictionary<string, int>(config.OreCellSizeByMetal ?? new Dictionary<string, int>()),
            config.NoSurfaceCopper, config.SmallerDeposits, config.RarerDistricts,
            config.PlacerFields, OreCells.ClampCellSize(config.PlacerCellSizeMetres));
        var saveGame = sapi.WorldManager.SaveGame;
        NewWorld = saveGame.IsNew;
        OreWorldRecord? saved = null;
        bool unreadable = false;
        try
        {
            if (saveGame.GetData(WorldRecordKey) is { } bytes)
                saved = OreWorldRecord.Parse(Encoding.UTF8.GetString(bytes));
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Ore cells: could not read this world's ore settings, leaving ore worldgen as the mods ship it: {0}", e.Message);
            unreadable = true;
        }
        var record = unreadable ? OreWorldRecord.AllOff : OreWorldRecord.ForWorld(saved, saveGame.IsNew, wanted);
        if (saved == null && !unreadable)
            saveGame.StoreData(WorldRecordKey, Encoding.UTF8.GetBytes(record.Serialize()));
        World = record.Effective(wanted);
        api.Logger.Notification(
            "[seraphhorizons] Ore worldgen for this world ({0}): ore cells {1}, no surface copper {2}, smaller deposits {3}, rarer districts {4}",
            saved != null ? "settings kept from its creation" : saveGame.IsNew ? "new world, settings from the config" : "created before ore cells, all off",
            World.OreCells ? $"on, {World.CellSize} m" : "off", OnOff(World.NoSurfaceCopper), OnOff(World.SmallerDeposits), OnOff(World.RarerDistricts));

        if (!World.NoSurfaceCopper) EmptyPatch(api, NoSurfaceCopperPatch);
        if (!World.RarerDistricts) EmptyPatch(api, RarerDistrictsPatch);
        api.Logger.Notification("[seraphhorizons] Placer fields for this world: {0}",
            World.PlacerFields ? $"on, {World.PlacerCellSize} m" : "off");
        if (World.PlacerFields)
        {
            if (PlacerFields.Unsupported(api) is { } why)
                api.Logger.Warning("[seraphhorizons] Placer fields are off: {0}", why);
            else
                PlacerFields.BindScatteredCut(_harmony ??= new Harmony(HarmonyId));
        }
        // The cell rule binds on the server side, but its tries must be raised before the deposit
        // generators are built (AssetsFinalize).
        if (World.OreCells && OreCellPlacement.Unsupported(api) is null)
            OreCellPlacement.BindTries(_harmony ??= new Harmony(HarmonyId));
        if (World.SmallerDeposits)
        {
            if (DepositSizes.Unsupported(api) is { } why)
                api.Logger.Warning("[seraphhorizons] Smaller deposits is off: {0}", why);
            else
                DepositSizes.Bind(api, _harmony ??= new Harmony(HarmonyId));
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (World.OreCells)
        {
            if (OreCellPlacement.Unsupported(api) is { } why)
                api.Logger.Warning("[seraphhorizons] Ore cells are off: {0}; Interesting Ore Gen places ore by its own rule", why);
            else
                Placement = OreCellPlacement.Bind(api, _harmony ??= new Harmony(HarmonyId), World);
        }
        if (World.PlacerFields && PlacerFields.Unsupported(api) is null)
            Placer = PlacerFields.Bind(api, World);
        if (Placement != null || Placer != null)
        {
            Deposits = new DepositService(api, Placement, Placer);
            Maps = new MapIssuer(api, Deposits);
        }
        _commands = new OreCommands(api, this);
        _commands.Register();
        _depositCommands = new DepositCommands(api, this);
        _depositCommands.Register();
    }

    // The pan's table, on the server after the patch loader and before the blocks are read
    // (as PanningDrops trims it); clients get the block's attributes from the server.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (!World.PlacerFields || PlacerFields.Unsupported(api) is not null) return;
        try
        {
            var changed = PlacerFields.AddPanCopper(api);
            api.Logger.Notification("[seraphhorizons] Placer fields: native copper at {0:0.#}% added to the pan's {1}",
                PlacerFields.AddedCopperChance * 100, changed.Count == 0 ? "rich gravel (none needed it)" : string.Join(", ", changed));
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Placer fields: could not add copper to the pan's rich gravel: {0}", e.Message);
        }
    }

    public override void Dispose()
    {
        Placement?.Unbind();
        Placement = null;
        Placer?.Unbind();
        Placer = null;
        Deposits?.Dispose();
        Deposits = null;
        Maps = null;
        DepositSizes.Unbind();
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }

    private static string OnOff(bool on) => on ? "on" : "off";

    /// <summary>Empties a patch file before the game's patch loader reads it (in AssetsLoaded).</summary>
    private static void EmptyPatch(ICoreAPI api, AssetLocation location)
    {
        if (api.Assets.TryGet(location) is { } asset)
            asset.Data = "[]"u8.ToArray();
    }
}
