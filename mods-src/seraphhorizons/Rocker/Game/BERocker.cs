using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rocker.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Rocker;

/// <summary>
/// The rocker (#716), on the shared machine base (<see cref="BEMachine"/>: placed whole, so its build has no
/// stages; its block info, its renderer): the charge in its hopper, the item being washed, what lies behind
/// its riffles and the units carried between items (<see cref="RockerState"/>). A hand machine
/// (<see cref="BEMachine.HandWorked"/>, its holds forgotten 400 ms after the last step): the player tips
/// crushed or ground ore, or placer gravel, into the hopper and works it by holding right-click on it with
/// water in a bucket, as on the quern: the server rocks it while held, pours the bucket into the hopper as it
/// does (<see cref="RockerWater"/>: the water is poured as it rocks, never stored, so the base's reservoir is
/// not the rocker's), puts what each washed item gives behind the riffles and drops the tailings in the cell
/// beyond the foot (#694). The concentrate comes out by hand, an empty hand's right-click. No automated input
/// or output: it has no inventory any hopper or chute can reach.
/// </summary>
public class BERocker : BEMachine
{
    private static readonly AssetLocation PourSound = new("game", "sounds/effect/water-pour");
    private static readonly AssetLocation LoadSound = new("game", "sounds/block/gravel");
    private static readonly AssetLocation TakeSound = new("game", "sounds/block/sand");
    // The server syncs the washing at least this often (of an item).
    private const double SyncStep = 0.1;
    private const float SplashInterval = 0.15f;

    private readonly RockerState _state = new();
    private ItemStack? _charge;
    private TailingsOwed _tailings;
    private double _litresOwed;
    private string? _rocker;
    private float _pourSeconds;
    private float? _serverRocksPerSecond;
    private readonly RockerClock _clock = new();
    private readonly ParticleTimer _splash = new(SplashInterval);

    public RockerState Washing => _state;
    /// <summary>One item of the charge in the hopper (its kind), or null.</summary>
    public ItemStack? Charge => _state.HasCharge ? _charge : null;
    /// <summary>The code of what lies behind the riffles (the most of it), for the renderer; or null.</summary>
    public string? CatchCode => _state.Riffles.OrderByDescending(r => r.Value).Select(r => r.Key).FirstOrDefault();

    public float RocksPerSecond => _serverRocksPerSecond ?? Config.RocksPerSecond;

    public bool CanTakeMoreCharge => !_state.HasCharge || _state.ChargeCount < Config.ChargeItems;

    private RockerSystem System => RockerSystem.Of(Api);
    private RockerConfig Config => System.Config;
    public new RockerRig? Rig => base.Rig as RockerRig;

    /// <summary>Placed whole: a build with no stages (no build file), so the rocker is built as it is placed.</summary>
    protected override MachineBuild? BuildSpec(IWorldAccessor world) => new(RockerSystem.MachineId, null, FeedMode.Continuous, [], []);

    protected override bool HandWorked => true;
    // How long after its last step a player's hold counts (the game steps every tick while held).
    protected override long HoldTimeoutMs => 400;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 100;
    public override int RenderRange => 48;

    public override bool HasWork => _state.HasCharge;
    public override double? WorkProgress => _state.HasCharge ? _state.ItemWork : null;

