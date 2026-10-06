using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.PicklingTub.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.PicklingTub;

/// <summary>
/// The pickling tub's state: its free liquid (an item stack of a liquid portion) and its batch of
/// gears (<see cref="TubBatch"/>), with the rule the batch started under, so a running batch keeps
/// its timings when the settings change and a client reads them from the tree. The batch is a
/// function of the game's clock (<see cref="IGameCalendar.TotalHours"/>); the server's tick only
/// starts a waiting batch and resends the tree when the batch's stage changes, so the client redraws.
/// </summary>
public class BEPicklingTub : BlockEntity
{
    public const string Domain = PicklingTubSystem.Domain;

    /// <summary>The tub's inside, in blocks: the floor's top and the brim the liquid reaches when full.</summary>
    public const float FloorY = 2 / 16f, BrimY = 11 / 16f;

    private ItemStack? _liquid;
    private TubBatch? _batch;
    private TubRuleConfig? _rule;
    private TubStage? _shown;
    private MeshData? _liquidMesh;
    private MeshData? _gearsMesh;

    /// <summary>The free liquid, outside the batch.</summary>
    public ItemStack? Liquid => _liquid;

    /// <summary>The batch, or null when the tub has no gears.</summary>
    public TubBatch? Batch
    {
        get => _batch;
        // Scenarios age a batch by moving its start back: the shared test world's clock is not theirs to move.
        set
        {
            _batch = value;
            MarkDirty(true);
        }
    }

    /// <summary>The rule the batch started under; null while it waits.</summary>
    public TubRuleConfig? Rule => _rule;

    public double Now => Api.World.Calendar.TotalHours;

    private TubRuleBook Rules => PicklingTubSystem.Of(Api).Rules;
    private PicklingTubConfig Config => Rules.Config;

    /// <summary>The batch's stage now; null without a batch.</summary>
    public TubStage? Stage => _batch == null ? null
        : _rule == null ? new TubStage(TubPhase.Waiting, 0, _batch.Count, 0, 0)
        : _batch.StageAt(_rule, Now);

    /// <summary>The free liquid's litres.</summary>
    public double FreeLitres => _liquid == null ? 0 : TubLiquid.Litres(_liquid.StackSize, ItemsPerLitre(_liquid));

    /// <summary>All the liquid in the tub, the batch's included.</summary>
    public double TotalLitres => FreeLitres + (_batch?.Litres ?? 0);

