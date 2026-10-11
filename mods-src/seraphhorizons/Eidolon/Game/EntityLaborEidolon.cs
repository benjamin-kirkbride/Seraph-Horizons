using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The player-built eidolon (Eidolon/README.md): a laborer automaton, 3.75 blocks tall. It is never
/// killed: at 0 HP it slumps disabled (<see cref="EidolonStop.Damaged"/>) until repaired to
/// <see cref="EidolonConfig.StandUpHealthShare"/> of its health, and out of charge it slumps until
/// recharged; nothing drops and no corpse is left. It never heals by itself (<see cref="RegenSpeedKey"/>):
/// only repair restores health. Down, creatures lose it (<see cref="EidolonDownedTargeting"/>). Its owner (<see cref="OwnerUid"/>) and their company
/// command it (<see cref="MayCommand"/>).
///
/// <para>The seams for what comes later: upkeeps are entity behaviours implementing
/// <see cref="IEidolonUpkeep"/> (charge, oil), checked together here into <see cref="Stop"/>
/// and <see cref="CanWork"/>; right-clicks reach each behaviour's <c>OnInteract</c> in the order the
/// entity type lists them (the game's dispatch), and a behaviour that commands checks
/// <see cref="MayCommand"/> or <see cref="RefuseUnlessCommander"/>; orders run through
/// <see cref="EntityBehaviorEidolonOrders"/> and <see cref="AiTaskEidolonOrder"/>; while it cannot
/// work, no AI task starts and those running are stopped.</para>
///
/// <para>It is its fittings (<see cref="Build"/>, Eidolon/README.md, "Fittings"): its layers'
/// materials, in its watched attributes, give its figures (<see cref="FittedStats"/>, from
/// <see cref="EidolonStats.Of"/>): its most health, its walking and running speed, its blows and its
/// plates' armour; and its renderer draws each layer in its material
/// (<see cref="EidolonShapeRenderer"/>). Changing the record (<see cref="Build"/>'s setter, or the
/// attribute written another way) applies at once.</para>
/// </summary>
public class EntityLaborEidolon : EntityAgent
{
    public const string OwnerUidKey = "seraphhorizons:ownerUid";
    public const string OwnerNameKey = "seraphhorizons:ownerName";
    /// <summary>The stop's code while it cannot work (watched, for the client's info).</summary>
    public const string StopKey = "seraphhorizons:stop";
    /// <summary>Whether it is slumped (watched; on load it slumps again at once).</summary>
    public const string SlumpedKey = "seraphhorizons:slumped";
    /// <summary>Set at 0 HP, cleared once repaired to <see cref="EidolonConfig.StandUpHealthShare"/>.</summary>
    public const string DownedKey = "seraphhorizons:downed";
    /// <summary>Its figures as the server worked them out (watched, for the client's info): the blows'
    /// damage, the move speed's multiplier and the armour.</summary>
    public const string StatsKey = "seraphhorizons:eidolonStats";
    /// <summary>The game's health behaviour heals any creature over time at this watched rate (default
    /// 1); held at 0, so only repair restores its health.</summary>
    public const string RegenSpeedKey = "regenSpeed";

    /// <summary>The lengths of the shape's one-shots, at 30 frames a second (Eidolon/README.md).</summary>
    public const double StandUpSeconds = 60 / 30.0;
    public const double ActivateSeconds = 90 / 30.0;

    private const float CheckSeconds = 0.25f;

    /// <summary>Checks between letting creatures go of it again while it is down (once a second).</summary>
    private const int DropTargetsEveryChecks = 4;

    private float _sinceCheck = CheckSeconds;
    private int _downedChecks;
    private bool _workingWhenChecked;
    private EidolonBuild? _appliedBuild;
    private EidolonStats? _stats;

    /// <summary>Slumping and standing up (server side).</summary>
    public EidolonPose Pose { get; } = new();

    /// <summary>Why it cannot work, or null (server side; the client reads <see cref="StopKey"/>).</summary>
    public EidolonStop? Stop { get; private set; }

    /// <summary>Whether it may work now: on its feet, no upkeep stopping it, not docking or docked in a
    /// gantry (server side).</summary>
    public bool CanWork => Alive && Pose.CanAct && Stop == null && Dock?.Holds != true;

