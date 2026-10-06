using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.GearReclamation.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.GearReclamation;

/// <summary>
/// Gear reclamation (<c>GearReclamation</c>, #474, #475, #477): the reclaimed gears, the degreasing
/// pot, the neutralizing and oiling barrels, and the oiled gear's roll. The items exist whatever
/// the switch (other mods' recipes and the other steps name them); with it off their recipes are
/// left out, the oiled gear is never rolled, the rusty gear's handbook section is not added and
/// the guide page is hidden. Settings in <c>GearReclamationSettings</c>.
/// </summary>
public class GearReclamationSystem : ModSystem
{
    public const string GearClass = "seraphhorizons.ReclaimedGear";
    public const string GuidePageCode = "seraphhorizons-gearreclamation";
    public const string GuideTitleKey = "seraphhorizons:gearreclamation-title";
    public static readonly AssetLocation PatchAsset = new(GearCodes.Domain, "patches/gearreclamation-rustygear.json");
    public static readonly AssetLocation CookingAsset = new(GearCodes.Domain, "recipes/cooking/gear-degrease.json");
    public static readonly AssetLocation[] BarrelAssets =
    [
        new(GearCodes.Domain, "recipes/barrel/gear-neutralize.json"),
        new(GearCodes.Domain, "recipes/barrel/gear-oil.json"),
    ];

    /// <summary>How often the server looks through every online player's inventories for an oiled
    /// gear the slot hook missed.</summary>
    public const int SweepIntervalMs = 2000;

    private ICoreAPI? _api;
    private ICoreServerAPI? _sapi;
    private GearReclamationConfig? _config;
    private ModSystemSurvivalHandbook? _handbook;
    private InitCustomPagesDelegate? _hidePage;
    private readonly HashSet<ItemSlot> _pending = [];

    public static GearReclamationSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<GearReclamationSystem>();

    /// <summary>The switch on this side; the server's decides everything that happens.</summary>
    public bool On => _api != null && SeraphHorizonsSystem.ConfigFor(_api).GearReclamation;

