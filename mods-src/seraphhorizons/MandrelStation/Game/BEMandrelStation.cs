using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.MandrelStation.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.MandrelStation;

/// <summary>
/// The mandrel station's controller, on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/mandrelstation.json</c>, the mandrel, its cell, its block info, its renderer). It
/// keeps the hollow section on the mandrel as its item stack and the forging (<see cref="MachineJob"/>: W,
/// 0..1, its steps the blows struck). The player forges as on the anvil: each right-click with a hammer is
/// a blow (the anvil's sound and sparks at <c>strike.pos</c>, the hammer's durability paid; a hammer of a
/// higher tool tier forges more a blow), and at the blow that finishes the hollow two pipe sections of the
/// hollow's metal drop beyond the tip. With right-click held after a hollow is finished, a blow on the bare
/// mandrel puts another of the same on from the player's hotbar. The mandrel, once fitted, comes out only
/// by breaking.
/// <para>Its second job winds a spring (<see cref="Winding"/>), and that is the station's hand work
/// (<see cref="BEMachine.HandWorked"/>): a hot steel rod goes on a fitted, bare mandrel, and the player
/// winds it by holding right-click with tongs, as on the quern; it winds only while the rod is at working
/// temperature (the anvil's rule), its parts glowing by it, and the spring comes off at the rod's
/// temperature. Ctrl + right-click takes the rod back, straight, at any time (to reheat it).</para>
/// The rules are MandrelStation/Core's.
/// </summary>
public class BEMandrelStation : BEMachine
{
    private static readonly AssetLocation HollowSound = new("game", "sounds/block/chute");
    private static readonly AssetLocation BlowSound = new("game", "sounds/effect/anvilhit");
    private static readonly AssetLocation SectionSound = new("game", "sounds/block/chute");
    private static readonly AssetLocation RodSound = new("game", "sounds/block/ingot");
    // The server syncs the winding's W at least this often; the renderer follows the held clock between.
    private const double WindSyncStep = 0.02;
    private static readonly Float3 StrikeFallback = new(0.5f, 0.75f, 1f);

    private ItemStack? _hollow;
    private MachineJob _job = MachineJob.None;
    private ItemStack? _rod;
    private MachineJob _wind = MachineJob.None;
    private string? _lastWinder;
    private bool _toldCold;
    private double? _serverTurnsPerRod;
    private long _lastBlowAt = long.MinValue / 2;
    private string? _lastFinished;
    private string? _lastClickBy;
    private long _lastClickAt = long.MinValue / 2;
    private int? _serverBlowsNeeded;
    private readonly MandrelStationClock _clock = new();

    /// <summary>The fitted mandrel's code, or null.</summary>
    public string? Mandrel => Assembly is { Built: true } a ? a.Build.CodeIn(0, 0) : null;
    /// <summary>A hollow is on the mandrel.</summary>
    public bool HollowOn => _job.On && _hollow != null;
    public ItemStack? Hollow => _hollow;
    public MachineJob Job => _job;
    /// <summary>A steel rod is on the mandrel, being wound into a spring.</summary>
    public bool RodOn => _wind.On && _rod != null;
    public ItemStack? Rod => _rod;
    public MachineJob Wind => _wind;
    /// <summary>Work is on the mandrel: a hollow or a rod.</summary>
    public bool WorkOn => HollowOn || RodOn;
    /// <summary>The hollow on: 1 lead, 2 copper, 0 none (a rod is not a hollow).</summary>
    public int HollowClass => HollowOn ? _job.Class : 0;

    /// <summary>Blows the hollow on takes with the base hammer (the copper one), as the server runs (0
    /// with none); a better hammer takes fewer.</summary>
    public int BlowsNeeded => _job.On ? _serverBlowsNeeded ?? Config.BlowsPerHollow(_job.Class) : 0;