    /// <summary>The liquid the tub holds: its free liquid's, else the running batch's.</summary>
    public string? LiquidCode => _liquid?.Collectible?.Code?.ToString() ?? (_batch?.Started == true ? _batch.Liquid : null);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => Tick(), 2000);
        else
            BuildMeshes();
    }

    // ---- The clock ----

    /// <summary>Starts a waiting batch when it can, and resends the tree when the stage changes
    /// (server side).</summary>
    public void Tick()
    {
        bool changed = TryStart();
        var stage = Stage;
        if (changed || !SameLook(stage, _shown))
        {
            _shown = stage;
            MarkDirty(true);
        }
    }

    // Progress changes all the time; only what is drawn or said differently resends the tree.
    private static bool SameLook(TubStage? a, TubStage? b) =>
        a is { } x ? b is { } y && x.Phase == y.Phase && x.Inputs == y.Inputs && x.Outputs == y.Outputs && x.Lost == y.Lost : b == null;

    /// <summary>Starts the batch if it waits and the free liquid has a rule for it and enough
    /// litres: those litres go into the batch. Returns whether it started.</summary>
    public bool TryStart()
    {
        if (_batch is not { Started: false } batch || _liquid?.Collectible?.Code is not { } code)
            return false;
        var rule = Rules.For(code.ToString(), batch.Input);
        if (rule == null)
            return false;
        float perLitre = ItemsPerLitre(_liquid);
        int items = Math.Max(1, TubLiquid.Items(Config.LitresPerBatch, perLitre));
        if (Config.LitresPerBatch > 0 && _liquid.StackSize < items)
            return false;
        int drawn = Config.LitresPerBatch > 0 ? items : 0;
        _liquid.StackSize -= drawn;
        if (_liquid.StackSize <= 0)
            _liquid = null;
        _rule = Copy(rule);
        _batch = batch.Start(rule, code.ToString(), TubLiquid.Litres(drawn, perLitre), Now, Api.World.Rand.NextDouble);
        return true;
    }

    private static TubRuleConfig Copy(TubRuleConfig rule) => new(rule.Liquid, rule.Input, rule.Output, rule.Hours, rule.GraceHours, rule.LossEveryHours, rule.Kind)
    {
        Failure = rule.Failure,
        FailureQuantity = rule.FailureQuantity,
        LossChance = rule.LossChance,
    };

    // ---- Right-clicks ----

    private enum Action
    {
        None,
        Pour,
        Fill,
        AddGears,
        Refuse,
        TakeOut,
    }

    /// <summary>A right-click: pours liquid in, takes it out into an empty container, adds the held
    /// gears or takes the batch out with an empty hand. Returns whether the click was the tub's; the
    /// work is done on the server.</summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var held = slot?.Itemstack;
        var action = Decide(held, out var refusal);
        if (action == Action.None)
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;
        bool changed = action switch
        {
            Action.Pour => Pour(byPlayer, slot!),
            Action.Fill => Fill(byPlayer, slot!),
            Action.AddGears => AddGears(slot!),
            Action.TakeOut => TakeOut(byPlayer),
            _ => Refuse(byPlayer, refusal),
        };
        if (changed)
        {
            TryStart();
            _shown = Stage;
            MarkDirty(true);
        }
        return true;
    }

    private Action Decide(ItemStack? held, out string refusal)
    {
        refusal = "";
        if (held == null)
            return _batch != null ? Action.TakeOut : Action.None;
        if (held.Collectible is ILiquidSource source && source.GetContent(held) is { Collectible.Code: { } content })
        {
            string code = content.ToString();
            if (!Rules.IsLiquid(code))
                return Action.None;
            if (LiquidCode is { } inTub && !string.Equals(inTub, code, StringComparison.OrdinalIgnoreCase))
            {
                refusal = "otherliquid";
                return Action.Refuse;
            }
            if (!Rules.CanPour(code, _batch))
            {
                refusal = "liquidforgears";
                return Action.Refuse;
            }
            return Action.Pour;
        }
        if (held.Collectible is BlockLiquidContainerBase && _liquid != null)
            return Action.Fill;
        string? gear = held.Collectible?.Code?.ToString();
        var why = Rules.CanAdd(gear ?? "", LiquidCode, _batch, Now);
        if (why == TubRefusal.NotAGear)
            return Action.None;
        if (why == TubRefusal.None)
            return Action.AddGears;
        refusal = why.ToString().ToLowerInvariant();
        return Action.Refuse;
    }

    private bool Refuse(IPlayer player, string refusal)
    {
        if (player is IServerPlayer sp)
            sp.SendIngameError("picklingtub-" + refusal,
                Lang.GetL(sp.LanguageCode, $"{Domain}:picklingtub-error-{refusal}", Config.BatchSize, LiquidName(sp.LanguageCode)));
        return false;
    }

    private string LiquidName(string language)
    {
        string? code = LiquidCode;
        if (code == null)
            return "";
        var collectible = Api.World.GetItem(new AssetLocation(code));
        return collectible == null ? code : collectible.GetHeldItemName(new ItemStack(collectible)).ToLower(CultureInfo.InvariantCulture);
    }

    private bool Pour(IPlayer player, ItemSlot slot)
    {
        var held = slot.Itemstack!;
        var source = (ILiquidSource)held.Collectible;
        var content = source.GetContent(held)!;
        float perLitre = ItemsPerLitre(content);
        int inTub = (_liquid?.StackSize ?? 0) + TubLiquid.Items(_batch?.Litres ?? 0, perLitre);
        int fit = TubLiquid.ItemsThatFit(Config.CapacityLitres, inTub, content.StackSize, perLitre);
        if (fit <= 0)
        {
            Refuse(player, "fulltub");
            return false;
        }
        int taken;
        if (held.StackSize > 1 && source is BlockLiquidContainerBase container)
            taken = container.SplitStackAndPerformAction(player.Entity, slot, one => container.TryTakeContent(one, fit)?.StackSize ?? 0);
        else
            taken = source.TryTakeContent(held, fit)?.StackSize ?? 0;
        slot.MarkDirty();
        if (taken <= 0)
            return false;
        if (_liquid == null)
        {
            _liquid = content.Clone();
            _liquid.StackSize = taken;
        }
        else
            _liquid.StackSize += taken;
        if (source is BlockLiquidContainerBase effects)
            effects.DoLiquidMovedEffects(player, content, taken, BlockLiquidContainerBase.EnumLiquidDirection.Pour);
        return true;
    }

    private bool Fill(IPlayer player, ItemSlot slot)
    {
        var held = slot.Itemstack!;
        var container = (BlockLiquidContainerBase)held.Collectible;
        var liquid = _liquid!;
        if (container.GetContent(held) is { } inside && !inside.Equals(Api.World, liquid, GlobalConstants.IgnoredStackAttributes))
            return false;
        float litres = (float)FreeLitres;
        int moved = held.StackSize > 1
            ? container.SplitStackAndPerformAction(player.Entity, slot, one => container.TryPutLiquid(one, liquid, litres))
            : container.TryPutLiquid(held, liquid, litres);
        slot.MarkDirty();
        if (moved <= 0)
            return false;
        container.DoLiquidMovedEffects(player, liquid, moved, BlockLiquidContainerBase.EnumLiquidDirection.Fill);
        liquid.StackSize -= moved;
        if (liquid.StackSize <= 0)
            _liquid = null;
        return true;
    }

    private bool AddGears(ItemSlot slot)
    {
        var held = slot.Itemstack!;
        string code = held.Collectible.Code.ToString();
        int count = Math.Min(held.StackSize, Config.BatchSize - (_batch?.Count ?? 0));
        if (count <= 0)
            return false;
        slot.TakeOut(count);
        slot.MarkDirty();
        if (_batch == null)
            _batch = new TubBatch(code, count);
        else
        {
            var rule = _rule == null ? null : Rules.For(_batch.Liquid, _batch.Input) ?? _rule;
            _batch = _batch.Add(count, rule, Now, Api.World.Rand.NextDouble);
            if (rule != null && _batch.Started)
                _rule = Copy(rule);
        }
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/environment/smallsplash"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        return true;
    }

    /// <summary>Takes the batch out into <paramref name="player"/>'s inventory (what does not fit is
    /// dropped on the tub): before done the gears that went in and their litres back into the tub,
    /// from done what the batch became.</summary>
    private bool TakeOut(IPlayer player)
    {
        if (_batch == null)
            return false;
        var result = _batch.TakeOut(_rule, Now);
        if (result.RefundLitres > 0 && _batch.Liquid != null)
            Refund(_batch.Liquid, result.RefundLitres);
        foreach (var (code, quantity) in result.Items)
            Give(player, code, quantity);
        _batch = null;
        _rule = null;
        return true;
    }

    private void Refund(string liquidCode, double litres)
    {
        if (_liquid != null && !string.Equals(_liquid.Collectible.Code.ToString(), liquidCode, StringComparison.OrdinalIgnoreCase))
            return; // another liquid was poured in since: the litre is lost
        var item = Api.World.GetItem(new AssetLocation(liquidCode));
        if (item == null)
            return;
        var stack = _liquid ?? new ItemStack(item, 0);
        float perLitre = ItemsPerLitre(stack);
        int room = TubLiquid.Items(Config.CapacityLitres, perLitre) - stack.StackSize;
        stack.StackSize += Math.Clamp(TubLiquid.Items(litres, perLitre), 0, Math.Max(0, room));
        _liquid = stack.StackSize > 0 ? stack : null;
    }

    private void Give(IPlayer player, string code, int quantity)
    {
        var location = new AssetLocation(code);
        CollectibleObject? collectible = Api.World.GetItem(location);
        collectible ??= Api.World.GetBlock(location) is { Id: > 0 } block ? block : null;
        if (collectible == null)
        {
            Api.Logger.Warning("[seraphhorizons] Pickling tub: no item {0}, so {1} of it are lost", code, quantity);
            return;
        }
        int max = Math.Max(1, collectible.MaxStackSize);
        while (quantity > 0)
        {
            var stack = new ItemStack(collectible, Math.Min(max, quantity));
            quantity -= stack.StackSize;
            // The game takes what it gives out of the stack; the rest is dropped on the tub.
            player.InventoryManager.TryGiveItemstack(stack, true);
            if (stack.StackSize > 0)
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.9, 0.5));
        }
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        // The gears come out as they are; the liquid is spilled.
        if (Api.Side == EnumAppSide.Server && _batch != null)
            foreach (var (code, quantity) in _batch.TakeOut(_rule, Now).Items)
                Drop(code, quantity);
        _batch = null;
        base.OnBlockBroken(byPlayer);
    }

    private void Drop(string code, int quantity)
    {
        var location = new AssetLocation(code);
        CollectibleObject? collectible = Api.World.GetItem(location);
        collectible ??= Api.World.GetBlock(location) is { Id: > 0 } block ? block : null;
        if (collectible == null)
            return;
        int max = Math.Max(1, collectible.MaxStackSize);
        while (quantity > 0)
        {
            var stack = new ItemStack(collectible, Math.Min(max, quantity));
            quantity -= stack.StackSize;
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }

    private static float ItemsPerLitre(ItemStack stack) => BlockLiquidContainerBase.GetContainableProps(stack)?.ItemsPerLitre ?? 100;

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (_liquid != null)
            tree.SetItemstack("liquid", _liquid);
        else
            tree.RemoveAttribute("liquid");
        if (_batch == null)
        {
            tree.RemoveAttribute("batch");
            return;
        }
        var batch = new TreeAttribute();
        batch.SetString("input", _batch.Input);
        batch.SetInt("count", _batch.Count);
        if (_batch.Liquid != null)
            batch.SetString("liquid", _batch.Liquid);
        if (_batch.StartHours is { } start)
            batch.SetDouble("start", start);
        batch.SetDouble("litres", _batch.Litres);
        batch.SetInt("lossAtDone", _batch.LossAtDone);
        if (_rule != null)
        {
            var rule = new TreeAttribute();
            rule.SetString("liquid", _rule.Liquid);
            rule.SetString("input", _rule.Input);
            rule.SetString("output", _rule.Output);
            rule.SetDouble("hours", _rule.Hours);
            rule.SetString("failure", _rule.Failure);
            rule.SetInt("failureQuantity", _rule.FailureQuantity);
            rule.SetDouble("graceHours", _rule.GraceHours);
            rule.SetDouble("lossEveryHours", _rule.LossEveryHours);
            rule.SetDouble("lossChance", _rule.LossChance);
            rule.SetString("kind", _rule.Kind.ToString());
            batch["rule"] = rule;
        }
        tree["batch"] = batch;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _liquid = tree.GetItemstack("liquid");
        if (_liquid != null && !_liquid.ResolveBlockOrItem(worldForResolving))
            _liquid = null;
        _batch = null;
        _rule = null;
        if (tree["batch"] is ITreeAttribute batch && batch.GetString("input") is { } input)
        {
            double? start = batch.HasAttribute("start") ? batch.GetDouble("start") : null;
            _batch = new TubBatch(input, batch.GetInt("count"), batch.GetString("liquid"), start, batch.GetDouble("litres"), batch.GetInt("lossAtDone"));
            if (start != null && batch["rule"] is ITreeAttribute rule)
                _rule = new TubRuleConfig(rule.GetString("liquid"), rule.GetString("input"), rule.GetString("output"), rule.GetDouble("hours"),
                    rule.GetDouble("graceHours"), rule.GetDouble("lossEveryHours"),
                    Enum.TryParse<TubRuleKind>(rule.GetString("kind"), out var kind) ? kind : TubRuleKind.Pickle)
                {
                    Failure = rule.GetString("failure"),
                    FailureQuantity = rule.GetInt("failureQuantity", 1),
                    LossChance = rule.GetDouble("lossChance"),
                };
            // A batch saved running without its rule (should not happen) waits again.
            if (_batch.Started && _rule == null)
                _batch = _batch with { StartHours = null };
        }
        if (Api is ICoreClientAPI)
        {
            BuildMeshes();
            MarkDirty(true);
        }
    }

    // ---- Block info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (TotalLitres > 0 && LiquidCode != null)
            dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-liquid", TotalLitres.ToString("0.#", CultureInfo.InvariantCulture),
                Config.CapacityLitres.ToString("0.#", CultureInfo.InvariantCulture), LiquidName(Lang.CurrentLocale)));
        else
            dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-noliquid"));
        if (_batch == null)
        {
            dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-nogears", Config.BatchSize));
            return;
        }
        var stage = Stage!.Value;
        string Name(string code) => ItemName(code);
        switch (stage.Phase)
        {
            case TubPhase.Waiting:
                dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-waiting", _batch.Count, Name(_batch.Input)));
                break;
            case TubPhase.Soaking:
                dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-{(_rule!.Kind == TubRuleKind.Rust ? "rusting" : "pickling")}",
                    _batch.Count, Name(_batch.Input), (int)Math.Floor(stage.Progress * 100)));
                break;
            case TubPhase.Done:
                dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-{(_rule!.Kind == TubRuleKind.Rust ? "rusted" : "pickled")}",
                    stage.Outputs, Name(_rule.Output)));
                if (stage.Lost > 0)
                    dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-overrusted", stage.Lost));
                break;
            case TubPhase.Eating:
                dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-eating", stage.Outputs, Name(_rule!.Output), stage.Lost));
                break;
            case TubPhase.Dissolved:
                dsc.AppendLine(Lang.Get($"{Domain}:picklingtub-info-dissolved"));
                break;
        }
    }

    private string ItemName(string code)
    {
        var location = new AssetLocation(code);
        CollectibleObject? collectible = Api.World.GetItem(location);
        collectible ??= Api.World.GetBlock(location) is { Id: > 0 } block ? block : null;
        return collectible == null ? code : collectible.GetHeldItemName(new ItemStack(collectible));
    }

    // ---- Drawing (client) ----

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (_liquidMesh != null)
            mesher.AddMeshData(_liquidMesh);
        if (_gearsMesh != null)
            mesher.AddMeshData(_gearsMesh);
        return false;
    }

    // On the main thread (Initialize, FromTreeAttributes): textures may be added to the atlas here,
    // not on the tesselation thread.
    private void BuildMeshes()
    {
        if (Api is not ICoreClientAPI capi)
            return;
        try
        {
            _liquidMesh = LiquidMesh(capi);
            _gearsMesh = GearsMesh(capi);
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[seraphhorizons] Pickling tub: could not draw its contents: {0}", e.Message);
            _liquidMesh = _gearsMesh = null;
        }
    }

    private MeshData? LiquidMesh(ICoreClientAPI capi)
    {
        string? code = LiquidCode;
        double litres = TotalLitres;
        if (code == null || litres <= 0)
            return null;
        var stack = _liquid ?? (Api.World.GetItem(new AssetLocation(code)) is { } item ? new ItemStack(item) : null);
        var props = stack == null ? null : BlockLiquidContainerBase.GetContainableProps(stack);
        if (stack == null || props?.Texture == null)
            return null;
        var shape = Shape.TryGet(capi, new AssetLocation(Domain, "shapes/block/picklingtub-liquid.json"));
        if (shape == null)
            return null;
        var source = new ContainerTextureSource(capi, stack, props.Texture);
        capi.Tesselator.TesselateShape("picklingtub-liquid", shape, out var mesh, source);
        float level = FloorY + (BrimY - FloorY) * (float)Math.Clamp(litres / Config.CapacityLitres, 0.04, 1);
        mesh.Translate(0, level, 0);
        return mesh;
    }

    // Up to the batch size of gears lying flat in layers of four, the input before done, then the
    // output, and the failure item for the lost ones.
    private MeshData? GearsMesh(ICoreClientAPI capi)
    {
        if (_batch == null || Stage is not { } stage)
            return null;
        var codes = new List<string>();
        codes.AddRange(Enumerable.Repeat(_batch.Input, stage.Inputs));
        if (_rule != null)
        {
            codes.AddRange(Enumerable.Repeat(_rule.Output, stage.Outputs));
            codes.AddRange(Enumerable.Repeat(_rule.Failure, stage.Lost));
        }
        MeshData? pile = null;
        for (int i = 0; i < codes.Count && i < 16; i++)
        {
            var location = new AssetLocation(codes[i]);
            if (Api.World.GetItem(location) is not { } item)
                continue;
            var one = ItemMeshes.Get(capi, item);
            if (one == null)
                continue;
            int layer = i / 4, spot = i % 4;
            float x = spot % 2 == 0 ? -0.17f : 0.17f, z = spot < 2 ? -0.17f : 0.17f;
            one = one.Clone()
                .Scale(new Vec3f(0.5f, 0, 0.5f), 0.55f, 0.55f, 0.55f)
                .Rotate(new Vec3f(0.5f, 0, 0.5f), 0, (float)(((i * 47) % 360) * Math.PI / 180), 0)
                .Translate(x, FloorY + layer * 0.06f, z);
            if (pile == null)
                pile = one;
            else
                pile.AddMeshData(one);
        }
        return pile;
    }
}
