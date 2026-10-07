using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// One recipe object read into a shape-neutral form. The same reader turns a registered
/// (resolved) recipe and a freshly parsed definition into this, so the two can be compared.
/// </summary>
public sealed class RecipeForm
{
    /// <summary>The recipe's `Name`, which the loaders default to the defining asset.</summary>
    public AssetLocation? Name;

    /// <summary>A code the definition gives itself (barrel, cooking, smithing): equal on every variant.</summary>
    public string? IdentityCode;

    public bool Enabled = true;
    public List<SlotForm> Slots = new();
    public List<OutputForm> Outputs = new();

    /// <summary>The type-specific block (`grid`, `barrel`, ...), keyed by its property name.</summary>
    public string? BlockName;
    public JObject? Block;
    public JArray? Voxels;
    public List<string> Requirements = new();

    /// <summary>The recipe's own `attributes` (liquid requirements and the like).</summary>
    public JToken? Attributes;

    public JObject Extra = new();

    /// <summary>
    /// The water of each Hydrate or Diedrate copy of this recipe folded into it
    /// (<see cref="WaterClones"/>), one entry per copy.
    /// </summary>
    public List<string> FoldedWater = new();
}

/// <summary>One ingredient slot. `Accepts` lists what may fill it; usually one entry.</summary>
public sealed class SlotForm
{
    public string? Key;
    public string? Role;
    public double? MinQuantity, MaxQuantity, MinRatio, MaxRatio;
    public bool IsTool;
    public int? ToolDurabilityCost;
    public StackSpec? Returned;
    public JObject Extra = new();
    public List<StackSpec> Accepts = new();

    public StackSpec Primary => Accepts[0];
}

/// <summary>
/// A stack as a recipe states it: a code that may still hold wildcards or `{name}`
/// placeholders, or a concrete one.
/// </summary>
public sealed class StackSpec
{
    public EnumItemClass Type;

    /// <summary>Full `domain:path`. Null for a tags-only ingredient.</summary>
    public AssetLocation? Code;

    /// <summary>Wildcard variable name (`name` on grid ingredients).</summary>
    public string? WildcardName;
    public string[]? AllowedVariants, SkipVariants;
    public ComplexTagCondition<TagSet> Tags;
    public double Quantity = 1;
    public double? Litres;
    public JToken? Attributes;

    /// <summary>What the engine resolved the code to, when it is concrete.</summary>
    public ItemStack? Resolved;
}

public sealed class OutputForm
{
    public StackSpec Stack = new();
    public JObject Extra = new();
}