    /// <summary>Whether it is down at 0 HP, until repaired to <see cref="EidolonConfig.StandUpHealthShare"/>
    /// (watched, so either side).</summary>
    public bool Downed => WatchedAttributes.GetBool(DownedKey);

    public string? OwnerUid => WatchedAttributes.GetString(OwnerUidKey) is { Length: > 0 } uid ? uid : null;

    public string? OwnerName => WatchedAttributes.GetString(OwnerNameKey);

    public EntityBehaviorTaskAI? TaskAi => GetBehavior<EntityBehaviorTaskAI>();

    public EntityBehaviorEidolonOrders? Orders => GetBehavior<EntityBehaviorEidolonOrders>();

    /// <summary>Its dock on a gantry's spine (Eidolon/README.md, "Docking").</summary>
    public EntityBehaviorEidolonDock? Dock => GetBehavior<EntityBehaviorEidolonDock>();

    private EidolonConfig Settings => EidolonSystem.Of(Api)?.Config ?? EidolonConfig.Defaults;

    /// <summary>Its fittings: the record under <see cref="EidolonBuild.AttributeKey"/> in its watched
    /// attributes, or with none (an eidolon from before fittings) <see cref="EidolonBuild.OldBuild"/>.
    /// Set on the server; its figures and look follow.</summary>
    public EidolonBuild Build
    {
        get => ReadBuild(WatchedAttributes);
        set => WriteBuild(WatchedAttributes, value);
    }

    /// <summary>The build kept in <paramref name="attributes"/> (<see cref="EidolonBuild.FromAttributes"/>).</summary>
    public static EidolonBuild ReadBuild(ITreeAttribute attributes)
    {
        if (attributes.GetTreeAttribute(EidolonBuild.AttributeKey) is not { } tree)
            return EidolonBuild.OldBuild;
        return EidolonBuild.FromAttributes(EidolonFittings.Layers.Select(EidolonFittings.TextureCode)
            .ToDictionary(code => code, code => (string?)tree.GetString(code)));
    }

    /// <summary>Writes <paramref name="build"/> into <paramref name="attributes"/> (a synced tree marks it
    /// dirty, so it reaches the clients and its listeners run).</summary>
    public static void WriteBuild(ITreeAttribute attributes, EidolonBuild build)
    {
        var tree = new TreeAttribute();
        foreach (var (code, material) in build.ToAttributes())
            tree.SetString(code, material);
        attributes[EidolonBuild.AttributeKey] = tree;
        (attributes as SyncedTreeAttribute)?.MarkPathDirty(EidolonBuild.AttributeKey);
    }

    /// <summary>Its figures from its fittings and the server's settings (server side; worked out again
    /// whenever <see cref="Build"/> changes).</summary>
    public EidolonStats FittedStats => _stats ??= EidolonStats.Of(Build, Settings);

    /// <summary>Its oil (null without the behaviour; its <c>Tank</c> null with MachineOil off).</summary>
    public EntityBehaviorEidolonOil? Oil => GetBehavior<EntityBehaviorEidolonOil>();

    /// <summary>A job done: drains its oil by the job's cost (EidolonSettings), and stops it at once if
    /// that leaves it dry. Job orders call this as each job finishes (server side).</summary>
    public void SpendOil(EidolonJob job) => Oil?.Spend(job);

    /// <summary>The animation it walks (or runs) with: what a behaviour holding something says
    /// (<see cref="IEidolonStance"/>, a carried block's <c>carry-walk</c>), else <c>walk</c> or <c>run</c>.</summary>
    public string MoveAnimation(bool run) =>
        SidedProperties.Behaviors.OfType<IEidolonStance>().Select(s => s.MoveAnimation(run)).FirstOrDefault(a => a != null)
        ?? (run ? "run" : "walk");

    public override bool IsInteractable => true;