    /// <summary>The fitted mandrel's metal (<c>iron</c>, <c>meteoriciron</c>, <c>steel</c>), or null.</summary>
    public string? MandrelMetal => MandrelPart.MetalOf(Mandrel);
    /// <summary>The job on the mandrel: 1 a lead hollow, 2 a copper one, 3 a rod being wound, 0 none.</summary>
    public int JobClass => HollowOn ? _job.Class : RodOn ? Winding.SpringClass : 0;
    /// <summary>The server's W: the forging of the hollow on, or the winding of the rod, 0..1.</summary>
    public double JobWork => RodOn ? _wind.Work : _job.Work;
    /// <summary>The blows struck on the hollow on so far.</summary>
    public int Blows => _job.Steps;
    public double TurnsPerRod => _serverTurnsPerRod ?? Config.TurnsPerRod;
    public float RodTemperature => _rod is { } rod && Api != null ? rod.Collectible.GetTemperature(Api.World, rod) : 0;

    private MandrelStationSystem System => MandrelStationSystem.Of(Api);
    private MandrelStationConfig Config => System.Config;
    private new MandrelStationRig? Rig => base.Rig as MandrelStationRig;

    // The winding is the station's hand work; a hammer's blows are clicks of their own.
    protected override bool HandWorked => true;
    protected override int TickMs => 50;
    public override int RenderRange => 48;

    /// <summary>Work on the mandrel, a hollow to strike or a rod to wind; only the rod is worked by holding.</summary>
    public override bool HasWork => WorkOn;
    public override double? WorkProgress => RodOn ? _wind.Progress : HollowOn ? _job.Progress : null;

    // ---- Interaction ----

