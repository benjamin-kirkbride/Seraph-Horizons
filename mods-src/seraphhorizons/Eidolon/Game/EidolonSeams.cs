using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// An upkeep that can stop the eidolon working: charge (<see cref="EntityBehaviorEidolonCharge"/>),
/// and later oil. Implemented by an entity behaviour; <see cref="EntityLaborEidolon"/> asks every behaviour
/// that implements it each check, on the server, and the worst answer counts
/// (<see cref="EidolonStop.First"/>). Damage is the entity's own (<see cref="EidolonStop.Damaged"/>).
/// </summary>
public interface IEidolonUpkeep
{
    /// <summary>Why it cannot work now, or null when this upkeep is fine.</summary>
    EidolonStop? Stop { get; }

    /// <summary>Whether it is draining now: false while the eidolon is slumped. Called by the entity
    /// before each check; an upkeep that drains with time keeps its clock in step with it.</summary>
    void OnCheck(EntityLaborEidolon eidolon, bool running);
}

/// <summary>
/// One order an eidolon carries out (Eidolon/README.md, "Orders"), run by its order task
/// (<see cref="AiTaskEidolonOrder"/>) while it can work. Made from the order's code and arguments by
/// the factory registered with <see cref="EidolonOrders.Register"/>; one instance per eidolon and
/// order, so it may keep state between <see cref="Start"/> and <see cref="Stop"/>. An order is
/// interrupted (stopped, cancelled) when the eidolon slumps or a higher task (self-defence) takes
/// over, and started again after.
/// </summary>
public interface IEidolonOrder
{
    /// <summary>The order's code, as registered.</summary>
    string Code { get; }

    void Start(EntityLaborEidolon eidolon);

    /// <summary>One server tick of the order; false when it is done, which clears it.</summary>
    bool Continue(EntityLaborEidolon eidolon, float dt);

    /// <summary>The order stops running: done, replaced, or interrupted (<paramref name="cancelled"/>).</summary>
    void Stop(EntityLaborEidolon eidolon, bool cancelled);
}

/// <summary>The order factories by code. <c>stay</c> and <c>goto</c> are built in
/// (<see cref="EidolonSystem"/>); later tasks add theirs (follow, carry, fell, haul, crew, guard).</summary>
public static class EidolonOrders
{
    private static readonly Dictionary<string, Func<EntityLaborEidolon, ITreeAttribute, IEidolonOrder>> Factories = new();

    public static void Register(string code, Func<EntityLaborEidolon, ITreeAttribute, IEidolonOrder> create) => Factories[code] = create;

    public static bool Exists(string code) => Factories.ContainsKey(code);

    public static IEidolonOrder? Create(EntityLaborEidolon eidolon, string code, ITreeAttribute args) =>
        Factories.TryGetValue(code, out var create) ? create(eidolon, args) : null;
}

/// <summary>
/// What the eidolon holds sets how it walks and stands (#676 on): an entity behaviour implementing
/// this, while it holds something, names the animation it walks (and runs) with in place of the
/// game's <c>walk</c> and <c>run</c> (<see cref="EntityLaborEidolon.MoveAnimation"/>, used by
/// <see cref="EidolonNavigator"/>): <c>carry-walk</c> with a block, <c>trunk-carry-walk</c> with a
/// trunk. The first behaviour that answers counts.
/// </summary>
public interface IEidolonStance
{
    /// <summary>The animation code to move with, or null when this behaviour holds nothing.</summary>
    string? MoveAnimation(bool run);
}