    /// <summary>Its right hand: the axe it fells with (<see cref="EntityBehaviorEidolonAxe"/>, #677),
    /// which the game's shape renderer draws at the shape's <c>RightHand</c> point.</summary>
    public override ItemSlot? RightHandItemSlot
    {
        get => GetBehavior<EntityBehaviorEidolonAxe>()?.Slot;
        set { }
    }

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);
        if (api.Side == EnumAppSide.Server && TaskAi is { } ai)
            ai.TaskManager.OnShouldExecuteTask += _ => CanWork;
        if (api.Side == EnumAppSide.Server)
        {
            // Set on every load, so eidolons saved before this stop healing too.
            WatchedAttributes.SetFloat(RegenSpeedKey, 0);
            if (GetBehavior<EntityBehaviorHealth>() is { } health)
                health.onDamaged += Armour;
            ApplyFittings();
        }
        WatchedAttributes.RegisterModifiedListener(EidolonBuild.AttributeKey, OnFittingsChanged);
    }

    private void OnFittingsChanged()
    {
        if (Api.Side == EnumAppSide.Server)
            ApplyFittings();
        else
            MarkShapeModified();
    }

    /// <summary>Works its figures out again from <see cref="Build"/> when it has changed since they last
    /// were: its most health (keeping the share of it it has, <see cref="EidolonStats.KeepShare"/>), and
    /// the figures the client shows (<see cref="StatsKey"/>). The speeds, blows and armour are read
    /// from <see cref="FittedStats"/> where they are used. Server side.</summary>
    public void ApplyFittings()
    {
        if (Api?.Side != EnumAppSide.Server)
            return;
        var build = Build;
        if (build.Equals(_appliedBuild))
            return;
        _appliedBuild = build;
        var stats = _stats = EidolonStats.Of(build, Settings);
        if (GetBehavior<EntityBehaviorHealth>() is { } health && Math.Abs(health.BaseMaxHealth - stats.MaxHealth) > 0.01)
        {
            float was = health.Health, wasMax = health.MaxHealth;
            health.BaseMaxHealth = (float)stats.MaxHealth;
            health.MarkDirty();
            health.Health = (float)EidolonStats.KeepShare(was, wasMax, health.MaxHealth);
        }
        var shown = new TreeAttribute();
        shown.SetFloat("defence", stats.DefenceDamage);
        shown.SetFloat("slam", stats.SlamDamage);
        shown.SetFloat("speed", Settings.WalkSpeed > 0 ? stats.WalkSpeed / Settings.WalkSpeed : 1);
        if (stats.Armour is { } armour)
        {
            shown.SetInt("armourTier", armour.ProtectionTier);
            shown.SetFloat("armourRelative", (float)armour.RelativeProtection);
            shown.SetFloat("armourFlat", (float)armour.FlatDamageReduction);
        }
        WatchedAttributes[StatsKey] = shown;
        WatchedAttributes.MarkPathDirty(StatsKey);
    }

    /// <summary>The plates' armour on a hit (the health behaviour's damage delegate, as the game's
    /// worn armour is on a player): a blunt, piercing or slashing attack is reduced as a full suit of
    /// the plates' metal would reduce it (<see cref="EidolonArmour"/>); anything else passes.</summary>
    private float Armour(float damage, DamageSource source)
    {
        if (!EidolonArmour.Applies(source.Type.ToString(), source.Source.ToString()))
            return damage;
        return FittedStats.Reduce(damage, source.DamageTier, Settings.ArmourLoss);
    }

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        if (Api.Side != EnumAppSide.Server)
            return;
        // From the creative spawner (ItemCreature with setGuardedEntityAttribute): the player who placed it.
        if (OwnerUid == null && WatchedAttributes.GetString("guardedPlayerUid") is { Length: > 0 } uid)
            SetOwner(uid, Api.World.PlayerByUid(uid)?.PlayerName);
        WatchedAttributes.RemoveAttribute("guardedPlayerUid");
        WatchedAttributes.RemoveAttribute("guardedEntityId");
    }

    /// <summary>Makes <paramref name="uid"/> the owner (null: none).</summary>
    public void SetOwner(string? uid, string? name)
    {
        if (string.IsNullOrEmpty(uid))
        {
            WatchedAttributes.RemoveAttribute(OwnerUidKey);
            WatchedAttributes.RemoveAttribute(OwnerNameKey);
        }
        else
        {
            WatchedAttributes.SetString(OwnerUidKey, uid);
            WatchedAttributes.SetString(OwnerNameKey, name ?? uid);
        }
    }

    /// <summary>Whether <paramref name="player"/> may command it or open what it carries: the owner and
    /// their company (<see cref="EidolonOwnership"/>). Server side (companies are the server's).</summary>
    public bool MayCommand(IPlayer player) => EidolonSystem.Of(Api)?.MayCommand(OwnerUid, player.PlayerUID) ?? OwnerUid == null || OwnerUid == player.PlayerUID;

    /// <summary><see cref="MayCommand"/>, telling a player who may not why (server side). For the
    /// command tool and the container.</summary>
    public bool RefuseUnlessCommander(IPlayer player)
    {
        if (MayCommand(player))
            return true;
        (player as IServerPlayer)?.SendIngameError("seraphhorizons-eidolon-notyours",
            Lang.GetL(((IServerPlayer)player).LanguageCode, "seraphhorizons:eidolon-notyours", OwnerName ?? "?"));
        return false;
    }

    /// <summary>Plays <c>activate</c>, waking (the gantry's last stage, #672): it cannot work until it
    /// has played. Server side.</summary>
    public void Activate()
    {
        Pose.Rise(Api.World.ElapsedMilliseconds / 1000.0 + ActivateSeconds);
        AnimManager.StopAnimation("hung");
        AnimManager.StartAnimation("activate");
        World.PlaySoundAt(new AssetLocation("game:sounds/creature/eidolon/awaken"), this, null, true, 32);
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (Api.Side != EnumAppSide.Server || !Alive)
            return;
        KeepApart();
        _sinceCheck += dt;
        if (_sinceCheck >= CheckSeconds)
        {
            _sinceCheck = 0;
            Check();
        }
    }

    /// <summary>Pushes itself away from any other eidolon it overlaps (<see cref="EidolonSpacing"/>), a
    /// docked one too, which stays put (<see cref="EntityBehaviorEidolonDock"/>):
    /// <c>repulseagents</c> is <c>movable: false</c>, so nothing else parts two. Server side, every tick;
    /// not while either is mounted (a gantry holds it in place).</summary>
    private void KeepApart()
    {
        if (MountedOn != null || Dock?.Holds == true || CollisionBox is not { } own)
            return;
        double reach = own.XSize;
        foreach (var e in World.GetEntitiesAround(Pos.XYZ, (float)reach, (float)own.YSize,
                     e => e is EntityLaborEidolon { Alive: true, MountedOn: null } && e != this))
        {
            if (e.Pos.Dimension != Pos.Dimension)
                continue;
            var (mx, mz) = EidolonSpacing.Push(e.Pos.X - Pos.X, e.Pos.Z - Pos.Z, (own.XSize + e.CollisionBox.XSize) / 2,
                EntityId, e.EntityId);
            Pos.Motion.X += mx;
            Pos.Motion.Z += mz;
        }
    }

    /// <summary>Weighs its upkeeps and health into <see cref="Stop"/>, slumps or stands up, and stops
    /// its work when it can no longer work. Server side; runs four times a second, and at once after
    /// anything that changes them (damage, a recharge).</summary>
    public void Check()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        ApplyFittings();
        // docked it is switched off: its upkeeps do not run down, and it hangs rather than slumps
        bool docked = Dock?.Holds == true;
        bool running = Pose.State != EidolonPoseState.Slumped && !docked;
        var upkeeps = SidedProperties.Behaviors.OfType<IEidolonUpkeep>().ToList();
        foreach (var upkeep in upkeeps)
            upkeep.OnCheck(this, running);
        var stop = EidolonStop.First(upkeeps.Select(u => u.Stop).Prepend(DamageStop()));
        if (!Equals(stop, Stop))
        {
            Stop = stop;
            if (stop == null)
                WatchedAttributes.RemoveAttribute(StopKey);
            else
                WatchedAttributes.SetString(StopKey, stop.Code);
        }
        switch (docked ? EidolonPoseAction.None : Pose.Update(stop?.Slumps == true, World.ElapsedMilliseconds / 1000.0, StandUpSeconds))
        {
            case EidolonPoseAction.Slump:
                AnimManager.StopAnimation("standup");
                AnimManager.StopAnimation("activate");
                AnimManager.StartAnimation("slump");
                WatchedAttributes.SetBool(SlumpedKey, true);
                World.PlaySoundAt(new AssetLocation("game:sounds/creature/eidolon/death"), this, null, true, 32, 0.6f);
                break;
            case EidolonPoseAction.StandUp:
                AnimManager.StartAnimation("standup");
                AnimManager.StopAnimation("slump");
                WatchedAttributes.SetBool(SlumpedKey, false);
                World.PlaySoundAt(new AssetLocation("game:sounds/creature/eidolon/awake"), this, null, true, 32);
                break;
        }
        bool working = CanWork;
        if (_workingWhenChecked && !working)
            StopWork();
        _workingWhenChecked = working;
        // Down, whatever goes for it is stopped: at once, then once a second for anything that took it
        // up before it went down (a pack told of it) or came in range already set on it.
        if (Downed)
        {
            if (_downedChecks++ % DropTargetsEveryChecks == 0)
                EidolonDownedTargeting.DropTargetsOf(this);
        }
        else
            _downedChecks = 0;
    }

    /// <summary>Stops every AI task and its walking (it slumps, or an upkeep stops it).</summary>
    public void StopWork()
    {
        if (TaskAi is { } ai)
        {
            ai.TaskManager.StopTasks();
            ai.PathTraverser?.Stop();
        }
        Controls.StopAllMovement();
        ServerControls.StopAllMovement();
    }

    private EidolonStop? DamageStop()
    {
        if (GetBehavior<EntityBehaviorHealth>() is not { } health)
            return null;
        if (health.Health <= 0)
            WatchedAttributes.SetBool(DownedKey, true);
        else if (WatchedAttributes.GetBool(DownedKey) && health.Health >= health.MaxHealth * Settings.StandUpHealthShare)
            WatchedAttributes.RemoveAttribute(DownedKey);
        return Downed ? EidolonStop.Damaged : null;
    }

    // At 0 HP the health behaviour calls Die(Death); lava calls Die(Combusted). Neither kills it: it
    // slumps where it is, keeping everything. Any other reason (removed, unloaded) passes.
    public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource? damageSourceForDeath = null)
    {
        if (reason is EnumDespawnReason.Death or EnumDespawnReason.Combusted)
        {
            if (Api.Side != EnumAppSide.Server)
                return;
            if (GetBehavior<EntityBehaviorHealth>() is { } health)
                health.Health = 0;
            Check();
            return;
        }
        base.Die(reason, damageSourceForDeath);
    }

    // Down at 0 HP it takes no more harm (nothing to lose), but is repaired.
    public override bool ShouldReceiveDamage(DamageSource damageSource, float damage) =>
        base.ShouldReceiveDamage(damageSource, damage)
        && (damageSource.Type == EnumDamageType.Heal || !Downed);

    public override bool ReceiveDamage(DamageSource damageSource, float damage)
    {
        bool took = base.ReceiveDamage(damageSource, damage);
        if (took && Api.Side == EnumAppSide.Server)
            Check();
        return took;
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        _sinceCheck = CheckSeconds;
    }

    public override string GetInfoText()
    {
        var text = new StringBuilder(base.GetInfoText());
        text.AppendLine(OwnerUid == null
            ? Lang.Get("seraphhorizons:eidolon-info-noowner")
            : Lang.Get("seraphhorizons:eidolon-info-owner", OwnerName ?? "?"));
        if (WatchedAttributes.GetString(StopKey) is { Length: > 0 } stop)
            text.AppendLine(Lang.Get("seraphhorizons:eidolon-stop-" + stop));
        AppendFittings(text);
        return text.ToString();
    }

    /// <summary>Its fittings, then the figures the server sent (<see cref="StatsKey"/>).</summary>
    private void AppendFittings(StringBuilder text)
    {
        var layers = EidolonFittingText.Layers(Build)
            .Select(l => Lang.Get("seraphhorizons:eidolon-info-layer", Get(l.Material), Lang.Get(l.Layer)));
        text.AppendLine(Lang.Get("seraphhorizons:eidolon-info-fittings", string.Join(", ", layers)));
        if (WatchedAttributes.GetTreeAttribute(StatsKey) is not { } stats)
            return;
        text.AppendLine(stats.HasAttribute("armourTier")
            ? Lang.Get("seraphhorizons:eidolon-info-armour", stats.GetInt("armourTier"),
                stats.GetFloat("armourRelative") * 100, stats.GetFloat("armourFlat"))
            : Lang.Get("seraphhorizons:eidolon-info-noarmour"));
        text.AppendLine(Lang.Get("seraphhorizons:eidolon-info-blows", stats.GetFloat("defence"), stats.GetFloat("slam"),
            stats.GetFloat("speed", 1) * 100));

        static string Get(FittingName t) => Lang.Get(t.Key, t.ArgKeys.Select(k => (object)Lang.Get(k)).ToArray());
    }
}