    /// <summary>
    /// The station's own right-click, before its build's. Ctrl (Shift aside) takes back, but for the
    /// creative shortcut: a rod at any time, else a hollow not yet struck (the mandrel is a part, not a
    /// consumable: it comes out only by breaking the station). With a rod on, any other click (Shift aside)
    /// starts winding it, held as on the quern, with tongs in either hand (<see cref="OnInteractStep"/>).
    /// On a fitted station any mandrel rod is the rod's business (<see cref="OfferRod"/>): a hot steel rod on
    /// a bare mandrel to be wound, else refused with a message.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        bool server = Api.Side == EnumAppSide.Server;
        if (controls.CtrlKey && !controls.ShiftKey && !CreativeShortcut(byPlayer))
        {
            if (server)
            {
                if (RodOn)
                    TakeRod(byPlayer);
                else
                    TakeBack(byPlayer);
            }
            return true;
        }
        if (RodOn && !controls.ShiftKey)
            return StartWinding(byPlayer);
        if (Built && MandrelPart.IsMandrel(slot.Itemstack?.Collectible?.Code?.ToString()))
        {
            if (server)
                OfferRod(slot, byPlayer);
            return true;
        }
        return null;
    }

    /// <summary>The station's own right-click, after its build's: a lead or copper hollow section goes on a
    /// bare mandrel; a hammer strikes a blow. Anything else is the item's own business.</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        bool server = Api.Side == EnumAppSide.Server;
        if (Forging.ClassOfHollow(code) != 0 && !WorkOn)
            return !server || TryLoadHollow(slot, byPlayer);
        if (Forging.IsHammer(code) && !byPlayer.Entity.Controls.ShiftKey)
        {
            if (server)
                StrikeBy(byPlayer, slot);
            return true;
        }
        return false;
    }

    /// <summary>Ctrl + right-click (server side): a hollow not yet struck comes off. A hollow being
    /// forged stays. The mandrel never comes out this way: the station has no consumable part, and
    /// breaking it gives the mandrel back.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        if (!HollowOn)
            return false;
        if (!_job.Untouched)
            return Error(byPlayer, "error-busy");
        var hollow = _hollow!;
        ClearJob();
        Give(byPlayer, hollow);
        Api.World.PlaySoundAt(HollowSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>With right-click held after a hollow was finished (server side): puts another of the
    /// same item on from the first of the player's hotbar slots holding one
    /// (<see cref="Forging.RefillSlot"/>; not the backpack), as a right-click with it in hand would
    /// (<see cref="TryLoadHollow"/>). Returns whether one went on.</summary>
    public bool RefillFromHotbar(IPlayer byPlayer)
    {
        if (Api.Side != EnumAppSide.Server || WorkOn || !Built
            || byPlayer.InventoryManager?.GetHotbarInventory() is not { } hotbar)
            return false;
        var codes = Enumerable.Range(0, hotbar.Count).Select(i => hotbar[i]?.Itemstack?.Collectible?.Code?.ToString()).ToList();
        int at = Forging.RefillSlot(codes, _lastFinished);
        return at >= 0 && hotbar[at] is { } slot && TryLoadHollow(slot, byPlayer);
    }

    /// <summary>The pipe section a hollow of class <paramref name="k"/> is forged into, or null when
    /// it does not exist in this game (it is UnifiedPipes' item).</summary>
    private Item? SectionItem(int k) =>
        Forging.SectionFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a hollow from <paramref name="slot"/> on the mandrel, if the station takes it now.</summary>
    public bool TryLoadHollow(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Forging.CanLoad(code, Built, WorkOn))
        {
            case HollowLoadVerdict.Loads:
                break;
            case HollowLoadVerdict.NoMandrel:
                return Error(byPlayer, "error-no-mandrel");
            case HollowLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (SectionItem(Forging.ClassOfHollow(code)) == null)
            return Error(byPlayer, "error-no-section");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack hollow)
    {
        _hollow = hollow;
        _job = Forging.Start(Forging.ClassOfHollow(hollow.Collectible.Code.ToString()));
        var at = WorldPoint(Rig?.Strike ?? StrikeFallback);
        Api.World.PlaySoundAt(HollowSound, at.X, at.Y, at.Z);
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _hollow = null;
        _job = MachineJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    /// <summary>The station's own message (<c>seraphhorizons:mandrelstation-{key}</c>) to the player; false.</summary>
    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("mandrelstation-" + key, Lang.GetL(sp.LanguageCode, MandrelStationSystem.Domain + ":mandrelstation-" + key, args));
        return false;
    }

    /// <summary>The help once built: a hollow or a hot rod goes on a bare mandrel; a hammer strikes the hollow,
    /// and Ctrl takes it back while it is unstruck; tongs wind the rod, and Ctrl takes it back.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => MandrelStationSystem.Domain + ":blockhelp-mandrelstation-" + action;
        var world = Api.World;
        if (!WorkOn && Stacks(world, [Forging.LeadHollow, Forging.CopperHollow], 1) is { Length: > 0 } hollows)
            help.Add(new WorldInteraction { ActionLangCode = Key("loadhollow"), MouseButton = EnumMouseButton.Right, Itemstacks = hollows });
        if (!WorkOn && SpringItem() != null && Stacks(world, [Winding.SteelRod], 1) is { Length: > 0 } rods)
            help.Add(new WorldInteraction { ActionLangCode = Key("loadrod"), MouseButton = EnumMouseButton.Right, Itemstacks = rods });
        if (HollowOn && Stacks(world, ["game:hammer-*"], 1) is { Length: > 0 } hammers)
            help.Add(new WorldInteraction { ActionLangCode = Key("strike"), MouseButton = EnumMouseButton.Right, Itemstacks = hammers });
        if (HollowOn && _job.Untouched)
            help.Add(new WorldInteraction { ActionLangCode = Key("takehollow"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
        if (RodOn)
        {
            var tongs = world.Items.Where(i => i?.Code != null && !i.IsMissing && i.Tool == EnumTool.Tongs).Select(i => new ItemStack(i)).ToArray();
            help.Add(new WorldInteraction { ActionLangCode = Key("wind"), MouseButton = EnumMouseButton.Right, Itemstacks = tongs.Length > 0 ? tongs : null });
            help.Add(new WorldInteraction { ActionLangCode = Key("takerod"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
        }
    }

    // ---- Winding ----

    /// <summary>The spring a rod is wound into, or null when it does not exist in this game (the
    /// <c>MandrelSprings</c> switch off).</summary>
    public Item? SpringItem() =>
        Api.World.GetItem(new AssetLocation(Winding.Spring)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>A stack's temperature, and whether it is at working temperature (the anvil's rule:
    /// its <c>workableTemperature</c> attribute, else half its melting point).</summary>
    public (float Temperature, float Workable, bool Hot) Heat(ItemStack stack)
    {
        var obj = stack.Collectible;
        float t = obj.GetTemperature(Api.World, stack);
        float melt = obj.GetMeltingPoint(Api.World, null, new DummySlot(stack));
        float? attr = obj.Attributes?["workableTemperature"] is { Exists: true } w ? w.AsFloat(melt / 2) : null;
        float workable = Winding.WorkableTemperature(melt, attr);
        return (t, workable, melt > 0 && t >= workable);
    }

    /// <summary>A rod right-clicked on the fitted station (server side): put on to be wound when it is a hot
    /// steel rod on a bare mandrel (<see cref="Winding.ForRod"/>), else refused with a message. On a bare
    /// station a rod is the build's: it is fitted as the mandrel.</summary>
    public bool OfferRod(ItemSlot slot, IPlayer? byPlayer)
    {
        var stack = slot.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        var heat = stack != null && Winding.IsRod(code) ? Heat(stack) : default;
        switch (Winding.ForRod(code, Mandrel, WorkOn, SpringItem() != null, heat.Hot))
        {
            case RodVerdict.AlreadyFitted:
                return Error(byPlayer, "error-mandrel-fitted");
            case RodVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            case RodVerdict.TooCold:
                return Error(byPlayer, "error-rod-cold", (int)heat.Temperature, (int)heat.Workable);
            case RodVerdict.Winds:
                return TryLoadRod(slot, byPlayer);
            default:
                return false;
        }
    }

    /// <summary>Puts one steel rod from <paramref name="slot"/> on the mandrel to be wound (server side),
    /// its stack (and so its temperature) kept: no checks but that the station takes one now.</summary>
    public bool TryLoadRod(ItemSlot slot, IPlayer? byPlayer)
    {
        if (!Built || WorkOn || slot.Itemstack is not { } stack || !Winding.IsRod(stack.Collectible?.Code?.ToString()))
            return false;
        if (SpringItem() == null)
            return Error(byPlayer, "error-mandrel-fitted");
        _rod = slot.TakeOut(1);
        slot.MarkDirty();
        _wind = Winding.Start();
        _toldCold = false;
        var at = WorldPoint(Rig?.Strike ?? StrikeFallback);
        Api.World.PlaySoundAt(RodSound, at.X, at.Y, at.Z, null, true, 16, 0.6f);
        MarkDirty(true);
        return true;
    }

    /// <summary>Ctrl + right-click with a rod on (server side): the rod comes back straight, its stack and
    /// temperature as they are, whatever the winding so far (to reheat it, or give up).</summary>
    public bool TakeRod(IPlayer byPlayer)
    {
        if (!RodOn)
            return false;
        var rod = _rod!;
        ClearRod();
        Give(byPlayer, rod);
        Api.World.PlaySoundAt(RodSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    private void ClearRod()
    {
        _rod = null;
        _wind = MachineJob.None;
    }

    /// <summary>Whether <paramref name="player"/> holds tongs in either hand (the game's tongs, any
    /// metal: their tool is <c>Tongs</c>); a player in creative mode needs none.</summary>
    public static bool HasTongs(IPlayer player) =>
        player.WorldData?.CurrentGameMode == EnumGameMode.Creative || TongsSlot(player) != null;

    private static ItemSlot? TongsSlot(IPlayer player)
    {
        foreach (var slot in new[] { player.Entity?.LeftHandItemSlot, player.InventoryManager?.ActiveHotbarSlot })
            if (slot?.Itemstack?.Collectible?.Tool == EnumTool.Tongs)
                return slot;
        return null;
    }

    /// <summary>A click on a station with a rod on: starts winding it, if the player has tongs.</summary>
    private bool StartWinding(IPlayer byPlayer)
    {
        if (Api.Side == EnumAppSide.Client)
            return true;
        if (!HasTongs(byPlayer))
            return Error(byPlayer, "error-no-tongs");
        _lastWinder = byPlayer.PlayerUID;
        return StartHold(byPlayer);
    }

    /// <summary>
    /// Right-click still held on the station: the base's hand hold, the winding, only while a rod is on and
    /// (on the server) the player holds tongs. A hammer's blows are clicks, not held: with no rod on the
    /// step ends here and the client repeats the click.
    /// </summary>
    public override bool OnInteractStep(IPlayer byPlayer, float secondsUsed)
    {
        bool server = Api.Side == EnumAppSide.Server;
        if (!RodOn || server && !HasTongs(byPlayer))
        {
            if (server)
                OnInteractStop(byPlayer);
            return false;
        }
        if (server)
            _lastWinder = byPlayer.PlayerUID;
        return base.OnInteractStep(byPlayer, secondsUsed);
    }

    // The winding syncs itself as it goes (WindSyncStep), so the base's full sync is not asked for.
    protected override bool WorkUnpowered(float dt)
    {
        WindBy(Winding.RadiansPerSecond * dt);
        return false;
    }

    /// <summary>Winds the rod by <paramref name="radians"/> of the held clock (server side): W advances by
    /// their turns over <c>TurnsPerRod</c> while the rod is at working temperature (a cold rod stops, and the
    /// winder is told once), and at W = 1 the rod is used up and the spring comes off, at the rod's
    /// temperature, dropped beyond the tip; the winder's tongs pay
    /// <c>TongsWearPerRod</c>. Returns the springs delivered.</summary>
    public int WindBy(double radians)
    {
        if (!RodOn || !Built)
            return 0;
        var heat = Heat(_rod!);
        if (!heat.Hot)
        {
            if (!_toldCold && _lastWinder != null && Api.World.PlayerByUid(_lastWinder) is { } winder)
                Error(winder, "error-rod-cooled", (int)heat.Temperature, (int)heat.Workable);
            _toldCold = true;
            return 0;
        }
        _toldCold = false;
        var before = _wind;
        (_wind, bool finished) = Winding.Advance(_wind, radians, Config.TurnsPerRod, heat.Hot);
        if (finished)
        {
            var spring = SpringItem();
            var winder = _lastWinder != null ? Api.World.PlayerByUid(_lastWinder) : null;
            ClearRod();
            if (spring != null)
            {
                var stack = new ItemStack(spring, Winding.SpringsPerRod);
                spring.SetTemperature(Api.World, stack, heat.Temperature);
                Deliver(stack);
            }
            if (winder != null && Config.TongsWearPerRod > 0 && winder.WorldData?.CurrentGameMode != EnumGameMode.Creative
                && TongsSlot(winder) is { Itemstack: { } tongs } slot)
            {
                tongs.Collectible.DamageItem(Api.World, winder.Entity, slot, Config.TongsWearPerRod);
                slot.MarkDirty();
            }
            MarkDirty(true);
            return Winding.SpringsPerRod;
        }
        if (Math.Floor(before.Work / WindSyncStep) != Math.Floor(_wind.Work / WindSyncStep))
            MarkDirty(false);
        return 0;
    }

    // ---- Forging ----

    /// <summary>A right-click with a hammer (server side): on a bare mandrel, with right-click held since a hollow was finished (<see cref="Forging.Held"/>),
    /// puts another of the same on from the player's hotbar once a blow would be struck
    /// (<see cref="RefillFromHotbar"/>); on a hollow, strikes a blow, no faster than
    /// <see cref="Forging.BlowIntervalMs"/>.</summary>
    public bool StrikeBy(IPlayer byPlayer, ItemSlot hammer)
    {
        long now = Api.World.ElapsedMilliseconds;
        bool held = byPlayer.PlayerUID == _lastClickBy && Forging.Held(now - _lastClickAt);
        _lastClickBy = byPlayer.PlayerUID;
        _lastClickAt = now;
        if (!Built)
            return Error(byPlayer, "error-no-mandrel");
        if (!HollowOn)
        {
            if (held && _lastFinished != null)
            {
                if (!Forging.Ready(now - _lastBlowAt))
                    return false;
                if (RefillFromHotbar(byPlayer))
                    return true;
            }
            return Error(byPlayer, "error-no-hollow");
        }
        if (!Forging.Ready(now - _lastBlowAt))
            return false;
        _lastBlowAt = now;
        Blow(byPlayer, hammer);
        return true;
    }

    /// <summary>
    /// One blow of the hammer on the hollow (server side): W advances by the hammer's tool tier over
    /// the base tier, over the hollow's blows (<see cref="Forging.WorkPerBlow"/>; no hammer, or one with
    /// no tier, is the base), the anvil's sound and sparks at <c>strike.pos</c>, the hammer in
    /// <paramref name="hammer"/> (if any) loses its wear a blow, and at the blow that brings W to 1 the
    /// hollow is used up and its two pipe sections come off. Returns the sections delivered.
    /// </summary>
    public int Blow(IPlayer? byPlayer = null, ItemSlot? hammer = null)
    {
        if (!HollowOn || !Built)
            return 0;
        int tier = hammer?.Itemstack?.Collectible?.ToolTier ?? 0;
        (_job, bool finished) = Forging.Strike(_job, Config.BlowsPerHollow(_job.Class), tier, Config.BaseHammerTier);
        var at = WorldPoint(Rig?.Strike ?? StrikeFallback);
        Api.World.PlaySoundAt(BlowSound, at.X, at.Y, at.Z, null, true, 16, 0.8f);
        Sparks(at);
        if (hammer?.Itemstack is { } stack && Config.HammerWearPerBlow > 0
            && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            stack.Collectible.DamageItem(Api.World, byPlayer?.Entity, hammer, Config.HammerWearPerBlow);
            hammer.MarkDirty();
        }
        if (finished)
        {
            int k = _job.Class;
            _lastFinished = _hollow?.Collectible?.Code?.ToString();
            ClearJob();
            if (SectionItem(k) is { } section)
                Deliver(new ItemStack(section, Forging.SectionsPerHollow));
            MarkDirty(true);
            RefreshState();
            return Forging.SectionsPerHollow;
        }
        MarkDirty(false);
        return 0;
    }

    // Small sparks off the blow, the anvil's colour.
    private void Sparks(Vec3d at) =>
        MachineParticles.Sparks().SpawnBetween(Api.World, at.AddCopy(-0.1, 0, -0.1), at.AddCopy(0.1, 0.05, 0.1));

    /// <summary>Drops <paramref name="stack"/> just beyond the output face.</summary>
    public void Deliver(ItemStack stack)
    {
        if (Rig is not { } rig)
            return;
        var at = WorldPoint(rig.OutputDrop());
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        Api.World.SpawnItemEntity(stack, at, new Vec3d(n.X * 0.05, 0.02, n.Z * 0.05));
        Api.World.PlaySoundAt(SectionSound, at.X, at.Y, at.Z);
    }

    // ---- The renderer's ----

    /// <summary>The work (<c>hollowlead</c>, <c>hollowcopper</c>, <c>rodsteel</c>) while that job is on, as the
    /// clock shows it; the mandrel as its stage is fitted.</summary>
    public override bool ShowsPart(string? requires) =>
        MandrelRequires.IsWork(requires) ? _clock.ShowsWork(requires!) : base.ShowsPart(requires);

    /// <summary>The mandrel's texture follows its metal.</summary>
    public override string TextureKey => MandrelMetal ?? "";

    /// <summary>The block's textures with the mandrel's metal in place of <c>mandrel</c>.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        new MandrelTextureSource(base.PartTextures(capi), MachineMeshes.MetalTexture(capi, MandrelMetal));

    /// <summary>The rig's hot parts (its <c>wind.hot</c>) glow by the rod's temperature while one is wound,
    /// the vanilla way (<c>AnvilWorkItemRenderer</c> and the like).</summary>
    public override (Vec4f Colour, int Extra)? PartGlow(string? requires)
    {
        if (requires == null || _clock.JobOn != Winding.SpringClass || Rig?.Wind?.Hot is not { } hot || !hot.Contains(requires))
            return null;
        int temp = (int)RodTemperature;
        float[] glow = ColorUtil.GetIncandescenceColorAsColor4f(temp);
        int extra = Winding.ExtraGlow(temp);
        return (new Vec4f(glow[0], glow[1], glow[2], extra / 255f), extra);
    }

    /// <summary>The hammer's or the winding's clock θ, W eased to the server's after each blow or predicted
    /// while wound, k and p (<see cref="MandrelStationClock"/>).</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        _clock.Advance(dt, JobClass, JobWork, Blows, Held, TurnsPerRod);
        return _clock.Input();
    }

    /// <summary>The block's textures with the mandrel's metal in place of <c>mandrel</c>.</summary>
    private sealed class MandrelTextureSource(ITexPositionSource inner, TextureAtlasPosition? mandrel) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "mandrel" && mandrel != null ? mandrel : inner[textureCode]!;
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: the mandrel, and the hollow if it has
    /// not been struck yet, or the rod if it has not been wound yet (once work has begun, it is lost with
    /// the station).</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var stack in base.AssemblyDrops())
            yield return stack;
        if (HollowOn && _job.Untouched)
            yield return _hollow!.Clone();
        if (RodOn && _wind.Untouched)
            yield return _rod!.Clone();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (HollowOn)
            tree.SetItemstack("hollow", _hollow);
        else
            tree.RemoveAttribute("hollow");
        tree.SetInt("class", _job.Class);
        tree.SetInt("blows", _job.Steps);
        tree.SetDouble("work", _job.Work);
        if (RodOn)
            tree.SetItemstack("rod", _rod);
        else
            tree.RemoveAttribute("rod");
        tree.SetDouble("wound", _wind.Work);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetInt("needed", Config.BlowsPerHollow(_job.Class));
            tree.SetDouble("turnsPerRod", Config.TurnsPerRod);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        bool built = Built;
        _hollow = tree.GetItemstack("hollow");
        if (_hollow != null && !_hollow.ResolveBlockOrItem(worldForResolving))
            _hollow = null;
        _job = _hollow == null || !built ? MachineJob.None : Forging.Restore(tree.GetInt("class"), tree.GetInt("blows"), tree.GetDouble("work"));
        if (!_job.On)
            _hollow = null;
        _rod = tree.GetItemstack("rod");
        if (_rod != null && (!_rod.ResolveBlockOrItem(worldForResolving) || _job.On || !built))
            _rod = null;
        _wind = Winding.Restore(_rod != null, tree.GetDouble("wound"));
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverBlowsNeeded = tree.TryGetInt("needed") is > 0 and var n ? n : null;
            _serverTurnsPerRod = tree.TryGetDouble("turnsPerRod") is > 0 and var t ? t : null;
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _hollow?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_hollow), blockIdMapping, itemIdMapping);
        _rod?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_rod), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _hollow?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
        _rod?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    /// <summary>The hollow on the mandrel, its blows and how far it is forged; or the rod, its heat and how far
    /// it is wound.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(MandrelStationSystem.Domain + ":mandrelstation-" + key, args);
        if (HollowOn)
            dsc.AppendLine(L("info-hollow", L("metal-" + Forging.MetalOf(_job.Class)), _job.Steps, (int)Math.Floor(_job.Work * 100 + 1e-6)));
        else if (RodOn)
        {
            var heat = Heat(_rod!);
            dsc.AppendLine(L(heat.Hot ? "info-rod" : "info-rod-cold", (int)Math.Floor(_wind.Work * 100 + 1e-6), (int)heat.Temperature, (int)heat.Workable));
        }
    }

    /// <summary>For a wrench: the forging's W exactly and the blows a hollow of its metal takes with the base
    /// hammer, or the winding's W and the turns a rod takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        string F(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        if (HollowOn)
            dsc.AppendLine(Lang.Get(MandrelStationSystem.Domain + ":mandrelstation-info-forge", F(_job.Work, "0.000"), BlowsNeeded));
        else if (RodOn)
            dsc.AppendLine(Lang.Get(MandrelStationSystem.Domain + ":mandrelstation-info-wind", F(_wind.Work, "0.000"), F(TurnsPerRod, "0.##")));
    }
}