    /// <summary>This side's settings, sanitised.</summary>
    public GearReclamationConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The stacks resolved so far on this server (the tests read it).</summary>
    public int Resolved { get; private set; }

    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterItemClass(GearClass, typeof(ItemReclaimedGear));
        // Before the game's patch loader, which applies the patches in AssetsLoaded. On the server
        // only: a client has no assets in Start (the game throws on reading one).
        if (!On && api.Side == EnumAppSide.Server && api.Assets.TryGet(PatchAsset) is { } patch)
            patch.Data = "[]"u8.ToArray();
    }

    // Recipes are read from the assets later in this phase (cooking at 0.6, barrels at 1), this
    // system at the default 0.1, on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
            return;
        bool DomainLoaded(string domain) => api.Assets.GetLocations("itemtypes/", domain).Count > 0;
        FilterCooking(api, On, DomainLoaded);
        foreach (var location in BarrelAssets)
            FilterBarrel(api, location, On, DomainLoaded);
    }

    // The items exist now; the server sends them to clients after this.
    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
            return;
        var hours = FlashRustHours.For(Config.FlashRustHours);
        int count = 0;
        foreach (var item in api.World.Items)
        {
            if (item?.Code == null || item.Attributes?[GearCodes.FlashRustAttribute].AsBool() != true)
                continue;
            foreach (var t in item.TransitionableProps ?? [])
            {
                if (t.Type != EnumTransitionType.Perish)
                    continue;
                t.FreshHours = NatFloat.createUniform((float)hours.Fresh, 0);
                t.TransitionHours = NatFloat.createUniform((float)hours.Transition, 0);
                count++;
            }
        }
        api.Logger.Notification("[seraphhorizons] Gear reclamation: {0} bare gear types flash-rust after {1} hours", count, hours.Fresh);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        if (!On)
        {
            // The recipe export leaves the page out too.
            var hidden = api.ObjectCache.TryGetValue(WoodworkingGuide.HiddenGuidesKey, out var listed)
                         && listed is IEnumerable<(string, string)> pages
                ? pages.ToList()
                : [];
            hidden.Add((GuidePageCode, GuideTitleKey));
            api.ObjectCache[WoodworkingGuide.HiddenGuidesKey] = hidden;
            return;
        }
        api.Event.PlayerNowPlaying += player => ResolveAll(player);
        api.Event.RegisterGameTickListener(_ => Sweep(), SweepIntervalMs);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (On || api.ModLoader.GetModSystem<ModSystemSurvivalHandbook>() is not { } handbook)
            return;
        _handbook = handbook;
        _hidePage = pages => pages.RemoveAll(p => p.PageCode == GuidePageCode);
        handbook.OnInitCustomPages += _hidePage;
    }

    public override void Dispose()
    {
        if (_handbook != null && _hidePage != null)
            _handbook.OnInitCustomPages -= _hidePage;
        _handbook = null;
        _hidePage = null;
        _pending.Clear();
    }

    /// <summary>Called by the oiled gear when its slot changes: a slot of a player's own inventory
    /// on the server is resolved on the next tick, after the move that put it there is done.</summary>
    internal void OnOiledGearInSlot(ItemSlot slot)
    {
        if (_sapi == null || !On || slot.Inventory is not InventoryBasePlayer inventory || !Rolls(inventory))
            return;
        if (_pending.Add(slot))
            _sapi.Event.RegisterCallback(_ =>
            {
                _pending.Remove(slot);
                if (slot.Inventory is InventoryBasePlayer { Player: { } player })
                    Resolve(slot, player);
            }, 1);
    }

    /// <summary>Whether oiled gears in <paramref name="inventory"/> are rolled: every player
    /// inventory but the creative one, whose stacks are the catalogue.</summary>
    private static bool Rolls(IInventory inventory) => inventory.ClassName != GlobalConstants.creativeInvClassName;

    /// <summary>Resolves every oiled gear in <paramref name="player"/>'s inventories.</summary>
    public int ResolveAll(IPlayer player)
    {
        if (!On || player.InventoryManager?.Inventories == null)
            return 0;
        int resolved = 0;
        foreach (var inventory in player.InventoryManager.Inventories.Values.ToList())
        {
            if (inventory is not InventoryBasePlayer || !Rolls(inventory))
                continue;
            foreach (var slot in inventory)
                if (Resolve(slot, player))
                    resolved++;
        }
        return resolved;
    }

    private void Sweep()
    {
        if (_sapi == null)
            return;
        foreach (var player in _sapi.World.AllOnlinePlayers)
            if (player is IServerPlayer { ConnectionState: EnumClientState.Playing })
                ResolveAll(player);
    }

    /// <summary>Turns the oiled gears in <paramref name="slot"/> into steel gears and steel bits:
    /// the sound gears stay in the slot (or, if none is, the bits), the rest go to
    /// <paramref name="player"/>'s inventory or, if it is full, drop at their feet. Returns whether
    /// there was anything to resolve.</summary>
    public bool Resolve(ItemSlot slot, IPlayer player)
    {
        var world = _api?.World;
        if (world == null || world.Side != EnumAppSide.Server || slot.Itemstack is not { } stack
            || stack.Collectible?.Code?.ToString() != GearCodes.Oiled)
            return false;
        var steelItem = world.GetItem(new AssetLocation(GearCodes.Steel));
        var bitItem = world.GetItem(new AssetLocation(GearCodes.SteelBit));
        if (steelItem == null || bitItem == null)
        {
            world.Logger.Error("[seraphhorizons] Gear reclamation: {0} or {1} is missing; oiled gears stay as they are",
                GearCodes.Steel, GearCodes.SteelBit);
            return false;
        }
        var result = GearLottery.Roll(stack.StackSize, Config.UsableGearChance, Config.BitsPerFailedGear, world.Rand.NextDouble);
        var stacks = new List<ItemStack>();
        stacks.AddRange(GearLottery.Stacks(result.Steel, steelItem.MaxStackSize).Select(n => new ItemStack(steelItem, n)));
        stacks.AddRange(GearLottery.Stacks(result.Bits, bitItem.MaxStackSize).Select(n => new ItemStack(bitItem, n)));
        slot.Itemstack = stacks.Count > 0 ? stacks[0] : null;
        slot.MarkDirty();
        foreach (var rest in stacks.Skip(1))
        {
            player.InventoryManager.TryGiveItemstack(rest, true);
            if (rest.StackSize > 0 && player.Entity != null)
                world.SpawnItemEntity(rest, player.Entity.Pos.XYZ);
        }
        Resolved++;
        world.Logger.Debug("[seraphhorizons] Gear reclamation: {0} oiled gears for {1}: {2} sound, {3} steel bits",
            result.Gears, player.PlayerName, result.Steel, result.Bits);
        return true;
    }

    /// <summary>Leaves the degreasing recipes out with the switch off; keeps only the alkalis whose
    /// mod is loaded, and leaves a recipe out when none is.</summary>
    private static void FilterCooking(ICoreAPI api, bool on, System.Func<string, bool> domainLoaded)
    {
        if (api.Assets.TryGet(CookingAsset) is not { } asset)
            return;
        var recipes = JArray.Parse(asset.ToText());
        foreach (var recipe in recipes.OfType<JObject>())
        {
            if (!on)
            {
                recipe["enabled"] = false;
                continue;
            }
            foreach (var ingredient in (recipe["ingredients"] as JArray ?? []).OfType<JObject>())
            {
                if (ingredient["validStacks"] is not JArray stacks)
                    continue;
                var keep = OptionalIngredients.Keep(stacks.Select(s => (string?)s["code"] ?? ""), domainLoaded);
                foreach (var s in stacks.OfType<JObject>().ToList())
                    if (!keep.Contains((string?)s["code"] ?? ""))
                        s.Remove();
                if (stacks.Count == 0)
                {
                    recipe["enabled"] = false;
                    api.Logger.Warning("[seraphhorizons] Gear reclamation: no mod with an alkali for {0} is loaded, so it is left out",
                        (string?)recipe["code"]);
                }
            }
        }
        asset.Data = Encoding.UTF8.GetBytes(recipes.ToString());
    }

    /// <summary>Leaves a barrel recipe out with the switch off or when one of its ingredients'
    /// mods is not loaded.</summary>
    private static void FilterBarrel(ICoreAPI api, AssetLocation location, bool on, System.Func<string, bool> domainLoaded)
    {
        if (api.Assets.TryGet(location) is not { } asset)
            return;
        var recipes = JArray.Parse(asset.ToText());
        foreach (var recipe in recipes.OfType<JObject>())
        {
            var codes = (recipe["ingredients"] as JArray ?? []).Select(i => (string?)i["code"] ?? "").ToList();
            if (on && codes.All(c => OptionalIngredients.Available(c, domainLoaded)))
                continue;
            recipe["enabled"] = false;
            if (on)
                api.Logger.Notification("[seraphhorizons] Gear reclamation: {0} needs {1}, which no loaded mod has; left out",
                    (string?)recipe["code"], string.Join(", ", codes.Where(c => !OptionalIngredients.Available(c, domainLoaded))));
        }
        asset.Data = Encoding.UTF8.GetBytes(recipes.ToString());
    }

    private static GearReclamationConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).GearReclamationSettings ?? new GearReclamationConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Gear reclamation: ModConfig/{SeraphHorizonsSystem.ConfigFile}, GearReclamationSettings: {fix}");
        return config;
    }
}

/// <summary>The reclaimed gears' item class. Only the oiled gear does anything: on the server, when
/// its slot in a player's inventory changes, it is resolved (<see cref="GearReclamationSystem.Resolve"/>).</summary>
public class ItemReclaimedGear : Item
{
    private bool Oiled => Code?.ToString() == GearCodes.Oiled;

    public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack? extractedStack = null)
    {
        base.OnModifiedInInventorySlot(world, slot, extractedStack);
        if (world.Side == EnumAppSide.Server && Oiled && slot.Itemstack?.Collectible == this)
            GearReclamationSystem.Of(world.Api).OnOiledGearInSlot(slot);
    }
}
