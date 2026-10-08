using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>Name, text and attributes of one collectible. Call inside an EnglishLocale.</summary>
internal static class ItemRecords
{
    /// <summary>
    /// The name the game shows. GetHeldItemName is what the client calls; classes override
    /// it, and an override can fail on a server, so fall back to the plain lang entry.
    /// </summary>
    public static string Name(CollectibleObject c) => Name(new ItemStack(c));

    public static string Name(ItemStack stack)
    {
        var c = stack.Collectible;
        string? name = null;
        try { name = c.GetHeldItemName(stack); }
        catch (Exception) { }
        if (string.IsNullOrWhiteSpace(name))
            name = Lang.GetMatching(c.Code.Domain + ":" + c.ItemClass.Name() + "-" + c.Code.Path);
        return name.Trim();
    }

    /// <summary>
    /// An untranslated name: Lang returns the key itself, "domain:item-path". Real names
    /// never start with the domain and a colon and have no spaces.
    /// </summary>
    public static bool IsLangKey(string name, CollectibleObject c) =>
        name.StartsWith(c.Code.Domain + ":", StringComparison.Ordinal) && !name.Contains(' ');

    /// <summary>
    /// The handbook's own text for the item: the description line, the "extraSections" the
    /// type declares, and the "-handbooktitle-"/"-handbooktext-" lang entries. Generated
    /// parts of the page (recipes, drops, stats) are exported as data elsewhere.
    /// </summary>
    public static string? Description(CollectibleObject c)
    {
        var parts = new List<string>();
        try
        {
            var desc = c.GetItemDescText()?.Trim();
            if (!string.IsNullOrEmpty(desc)) parts.Add(desc);
        }
        catch (Exception) { }

        var sections = c.Attributes?["handbook"]?["extraSections"];
        if (sections is { Exists: true } && sections.Token is JArray array)
        {
            foreach (var section in array.OfType<JObject>())
            {
                var title = section["title"]?.Value<string>();
                var text = section["text"]?.Value<string>();
                var block = new List<string>();
                if (!string.IsNullOrEmpty(title)) block.Add("<strong>" + Lang.GetUnformatted(title) + "</strong>");
                if (!string.IsNullOrEmpty(text)) block.Add(Lang.GetUnformatted(text));
                if (block.Count > 0) parts.Add(string.Join("\n", block));
            }
        }

        var prefix = c.Code.Domain + ":" + c.ItemClass.Name();
        var hbTitle = Lang.GetMatchingIfExists(prefix + "-handbooktitle-" + c.Code.ToShortString());
        var hbText = Lang.GetMatchingIfExists(prefix + "-handbooktext-" + c.Code.ToShortString());
        if (hbTitle != null || hbText != null)
        {
            var block = new List<string>();
            if (hbTitle != null) block.Add("<strong>" + hbTitle + "</strong>");
            if (hbText != null) block.Add(hbText);
            parts.Add(string.Join("\n", block));
        }

        var joined = string.Join("\n\n", parts).Trim();
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>
    /// $defs/itemAttributes. A field is present only when it applies, using the same tests
    /// the game's tooltip uses (durability above 1, attack power above the 0.5 bare-hand
    /// default, tool tier only for tools, mining tools and weapons).
    /// </summary>
    public static JObject Attributes(IWorldAccessor world, CollectibleObject c)
    {
        var a = new JObject();
        var stack = new ItemStack(c);

        a["maxStackSize"] = Math.Max(1, c.MaxStackSize);

        var durability = Safe(() => c.GetMaxDurability(stack), c.Durability);
        if (durability > 1) a["durability"] = durability;

        var attack = Safe(() => c.GetAttackPower(stack), c.AttackPower);
        var isMiningTool = c.MiningSpeed is { Count: > 0 };
        if (c.Tool != null) a["tool"] = Json.Lower(c.Tool.Value);
        if (c.Tool != null || isMiningTool || attack > 0.5f) a["toolTier"] = Math.Max(0, c.ToolTier);

        if (c is Block block && block.RequiredMiningTier > 0) a["requiredMiningTier"] = block.RequiredMiningTier;
        if (attack > 0.5f) a["attackPower"] = Json.Round(attack);
        a["materialDensity"] = c.MaterialDensity;

        var food = c.NutritionProps;
        if (food != null && (food.Satiety != 0 || food.Health != 0))
        {
            var n = new JObject { ["category"] = Json.Lower(food.FoodCategory), ["satiety"] = Json.Round(food.Satiety) };
            if (food.Health != 0) n["health"] = Json.Round(food.Health);
            a["nutrition"] = n;
        }

        // The handbook reads fertilizerProps from the attributes (survival's FertilizerProps) and
        // shows all three nutrients, zeros included: "Fertilizer: {0}% N, {1}% P, {2}% K".
        var fert = c.Attributes?["fertilizerProps"];
        if (fert != null && fert.Exists)
        {
            a["fertilizer"] = new JObject
            {
                ["n"] = Json.Round(fert["n"].AsFloat(0)),
                ["p"] = Json.Round(fert["p"].AsFloat(0)),
                ["k"] = Json.Round(fert["k"].AsFloat(0)),
            };
        }

        var comb = c.CombustibleProps;
        if (comb != null && comb.BurnTemperature > 0 && comb.BurnDuration > 0)
        {
            a["burn"] = new JObject
            {
                ["temperature"] = comb.BurnTemperature,
                ["durationSeconds"] = Json.Round(comb.BurnDuration),
            };
        }
        var smelted = Json.Stack(comb?.SmeltedStack);
        if (comb != null && smelted != null)
        {
            var s = new JObject();
            if (comb.MeltingPoint > 0) s["meltingPoint"] = comb.MeltingPoint;
            if (comb.MeltingDuration > 0) s["durationSeconds"] = Json.Round(comb.MeltingDuration);
            s["inputQuantity"] = comb.SmeltedRatio;
            s["requiresContainer"] = comb.RequiresContainer;
            s["method"] = Json.Lower(comb.SmeltingType);
            s["output"] = smelted;
            a["smelting"] = s;
        }

        var flags = Enum.GetValues<EnumItemStorageFlags>()
            .Where(f => f != 0 && (c.StorageFlags & f) == f)
            .Select(f => Json.Lower(f))
            .Distinct()
            .ToList();
        if (flags.Count > 0) a["storageFlags"] = new JArray(flags);

        var extra = Extra(world, c);
        if (extra.Count > 0) a["extra"] = extra;
        return a;
    }

    /// <summary>Processing the schema has no field for yet.</summary>
    private static JObject Extra(IWorldAccessor world, CollectibleObject c)
    {
        var extra = new JObject();
        var ground = Json.Stack(c.GrindingProps?.GroundStack);
        if (ground != null) extra["grinding"] = new JObject { ["output"] = ground };

        var crushed = Json.Stack(c.CrushingProps?.CrushedStack);
        if (crushed != null)
        {
            extra["crushing"] = new JObject
            {
                ["output"] = crushed,
                ["quantity"] = Json.Quantity(c.CrushingProps!.Quantity),
                ["hardnessTier"] = c.CrushingProps.HardnessTier,
            };
        }

        var juicing = Juicing(world, c);
        if (juicing != null) extra["juicing"] = juicing;
        var distillation = Distillation(world, c);
        if (distillation != null) extra["distillation"] = distillation;
        var liquid = Liquid(c);
        if (liquid != null) extra["liquid"] = liquid;

        // TransitionableProps are recipe records of shape `transition` (Recipes/Transitions.cs).

        var food = c.NutritionProps;
        var eaten = Json.Stack(food?.EatenStack);
        if (eaten != null) extra["eatenStack"] = eaten;
        return extra;
    }

    /// <summary>
    /// The fruit press: survival's JuiceableProperties, read from the attributes and resolved as
    /// BlockEntityFruitPress.getJuiceableProps does. LitresPerItem is per input item; mash has
    /// none (what is left in it rides on the stack), so it is left out there.
    /// </summary>
    private static JObject? Juicing(IWorldAccessor world, CollectibleObject c)
    {
        var json = c.Attributes?["juiceableProperties"];
        if (json == null || !json.Exists) return null;
        var props = Safe(() => json.AsObject<JuiceableProperties>(null!, c.Code.Domain), null);
        if (props?.LiquidStack == null) return null;
        JObject? Resolved(JsonItemStack? s) =>
            s != null && (s.ResolvedItemstack != null || s.Resolve(world, "seraphexport juicing", c.Code, false))
                ? Json.Stack(s) : null;

        var output = Resolved(props.LiquidStack);
        if (output == null) return null;
        var j = new JObject();
        if (props.LitresPerItem is float litres) j["litresPerItem"] = Json.Round(litres);
        j["output"] = output;
        var pressed = Resolved(props.PressedStack);
        if (pressed != null) j["pressed"] = pressed;
        var returned = Resolved(props.ReturnStack);
        if (returned != null) j["returned"] = returned;
        return j;
    }

    /// <summary>
    /// The still: survival's DistillationProps on a liquid, read from the attributes as
    /// BlockEntityCondenser does. Ratio is litres out per litre in (0.1 for fruit cider).
    /// </summary>
    private static JObject? Distillation(IWorldAccessor world, CollectibleObject c)
    {
        var json = c.Attributes?["distillationProps"];
        if (json == null || !json.Exists) return null;
        var props = Safe(() => json.AsObject<DistillationProps>(null!, c.Code.Domain), null);
        var stack = props?.DistilledStack;
        if (stack == null || (stack.ResolvedItemstack == null && !stack.Resolve(world, "seraphexport distillation", c.Code, false)))
            return null;
        var output = Json.Stack(stack);
        if (output == null) return null;
        return new JObject { ["ratio"] = Json.Round(props!.Ratio), ["output"] = output };
    }

    /// <summary>
    /// A liquid: survival's WaterTightContainableProps with Containable set, read from the
    /// attributes as BlockLiquidContainerBase.GetContainableProps does. ItemsPerLitre is how
    /// many portion items make a litre (100 for most), so a value per litre is one per that many.
    /// A liquid block in the world (water, 0.001) is containable only through its portion item
    /// (whenFilled), so anything under one item per litre is not a liquid here.
    /// </summary>
    private static JObject? Liquid(CollectibleObject c)
    {
        var json = c.Attributes?["waterTightContainerProps"];
        if (json == null || !json.Exists) return null;
        var props = Safe(() => json.AsObject<WaterTightContainableProps>(null!, c.Code.Domain), null);
        if (props == null || !props.Containable || props.ItemsPerLitre < 1) return null;
        return new JObject { ["itemsPerLitre"] = (int)Math.Round(props.ItemsPerLitre) };
    }

    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f(); }
        catch (Exception) { return fallback; }
    }
}
