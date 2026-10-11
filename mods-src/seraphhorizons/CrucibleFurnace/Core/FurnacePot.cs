namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// A melting pot standing in the furnace (#796): the heats it has been through, its charge until that
/// melts, then the recipe it melted by and the units charged. The furnace's two pots share one fire,
/// but each is its own charge: they may hold different recipes, and each melts when the fire reaches
/// its own recipe's temperature (README "Crucible furnace", "Two pots, one fire").
/// </summary>
public sealed class FurnacePot
{
    public int Heats { get; set; }
    public List<ChargeItem> Charge { get; set; } = [];
    /// <summary>The recipe the charge melted by, or null while it is unmelted.</summary>
    public string? Melted { get; set; }
    public int MeltedUnits { get; set; }

    public int ChargeUnits => PotRecipes.TotalUnits(Charge);

    /// <summary>Whether its charge (if any) makes something: an empty pot or a melted one counts as fine.</summary>
    public bool ChargeMakesSomething(PotRecipes recipes) => Melted != null || Charge.Count == 0 || recipes.Match(Charge).Recipe != null;

    /// <summary>Melts the charge if the fire at <paramref name="temperature"/> is at its recipe's
    /// temperature; returns whether it did.</summary>
    public bool TryMelt(PotRecipes recipes, double temperature)
    {
        if (Melted != null || Charge.Count == 0)
            return false;
        if (recipes.Match(Charge).Recipe is not { } recipe || temperature < recipe.Temperature)
            return false;
        Melted = recipe.Code;
        MeltedUnits = PotRecipes.TotalUnits(Charge);
        Charge = [];
        return true;
    }

    /// <summary>Puts <paramref name="available"/> of <paramref name="code"/> in as the recipes allow,
    /// up to <paramref name="capacity"/> units; the add (its count is what went in).</summary>
    public ChargeAdd Add(PotRecipes recipes, string code, int available, int capacity)
    {
        var add = recipes.CanAdd(Charge, code, available, capacity);
        if (add.Count <= 0)
            return add;
        int i = Charge.FindIndex(c => c.Code == code);
        if (i >= 0)
            Charge[i] = Charge[i] with { Count = Charge[i].Count + add.Count };
        else
            Charge.Add(new ChargeItem(code, add.Count));
        return add;
    }
}
