namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// A melting pot standing in the furnace (#796): the heats it has been through, its bath once a charge
/// has melted (the recipe it melted by and its units), and its charge: the whole of it while nothing has
/// melted, or what has been added to the molten bath since and is still dissolving. The furnace's two pots
/// share one fire, but each is its own charge: they may hold different recipes, and each melts at its own
/// pace once the fire is at its own recipe's temperature (README "Crucible furnace", "Two pots, one fire",
/// "Melting takes time"). <see cref="FurnaceMelt"/> runs it.
/// </summary>
public sealed class FurnacePot
{
    public int Heats { get; set; }

    /// <summary>The unmelted charge, or the additions to the molten bath not yet dissolved.</summary>
    public List<ChargeItem> Charge { get; set; } = [];

    /// <summary>The recipe the bath melted by, or null while nothing has melted.</summary>
    public string? Melted { get; set; }

    public int MeltedUnits { get; set; }

    /// <summary>Units of <see cref="Charge"/> melted (or dissolved) so far: it is done when this reaches
    /// <see cref="ChargeUnits"/>. Kept while the fire is below the recipe's temperature.</summary>
    public double Progress { get; set; }

    /// <summary>Game hours the fire has been below the molten bath's solid point without a break.</summary>
    public double BelowSolidHours { get; set; }

    /// <summary>The bath froze in the furnace: pulled, the pot breaks and its metal comes out as bits.</summary>
    public bool Frozen { get; set; }

    public int ChargeUnits => PotRecipes.TotalUnits(Charge);

    /// <summary>Whether its charge (if any) makes something: an empty pot or a melted one counts as fine
    /// (a molten bath off ratio with its additions still pours its own metal).</summary>
    public bool ChargeMakesSomething(PotRecipes recipes) => Melted != null || Charge.Count == 0 || recipes.Match(Charge).Recipe != null;

    /// <summary>The molten bath as the items of its metal (an ingot's output as its bits, a bit to 5
    /// units), for measuring additions against the recipes; empty with no pourable bath.</summary>
    public IReadOnlyList<ChargeItem> Bath(PotRecipes recipes) =>
        Frozen || recipes.Get(Melted) is not { Pourable: true } r || MeltedUnits <= 0 ? [] : [PotRecipes.MeltAsItems(r.Output, MeltedUnits)];

    /// <summary>Whether the pot takes additions now: no bath yet, or a molten (pourable, unfrozen) one.</summary>
    public bool TakesCharge(PotRecipes recipes) =>
        Melted == null || !Frozen && recipes.Get(Melted) is { Pourable: true };

    /// <summary>The bath and the charge together: what a recipe is matched against.</summary>
    public List<ChargeItem> Mix(PotRecipes recipes)
    {
        var mix = new List<ChargeItem>(Bath(recipes));
        foreach (var item in Charge)
        {
            int i = mix.FindIndex(c => c.Code == item.Code);
            if (i >= 0)
                mix[i] = mix[i] with { Count = mix[i].Count + item.Count };
            else
                mix.Add(item);
        }
        return mix;
    }

    /// <summary>The recipe the pot is melting (or dissolving) its charge by, or null: nothing to melt,
    /// or a charge (with the bath) on no recipe's ratio.</summary>
    public PotRecipe? Melting(PotRecipes recipes) =>
        Charge.Count == 0 || !TakesCharge(recipes) ? null : recipes.Match(Mix(recipes)).Recipe;

    /// <summary>A molten bath whose additions put it on no recipe's ratio: pulled, it pours its own
    /// metal and the additions are lost to the slag.</summary>
    public bool OffRatio(PotRecipes recipes) => Melted != null && Charge.Count > 0 && Melting(recipes) == null;

    /// <summary>Puts <paramref name="available"/> of <paramref name="code"/> in as the recipes allow, the
    /// molten bath (if any) counted as items of its metal, up to <paramref name="capacity"/> units; the
    /// add (its count is what went in). A pot whose bath is a ferroalloy, or frozen, takes nothing.</summary>
    public ChargeAdd Add(PotRecipes recipes, string code, int available, int capacity)
    {
        if (!TakesCharge(recipes))
            return new ChargeAdd(0, ChargeRefusal.PotFull);
        var add = recipes.CanAdd(Mix(recipes), code, available, capacity);
        if (add.Count <= 0)
            return add;
        int i = Charge.FindIndex(c => c.Code == code);
        if (i >= 0)
            Charge[i] = Charge[i] with { Count = Charge[i].Count + add.Count };
        else
            Charge.Add(new ChargeItem(code, add.Count));
        return add;
    }

    /// <summary>The charge has melted (or the additions dissolved): the pot is the recipe's bath of the
    /// mix's units.</summary>
    internal void Finish(PotRecipes recipes, PotRecipe recipe)
    {
        MeltedUnits = PotRecipes.TotalUnits(Mix(recipes));
        Melted = recipe.Code;
        Charge = [];
        Progress = 0;
        BelowSolidHours = 0;
    }
}