    /// <summary>The world position of the cell the tailings fall into, beyond the open foot.</summary>
    public BlockPos TailingsPos() => CellPos(Rig?.TailingsCell ?? new Int3(-1, 0, 0));

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the rocker. With crushed or ground ore or placer gravel in hand, tips as much of it
    /// as the hopper holds in (one kind at a time). With a liquid container in hand, starts rocking:
    /// held, it rocks while water is poured (<see cref="OnInteractStep"/>). With an empty hand, takes
    /// everything from behind the riffles; Ctrl and an empty hand takes the charge back while untouched.
    /// Shift lets a held block be placed against it.
    /// </summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var held = slot.Itemstack;
        var controls = byPlayer.Entity.Controls;
        bool server = Api.Side == EnumAppSide.Server;
        if (controls.ShiftKey)
            return false;
        if (held == null)
        {
            if (!server)
                return _state.HasCatch || _state.HasCharge;
            if (controls.CtrlKey)
                return TakeCharge(byPlayer);
            if (_state.HasCatch)
                return TakeCatch(byPlayer);
            return _state.HasCharge ? Error(byPlayer, "error-need-water") : Error(byPlayer, "error-empty");
        }
        if (RockerFeed.Parse(held.Collectible.Code.ToString()) != null)
            return !server || TryLoad(slot, byPlayer);
        if (RockerWater.IsContainer(held))
        {
            if (!server)
                return true;
            if (_state.Stop(RockerWater.Litres(held), Config) is var stop and not RockerStop.None)
                return Error(byPlayer, stop switch
                {
                    RockerStop.NoCharge => "error-no-charge",
                    RockerStop.RifflesFull => "error-riffles-full",
                    _ => "error-no-water",
                });
            _rocker = byPlayer.PlayerUID;
            return StartHold(byPlayer);
        }
        return false;
    }

    /// <summary>Right-click still held (both sides, every tick): the player goes on rocking while they hold a
    /// liquid container, and (on the server) while it has the water the next rock pours and the rocker can
    /// wash.</summary>
    public override bool OnInteractStep(IPlayer byPlayer, float secondsUsed)
    {
        var held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        bool server = Api.Side == EnumAppSide.Server;
        if (!RockerWater.IsContainer(held)
            || server && _state.Stop(RockerWater.Litres(held) - _litresOwed, Config) != RockerStop.None)
        {
            if (server)
                OnInteractStop(byPlayer);
            return false;
        }
        if (server)
            _rocker = byPlayer.PlayerUID;
        return base.OnInteractStep(byPlayer, secondsUsed);
    }

    /// <summary>The key a charge's kind is held by: its code and the ore's state on the stack (riddled,
    /// ground, amalgamated, its units), so items that would not stack do not share a charge.</summary>
    public static string KindKey(ItemStack stack) =>
        stack.Collectible.Code + "|" + (stack.Attributes is TreeAttribute tree && tree.Count > 0 ? tree.ToJsonToken() : "");

    /// <summary>Tips as much of the held feed as the hopper takes (server side).</summary>
    public bool TryLoad(ItemSlot slot, IPlayer? byPlayer)
    {
        var stack = slot.Itemstack;
        if (stack == null)
            return false;
        switch (_state.CanLoad(KindKey(stack), RockerFeed.Parse(stack.Collectible.Code.ToString()) != null, Config, out int room))
        {
            case RockerLoadVerdict.Loads:
                break;
            case RockerLoadVerdict.OtherFeed:
                return Error(byPlayer, "error-other-charge");
            case RockerLoadVerdict.Full:
                return Error(byPlayer, "error-hopper-full");
            default:
                return false;
        }
        int n = _state.Load(KindKey(stack), Math.Min(room, stack.StackSize), Config);
        if (n <= 0)
            return false;
        if (_charge == null || _state.ChargeCount == n)
        {
            _charge = stack.Clone();
            _charge.StackSize = 1;
        }
        slot.TakeOut(n);
        slot.MarkDirty();
        var at = WorldPoint(Rig?.Pour.Pos ?? new Float3(0.5f, 0.6f, 0.5f));
        Api.World.PlaySoundAt(LoadSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Ctrl and an empty hand (server side): the charge comes back while untouched.</summary>
    public bool TakeCharge(IPlayer byPlayer)
    {
        if (!_state.HasCharge)
            return Error(byPlayer, "error-empty");
        if (!_state.ChargeUntouched)
            return Error(byPlayer, "error-busy");
        var stack = _charge!.Clone();
        stack.StackSize = _state.Unload();
        _charge = null;
        Give(byPlayer, stack);
        MarkDirty(true);
        return true;
    }

    /// <summary>An empty hand (server side): everything behind the riffles comes out.</summary>
    public bool TakeCatch(IPlayer byPlayer)
    {
        var caught = _state.TakeCatch();
        foreach (var stack in StacksOf(caught))
            Give(byPlayer, stack);
        var at = WorldPoint(Rig?.Concentrate.Pos ?? new Float3(0.4f, 0.3f, 0.5f));
        Api.World.PlaySoundAt(TakeSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
        return true;
    }

    private IEnumerable<ItemStack> StacksOf(IReadOnlyDictionary<string, int> counts)
    {
        foreach (var (code, count) in counts)
        {
            var loc = new AssetLocation(code);
            CollectibleObject? c = Api.World.GetItem(loc) ?? (CollectibleObject?)Api.World.GetBlock(loc);
            if (c == null || c.IsMissing || c.Id == 0)
            {
                Api.Logger.Warning("[seraphhorizons] Rocker: no {0} in this world; {1} of it lost", code, count);
                continue;
            }
            int max = Math.Max(1, c.MaxStackSize);
            for (int left = count; left > 0; left -= max)
                yield return new ItemStack(c, Math.Min(max, left));
        }
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
    }

    /// <summary>The rocker's own message (<c>seraphhorizons:rocker-{key}</c>) to the player; false.</summary>
    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("rocker-" + key, Lang.GetL(sp.LanguageCode, RockerSystem.Domain + ":rocker-" + key, args));
        return false;
    }

    /// <summary>The help: feed tips in while the hopper has room; a bucket of water rocks a charge; an empty
    /// hand takes the concentrate, and Ctrl the charge back while untouched.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => RockerSystem.Domain + ":blockhelp-rocker-" + action;
        var world = Api.World;
        if (CanTakeMoreCharge)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Key("charge"), MouseButton = EnumMouseButton.Right,
                Itemstacks = Stacks(world, ["seraphhorizons:crushedore-quartz_nativegold-medium", "seraphhorizons:crushedore-cassiterite-medium",
                                            "seraphhorizons:groundore-galena-poor", "game:richgravel-granite"], 1),
            });
        if (_state.HasCharge)
            help.Add(new WorldInteraction { ActionLangCode = Key("rock"), MouseButton = EnumMouseButton.Right, Itemstacks = WaterStacks(world) });
        if (_state.HasCatch)
            help.Add(new WorldInteraction { ActionLangCode = Key("takeconcentrate"), MouseButton = EnumMouseButton.Right });
        if (_state.HasCharge && _state.ChargeUntouched)
            help.Add(new WorldInteraction { ActionLangCode = Key("takecharge"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // A full bucket of water, for the help.
    private static ItemStack[] WaterStacks(IWorldAccessor world)
    {
        if (world.GetBlock(new AssetLocation("game:woodbucket")) is not BlockLiquidContainerBase bucket
            || world.GetItem(new AssetLocation(RockerWater.PlainWater)) is not { } water)
            return [];
        var full = new ItemStack(bucket);
        var props = BlockLiquidContainerBase.GetContainableProps(new ItemStack(water));
        bucket.SetContent(full, new ItemStack(water, (int)((props?.ItemsPerLitre ?? 100) * bucket.CapacityLitres)));
        return [full];
    }

    // ---- Rocking ----

    // The rocking syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool WorkUnpowered(float dt)
    {
        if (_rocker != null && Api.World.PlayerByUid(_rocker) is { } player)
            Rock(player, Config.RocksPerSecond * dt);
        return false;
    }

    /// <summary>
    /// Rocks it by up to <paramref name="rocks"/> with the water in <paramref name="player"/>'s held
    /// container (server side): the water poured is taken out of it, each item washed through puts what
    /// it gives behind the riffles, and its tailings fall beyond the foot. Returns the step.
    /// </summary>
    public RockStep Rock(IPlayer player, double rocks)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        double available = RockerWater.Litres(slot?.Itemstack) - _litresOwed;
        var template = _charge;
        if (slot == null || template == null)
            return RockStep.None;
        double before = _state.ItemWork;
        int countBefore = _state.ChargeCount;
        var step = _state.Rock(rocks, available, Config, () => System.YieldOf(template));
        if (step.Litres > 0)
        {
            double perLitre = RockerWater.PortionsPerLitre(slot.Itemstack);
            _litresOwed += step.Litres;
            int portions = (int)Math.Floor(_litresOwed * perLitre + 1e-9);
            if (portions > 0)
                _litresOwed -= RockerWater.Take(player, slot, portions) / perLitre;
        }
        if (step.Washed > 0)
            Discard(template, step.Washed);
        if (!_state.HasCharge)
            _charge = null;
        if (step.Washed > 0 || step.Caught.Count > 0 || !_state.HasCharge)
            MarkDirty(true);
        else if (countBefore != _state.ChargeCount || Math.Floor(before / SyncStep) != Math.Floor(_state.ItemWork / SyncStep))
            MarkDirty(false);
        RefreshState();
        return step;
    }

    // The tailings of the items washed: the rocker's rule (#694), in the feed's rock, into the cell beyond the foot.
    private void Discard(ItemStack template, int washed)
    {
        if (OreMachinesSystem.Of(Api).Config.TailingsFor(RockerSystem.MachineId) is not { } rule)
            return;
        (int whole, _tailings) = _tailings.Add(washed, rule);
        if (whole > 0)
            Machines.Tailings.Discard(Api.World, TailingsPos(), rule.Form, Machines.Tailings.RockOf(template), whole);
    }

    // A worked rocker pours.
    protected override void OnClientTick(float dt)
    {
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !Held || !_state.HasCharge)
        {
            _pourSeconds = 0;
            return;
        }
        if ((_pourSeconds -= dt) <= 0)
        {
            _pourSeconds = 1.8f;
            var at = WorldPoint(rig.Pour.Pos);
            capi.World.PlaySoundAt(PourSound, at.X, at.Y, at.Z, null, true, 12, 0.4f);
        }
    }

    // ---- The renderer's ----

    // Worked: someone rocks it with a charge in the hopper.
    private bool Worked => Held && _state.HasCharge;

    /// <summary>Nothing of it stands still, so the block's chunk mesh is empty and every rig part is drawn: the
    /// cradle always; the water while it is worked; the charge while one is in the hopper and the concentrate
    /// while some lies behind the riffles.</summary>
    public override bool ShowsPart(string? requires) => requires switch
    {
        null => true,
        RockerRequires.Water => Worked,
        RockerRequires.Charge => Charge != null,
        RockerRequires.Concentrate => CatchCode != null,
        _ => false,
    };

    /// <summary>The charge's and the concentrate's textures follow what is in it.</summary>
    public override string TextureKey => (Charge?.Collectible.Code.ToString() ?? "") + "|" + (CatchCode ?? "");

    /// <summary>The block's textures with the charge's and the concentrate's own in place of <c>charge</c> and
    /// <c>concentrate</c>.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi)
    {
        var catchStack = CatchCode is { } code ? StackOf(code) : null;
        return new LoadTextureSource(base.PartTextures(capi), Atlas(capi, Charge), Atlas(capi, catchStack));
    }

    private ItemStack? StackOf(string code)
    {
        var loc = new AssetLocation(code);
        CollectibleObject? c = Api.World.GetItem(loc) ?? (CollectibleObject?)Api.World.GetBlock(loc);
        return c == null || c.IsMissing || c.Id == 0 ? null : new ItemStack(c);
    }

    /// <summary>An item's own texture in the block atlas (a crushed ore's, the concentrate's, a gravel's), or null.</summary>
    private static TextureAtlasPosition? Atlas(ICoreClientAPI capi, ItemStack? stack)
    {
        if (stack == null)
            return null;
        CompositeTexture? texture = stack.Item != null ? stack.Item.FirstTexture : stack.Block?.Textures?.Values.FirstOrDefault();
        if (texture == null)
            return null;
        try
        {
            return new ContainerTextureSource(capi, stack, texture)["charge"];
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[seraphhorizons] Rocker: no texture for {0}: {1}", stack.Collectible.Code, e.Message);
            return null;
        }
    }

    /// <summary>The rocking clock θ (<see cref="RockerClock"/>: a turn a rock, run on to level when the hold
    /// ends); while worked, splashes at the foot (<c>outflow</c>).</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        bool worked = Worked;
        _clock.Advance(dt, worked, RocksPerSecond);
        if (!far && worked && Rig is { } rig && _splash.Due(dt))
        {
            var at = WorldPoint(rig.Posed(rig.Outflow, _clock.Theta));
            (MachineParticles.Splash(ColorUtil.ToRgba(140, 160, 190, 220)) with { MinVelocity = new Vec3f(-0.6f, -0.2f, -0.2f), MaxVelocity = new Vec3f(-0.2f, 0.3f, 0.2f) })
                .Spawn(Api.World, at, MachineParticles.Around(0.05, 0.02, 0.15));
        }
        return new RigInput(_clock.Theta);
    }

    /// <summary>The block's textures with the charge's and the concentrate's in place of their codes.</summary>
    private sealed class LoadTextureSource(ITexPositionSource inner, TextureAtlasPosition? charge, TextureAtlasPosition? concentrate)
        : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] => textureCode switch
        {
            "charge" when charge != null => charge,
            "concentrate" when concentrate != null => concentrate,
            _ => inner[textureCode]!,
        };
    }

    // ---- Breaking ----

    /// <summary>What breaking it gives besides the rocker: the charge while untouched, and everything
    /// behind the riffles (an item half washed is lost).</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        if (_state.HasCharge && _charge != null)
        {
            int n = _state.ChargeCount - (_state.ChargeUntouched ? 0 : 1);
            if (n > 0)
            {
                var stack = _charge.Clone();
                stack.StackSize = n;
                yield return stack;
            }
        }
        foreach (var stack in StacksOf(_state.Riffles))
            yield return stack;
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (_state.HasCharge && _charge != null)
        {
            tree.SetItemstack("charge", _charge);
            tree.SetString("chargeKey", _state.ChargeKey);
            tree.SetInt("chargeCount", _state.ChargeCount);
            tree.SetDouble("itemWork", _state.ItemWork);
        }
        else
        {
            tree.RemoveAttribute("charge");
            tree.RemoveAttribute("chargeKey");
            tree.SetInt("chargeCount", 0);
            tree.SetDouble("itemWork", 0);
        }
        var riffles = new TreeAttribute();
        foreach (var (code, n) in _state.Riffles)
            riffles.SetInt(code, n);
        tree["riffles"] = riffles;
        var carry = new TreeAttribute();
        foreach (var (code, units) in _state.Carry.Held)
            carry.SetDouble(code, units);
        tree["carry"] = carry;
        tree.SetDouble("tailings", _tailings.Layers);
        tree.SetDouble("litresOwed", _litresOwed);
        if (Api?.Side == EnumAppSide.Server)
            tree.SetFloat("rocksPerSecond", Config.RocksPerSecond);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _charge = tree.GetItemstack("charge");
        if (_charge != null && !_charge.ResolveBlockOrItem(worldForResolving))
            _charge = null;
        var riffles = (tree["riffles"] as ITreeAttribute)?.Select(kv => new KeyValuePair<string, int>(kv.Key, (kv.Value as IntAttribute)?.value ?? 0))
                      ?? [];
        var carry = (tree["carry"] as ITreeAttribute)?.Select(kv => new KeyValuePair<string, double>(kv.Key, (kv.Value as DoubleAttribute)?.value ?? 0))
                    ?? [];
        _state.Restore(_charge == null ? null : tree.GetString("chargeKey") ?? KindKey(_charge), tree.GetInt("chargeCount"),
            tree.GetDouble("itemWork"), riffles.ToList(), carry.ToList());
        if (!_state.HasCharge)
            _charge = null;
        _tailings = TailingsOwed.Restore(tree.GetDouble("tailings"));
        _litresOwed = Math.Clamp(tree.GetDouble("litresOwed"), 0, 1);
        if (worldForResolving.Side == EnumAppSide.Client)
            _serverRocksPerSecond = tree.TryGetFloat("rocksPerSecond");
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _charge?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_charge), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _charge?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    /// <summary>The charge in the hopper and how far the item in hand is washed, and what lies behind the riffles.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(RockerSystem.Domain + ":rocker-" + key, args);
        if (Charge is { } charge)
            dsc.AppendLine(L("info-charge", _state.ChargeCount, charge.GetName(), (int)Math.Floor(_state.ItemWork * 100 + 1e-9)));
        if (_state.HasCatch)
        {
            var names = _state.Riffles.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r =>
            {
                var loc = new AssetLocation(r.Key);
                CollectibleObject? c = Api.World.GetItem(loc) ?? (CollectibleObject?)Api.World.GetBlock(loc);
                return $"{r.Value} {(c == null ? r.Key : new ItemStack(c).GetName())}";
            });
            dsc.AppendLine(L("info-riffles", string.Join(", ", names)));
            if (_state.RiffleCount >= Config.RiffleItems)
                dsc.AppendLine(L("info-riffles-full"));
        }
    }

    /// <summary>For a wrench: the item in hand's washing exactly, the rocks a second, and the water owed to the
    /// bucket.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (_state.HasCharge)
            dsc.AppendLine(Lang.Get(RockerSystem.Domain + ":rocker-info-work",
                _state.ItemWork.ToString("0.000", CultureInfo.InvariantCulture), RocksPerSecond.ToString("0.##", CultureInfo.InvariantCulture),
                _litresOwed.ToString("0.00", CultureInfo.InvariantCulture)));
    }
}
