namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>An eidolon's figures as its fittings make them (<see cref="EidolonStats.Of"/>).</summary>
/// <param name="MaxHealth">Its most health.</param>
/// <param name="WalkSpeed">Walking speed, the creature scale of <see cref="EidolonConfig.WalkSpeed"/>.</param>
/// <param name="RunSpeed">Running speed, the same scale.</param>
/// <param name="DefenceDamage">Each punch's or kick's damage.</param>
/// <param name="SlamDamage">Each slam's damage.</param>
/// <param name="Armour">Its plates' armour, or null with no plates on.</param>
public sealed record EidolonStats(
    double MaxHealth, float WalkSpeed, float RunSpeed, float DefenceDamage, float SlamDamage, PlateArmour? Armour)
{
    /// <summary>
    /// The figures for <paramref name="build"/>: health is the base (<see cref="BaseHealth"/>) times
    /// the plates', joints' and skirt's health multipliers; the move speeds and the blows' damage are
    /// the settings' times the plates' multipliers; the armour is the plates' metal's. A layer that
    /// is off counts as iron (every multiplier 1) and, for the plates, as no armour. Bones and sash
    /// change nothing.
    /// </summary>
    public static EidolonStats Of(EidolonBuild build, EidolonConfig settings)
    {
        string? plates = build[FittingLayer.Plates];
        double health = BaseHealth(settings)
                        * settings.PlateHealth.For(plates)
                        * settings.JointHealth.For(build[FittingLayer.Joints])
                        * settings.SkirtHealth.For(build[FittingLayer.Skirt]);
        float speed = (float)settings.PlateMoveSpeed.For(plates);
        float damage = (float)settings.PlateDamage.For(plates);
        return new EidolonStats(
            health,
            settings.WalkSpeed * speed,
            settings.RunSpeed * speed,
            settings.DefenceDamage * damage,
            settings.SlamDamage * damage,
            settings.PlateArmour.For(plates));
    }

    /// <summary>The health an all-iron eidolon has: <see cref="EidolonConfig.AllSteelHealth"/> over the
    /// steel plates', joints' and skirt's multipliers, so an all-steel one has exactly that (300 /
    /// (1.5 × 1.5 × 1.25) ≈ 106.7 by default).</summary>
    public static double BaseHealth(EidolonConfig settings) =>
        settings.AllSteelHealth
        / (settings.PlateHealth.Steel * settings.JointHealth.Steel * settings.SkirtHealth.Steel);

    /// <summary>The health an eidolon at <paramref name="health"/> of <paramref name="oldMax"/> has when
    /// its most becomes <paramref name="newMax"/>: the same share of it (a layer swapped on a docked
    /// eidolon neither heals nor hurts it). With no old most to take a share of, full.</summary>
    public static double KeepShare(double health, double oldMax, double newMax)
    {
        if (!(oldMax > 0) || !double.IsFinite(health))
            return newMax;
        return Math.Clamp(health / oldMax, 0, 1) * newMax;
    }

    /// <summary>A hit of <paramref name="damage"/> at <paramref name="damageTier"/> as this eidolon's
    /// armour leaves it (<see cref="EidolonArmour.Reduce"/>); unchanged with no plates.</summary>
    public float Reduce(float damage, int damageTier, ArmourTierLoss loss) =>
        Armour == null ? damage : EidolonArmour.Reduce(damage, damageTier, Armour, loss);
}

/// <summary>
/// The game's armour maths for a player wearing a full suit of one armour, applied to the eidolon.
///
/// Source: Vintage Story 1.22.7, <c>VSSurvivalMod.dll</c>,
/// <c>Vintagestory.GameContent.ModSystemWearableStats.handleDamaged</c> (decompiled with ilspycmd),
/// and the plate armour's figures in <c>assets/survival/itemtypes/wearable/seraph/armor.json</c>
/// (<c>protectionModifiersByType</c>, <c>defaultProtLoss</c>, read into
/// <c>ItemWearable.ProtectionModifiers</c>). The game, on the server:
/// <list type="number">
/// <item>leaves the damage alone unless its type is a blunt, piercing or slashing attack
///   (<see cref="ArmouredDamageTypes"/>) and its source is neither internal nor suicide;</item>
/// <item>picks one armour slot by chance (head 20 %, body 50 %, legs 30 %) and, when it holds armour
///   with durability left, uses that piece's modifiers (a full suit of one metal: always the same
///   ones, so the pick does not matter);</item>
/// <item>starts from the piece's flat reduction F and relative protection R, and for each tier
///   i = 1 .. the attack's damage tier: past the piece's protection tier (i &gt; tier) takes the
///   "above" losses, else the "within" ones (each halved when past it and the piece is
///   <c>HighDamageTierResistant</c>); F -= flat loss; R *= 1 − relative loss;</item>
/// <item>damage = max(0, damage − F); damage *= 1 − max(0, R). (F is not clamped: a hit far above
///   the armour's tier can come out larger than it went in, as the game has it.)</item>
/// </list>
/// The game also wears the piece's durability; the eidolon's plates do not wear. The shield step
/// before it (<c>applyShieldProtection</c>) is the held shield's, which the eidolon has none of.
/// </summary>
public static class EidolonArmour
{
    /// <summary>The damage types armour acts on, by the game's <c>EnumDamageType</c> names.</summary>
    public static readonly IReadOnlySet<string> ArmouredDamageTypes =
        new HashSet<string>(StringComparer.Ordinal) { "BluntAttack", "PiercingAttack", "SlashingAttack" };

    /// <summary>The damage sources armour never acts on, by the game's <c>EnumDamageSource</c> names.</summary>
    public static readonly IReadOnlySet<string> UnarmouredSources =
        new HashSet<string>(StringComparer.Ordinal) { "Internal", "Suicide" };

    /// <summary>Whether armour acts on a hit of this type from this source (both the game's enum names).</summary>
    public static bool Applies(string damageType, string source) =>
        ArmouredDamageTypes.Contains(damageType) && !UnarmouredSources.Contains(source);

    /// <summary>The flat reduction and relative protection <paramref name="armour"/> has left against
    /// an attack of <paramref name="damageTier"/>.</summary>
    public static (double Flat, double Relative) Effective(int damageTier, PlateArmour armour, ArmourTierLoss loss)
    {
        // The game keeps these in floats; so does this, so the figures match it to the last bit.
        float flat = (float)armour.FlatDamageReduction;
        float relative = (float)armour.RelativeProtection;
        for (int i = 1; i <= damageTier; i++)
        {
            bool above = i > armour.ProtectionTier;
            float flatLoss = (float)(above ? loss.FlatLossAboveTier : loss.FlatLossWithinTier);
            float relativeLoss = (float)(above ? loss.RelativeLossAboveTier : loss.RelativeLossWithinTier);
            if (above && loss.HighDamageTierResistant)
            {
                flatLoss /= 2f;
                relativeLoss /= 2f;
            }
            flat -= flatLoss;
            relative *= 1f - relativeLoss;
        }
        return (flat, relative);
    }

    /// <summary>A hit of <paramref name="damage"/> at <paramref name="damageTier"/> as a full suit of
    /// <paramref name="armour"/> leaves it (the caller checks <see cref="Applies"/> first).</summary>
    public static float Reduce(float damage, int damageTier, PlateArmour armour, ArmourTierLoss loss)
    {
        var (flat, relative) = Effective(damageTier, armour, loss);
        damage = Math.Max(0f, damage - (float)flat);
        damage *= 1f - Math.Max(0f, (float)relative);
        return damage;
    }
}
