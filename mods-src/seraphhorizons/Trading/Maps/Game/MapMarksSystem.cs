using HarmonyLib;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Maps;

/// <summary>
/// The waypoints ore maps, gravel maps and leads put on a player's map, and the trader camps
/// marked when their trader is met (server side; the rule is <see cref="MapMarks"/>). Every marker
/// the mod makes carries its precision in its title ("Copper deposit (precision 2, ±150 m)",
/// "Trader camp (cook) (approximate, ±64 m)", "… (exact)") and is remembered by its guid with its
/// target and precision (<see cref="Book"/>, saved under <see cref="SaveKey"/>), so a map whose
/// target is marked already is refused at the trader, a better map replaces a rougher marker, and
/// meeting a camp's trader replaces the lead's marker with an exact one. Bookkeeping only: it is on
/// whatever the switches say, as ore maps are read without the trading features.
/// </summary>
public class MapMarksSystem : ModSystem
{
    public const string SaveKey = "seraphhorizons:mapmarks";

    /// <summary>The colour of trader camp markers (leads and meetings).</summary>
    public static readonly int TraderColor = ColorUtil.ColorFromRgba(90, 160, 220, 255);

    private static readonly System.Reflection.MethodInfo? Resend = AccessTools.Method(typeof(WaypointMapLayer), "ResendWaypoints");

    private ICoreServerAPI? _sapi;

    public MarkBook Book { get; private set; } = new();

