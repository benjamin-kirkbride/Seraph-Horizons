using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The player-built eidolon (Eidolon/README.md): a laborer automaton, 3.75 blocks tall. It is never
/// killed: at 0 HP it slumps disabled (<see cref="EidolonStop.Damaged"/>) until repaired to
/// <see cref="EidolonConfig.StandUpHealthShare"/> of its health, and out of charge it slumps until
/// recharged; nothing drops and no corpse is left. Its owner (<see cref="OwnerUid"/>) and their company
/// command it (<see cref="MayCommand"/>).
///
/// <para>The seams for what comes later: upkeeps are entity behaviours implementing
/// <see cref="IEidolonUpkeep"/> (charge now, oil later), checked together here into <see cref="Stop"/>
/// and <see cref="CanWork"/>; right-clicks reach each behaviour's <c>OnInteract</c> in the order the
/// entity type lists them (the game's dispatch), and a behaviour that commands checks
/// <see cref="MayCommand"/> or <see cref="RefuseUnlessCommander"/>; orders run through
/// <see cref="EntityBehaviorEidolonOrders"/> and <see cref="AiTaskEidolonOrder"/>; while it cannot
/// work, no AI task starts and those running are stopped.</para>
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

    /// <summary>The lengths of the shape's one-shots, at 30 frames a second (Eidolon/README.md).</summary>
    public const double StandUpSeconds = 60 / 30.0;
    public const double ActivateSeconds = 90 / 30.0;

    private const float CheckSeconds = 0.25f;

    private float _sinceCheck = CheckSeconds;
    private bool _workingWhenChecked;

    /// <summary>Slumping and standing up (server side).</summary>
    public EidolonPose Pose { get; } = new();

    /// <summary>Why it cannot work, or null (server side; the client reads <see cref="StopKey"/>).</summary>
    public EidolonStop? Stop { get; private set; }

    /// <summary>Whether it may work now: on its feet, no upkeep stopping it (server side).</summary>
    public bool CanWork => Alive && Pose.CanAct && Stop == null;

    public string? OwnerUid => WatchedAttributes.GetString(OwnerUidKey) is { Length: > 0 } uid ? uid : null;

    public string? OwnerName => WatchedAttributes.GetString(OwnerNameKey);

    public EntityBehaviorTaskAI? TaskAi => GetBehavior<EntityBehaviorTaskAI>();

    public EntityBehaviorEidolonOrders? Orders => GetBehavior<EntityBehaviorEidolonOrders>();

    private EidolonConfig Settings => EidolonSystem.Of(Api)?.Config ?? EidolonConfig.Defaults;

    public override bool IsInteractable => true;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);
        if (api.Side == EnumAppSide.Server && TaskAi is { } ai)
            ai.TaskManager.OnShouldExecuteTask += _ => CanWork;
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
        _sinceCheck += dt;
        if (_sinceCheck >= CheckSeconds)
        {
            _sinceCheck = 0;
            Check();
        }
    }

    /// <summary>Weighs its upkeeps and health into <see cref="Stop"/>, slumps or stands up, and stops
    /// its work when it can no longer work. Server side; runs four times a second, and at once after
    /// anything that changes them (damage, a recharge).</summary>
    public void Check()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        bool running = Pose.State != EidolonPoseState.Slumped;
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
        switch (Pose.Update(stop?.Slumps == true, World.ElapsedMilliseconds / 1000.0, StandUpSeconds))
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
        return WatchedAttributes.GetBool(DownedKey) ? EidolonStop.Damaged : null;
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

    // Down at 0 HP it takes no more harm (nothing to lose), but heals.
    public override bool ShouldReceiveDamage(DamageSource damageSource, float damage) =>
        base.ShouldReceiveDamage(damageSource, damage)
        && (damageSource.Type == EnumDamageType.Heal || !WatchedAttributes.GetBool(DownedKey));

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
        return text.ToString();
    }
}