    public static MapMarksSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<MapMarksSystem>();

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        api.Event.SaveGameLoaded += () =>
        {
            try
            {
                var json = api.WorldManager.SaveGame.GetData<string>(SaveKey);
                Book = json is null ? new MarkBook() : MarkBook.FromJson(json);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[seraphhorizons] Map markers: the saved markers do not load ({0}); starting afresh", e.Message);
                Book = new MarkBook();
            }
        };
        api.Event.GameWorldSave += () => api.WorldManager.SaveGame.StoreData(SaveKey, Book.ToJson());
    }

    private WaypointMapLayer? Layer =>
        _sapi!.World.Config.GetBool("allowMap", true)
            ? _sapi.ModLoader.GetModSystem<WorldMapManager>()?.MapLayers.OfType<WaypointMapLayer>().FirstOrDefault()
            : null;

    private static List<WaypointView> Views(WaypointMapLayer layer, string uid) =>
        layer.Waypoints.Where(w => w.OwningPlayerUid == uid).Select(w => new WaypointView(w.Guid, w.Position.X, w.Position.Z, w.Icon, w.Title)).ToList();

    /// <summary>The best precision the player's map holds of the target (0: none, or no map).</summary>
    public int MarkedPrecision(IPlayer player, string key)
    {
        if (Layer is not { } layer) return 0;
        var views = Views(layer, player.PlayerUID);
        Book.Prune(player.PlayerUID, views.Select(v => v.Guid));
        return MapMarks.MarkedPrecision(key, Book.Of(player.PlayerUID), views);
    }

    /// <summary>The targets the player's map holds a remembered marker of, at any precision.</summary>
    public HashSet<string> MarkedKeys(IPlayer player)
    {
        if (Layer is not { } layer) return [];
        var views = Views(layer, player.PlayerUID);
        Book.Prune(player.PlayerUID, views.Select(v => v.Guid));
        var live = views.Select(v => v.Guid).Where(g => g != null).ToHashSet();
        return Book.Of(player.PlayerUID).Where(r => live.Contains(r.Guid)).Select(r => r.Key).ToHashSet();
    }

    public enum Outcome { Added, Already, NoMap }

    /// <summary>
    /// Puts <paramref name="target"/> on the player's map at <paramref name="pos"/>, unless a marker
    /// of it as precise or better is there (<see cref="Outcome.Already"/>, as is an unremembered marker
    /// with the same icon on the very spot, which is adopted). Rougher markers of it go, and so do the
    /// old unremembered ones <paramref name="legacy"/> picks out.
    /// </summary>
    public Outcome Mark(IServerPlayer player, MarkTarget target, Vec3d pos, string title, string icon, int color,
        System.Func<List<WaypointView>, IEnumerable<WaypointView>>? legacy = null)
    {
        if (Layer is not { } layer) return Outcome.NoMap;
        string uid = player.PlayerUID;
        var views = Views(layer, uid);
        Book.Prune(uid, views.Select(v => v.Guid));
        var records = Book.Of(uid);
        if (MapMarks.Replaces(target, records, views) is not { } replaced) return Outcome.Already;
        var known = records.Select(r => r.Guid).ToHashSet();
        if (layer.Waypoints.FirstOrDefault(w => w.OwningPlayerUid == uid && w.Icon == icon && w.Position.X == pos.X && w.Position.Z == pos.Z
                                                && (w.Guid is null || !known.Contains(w.Guid))) is { } same)
        {
            same.Guid ??= Guid.NewGuid().ToString();
            Book.Add(uid, new MarkRecord { Guid = same.Guid, Key = target.Key, Precision = target.Precision });
            return Outcome.Already;
        }
        var gone = replaced.ToHashSet();
        if (legacy != null)
            foreach (var w in legacy(views))
                if (w.Guid != null) gone.Add(w.Guid);
                else layer.Waypoints.RemoveAll(p => p.OwningPlayerUid == uid && p.Guid is null && p.Position.X == w.X && p.Position.Z == w.Z && p.Title == w.Title);
        if (gone.Count > 0)
        {
            layer.Waypoints.RemoveAll(w => w.OwningPlayerUid == uid && w.Guid != null && gone.Contains(w.Guid));
            Book.Remove(uid, gone);
        }
        string guid = Guid.NewGuid().ToString();
        layer.AddWaypoint(new Waypoint
        {
            Color = color,
            Icon = icon,
            Pinned = true,
            Position = pos,
            OwningPlayerUid = uid,
            Title = title,
            Guid = guid,
        }, player);
        Book.Add(uid, new MarkRecord { Guid = guid, Key = target.Key, Precision = target.Precision });
        return Outcome.Added;
    }

    /// <summary>Takes a marker off a player's map and tells their client.</summary>
    public void Unmark(IServerPlayer player, string guid)
    {
        if (Layer is not { } layer) return;
        if (layer.Waypoints.RemoveAll(w => w.OwningPlayerUid == player.PlayerUID && w.Guid == guid) == 0) return;
        Book.Remove(player.PlayerUID, [guid]);
        Resend?.Invoke(layer, [player]);
    }

    // ---- Titles ----

    /// <summary>A marker's title with its precision: "… (exact)", "… (precision 2, ±150 m)", or for a
    /// lead "… (approximate, ±64 m)".</summary>
    public static string Title(string lang, string name, int precision, bool lead = false) =>
        lead ? Lang.GetL(lang, "seraphhorizons:map-waypoint-lead", name, MapMarks.LeadReach)
        : precision >= MapMarks.Exact ? Lang.GetL(lang, "seraphhorizons:map-waypoint-exact", name)
        : Lang.GetL(lang, "seraphhorizons:map-waypoint-precision", name, precision, MapMarks.ReachOf(precision));

    // ---- What a stack points at ----

    /// <summary>The target of a map, a lead or an offer of either (by its attributes), or null.</summary>
    public static MarkTarget? TargetOf(ItemStack? stack)
    {
        if (stack?.Collectible is null) return null;
        var a = stack.Attributes;
        if (stack.Collectible is ItemOreMap)
        {
            if (a.GetString(ItemOreMap.AttrDeposit) is not { Length: > 0 } id) return null;
            bool gravel = stack.Collectible.Code == MapIssuer.GravelMapCode || a.GetString(ItemOreMap.AttrMetal) == PlacerCells.Kind;
            return new MarkTarget(MapMarks.DepositKey(id), gravel ? MapMarks.Exact : a.GetAsInt(ItemOreMap.AttrPrecision, MapMarks.Exact));
        }
        if (stack.Collectible is ItemTraderLead)
        {
            if (!LeadTargets.TryParse(a.GetString(MapOfferAttrs.LeadKind), out var kind)) return null;
            if (kind == LeadKind.Settlement)
                return a.HasAttribute(MapOfferAttrs.X)
                    ? new MarkTarget(MapMarks.SettlementKey(a.GetAsInt(MapOfferAttrs.X), a.GetAsInt(MapOfferAttrs.Z)), MapMarks.LeadPrecision)
                    : null;
            return CellKey.TryParse(a.GetString(MapOfferAttrs.Cell) ?? "", out var cell)
                ? new MarkTarget(TraderIds.Camp(cell.X, cell.Z), MapMarks.LeadPrecision)
                : null;
        }
        return null;
    }

    /// <summary>The targets of every map and lead the player carries (pending ones included).</summary>
    public static List<MarkTarget> Held(IPlayer player) =>
        player.InventoryManager.Inventories.Values
            .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName
                              or GlobalConstants.mousecursorInvClassName or GlobalConstants.characterInvClassName)
            .SelectMany(inv => inv)
            .Select(s => TargetOf(s.Itemstack))
            .OfType<MarkTarget>()
            .ToList();

    /// <summary>Whether the player has the offer already: marked as precisely, or a copy carried.</summary>
    public MarkCheck Check(IPlayer player, ItemStack offer) =>
        TargetOf(offer) is { } target ? MapMarks.Check(target, MarkedPrecision(player, target.Key), Held(player)) : MarkCheck.Free;
}
