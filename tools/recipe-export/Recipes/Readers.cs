using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>Reads one recipe object (registered or freshly parsed) into a <see cref="RecipeForm"/>.</summary>
public interface IRecipeReader
{
    /// <summary>The `shape` of `recipeTypes`: grid, voxels, barrel, alloy, cooking or generic.</summary>
    string Shape { get; }

    RecipeForm Read(object recipe);
}

public static class Readers
{
    /// <summary>
    /// The reader for a registry's element type. Dedicated readers first; anything else goes
    /// through the generic ones. Throws when nothing can read the type.
    /// </summary>
    public static IRecipeReader For(Type type)
    {
        if (typeof(GridRecipe).IsAssignableFrom(type)) return new GridReader();
        if (typeof(LayeredVoxelRecipe).IsAssignableFrom(type)) return new VoxelReader();
        if (typeof(BarrelRecipe).IsAssignableFrom(type)) return new BarrelReader();
        if (typeof(AlloyRecipe).IsAssignableFrom(type)) return new AlloyReader();
        if (typeof(CookingRecipe).IsAssignableFrom(type)) return new CookingReader();
        if (typeof(IRecipeBase).IsAssignableFrom(type)) return new BaseRecipeReader();
        return ReflectiveReader.Create(type);
    }

    // ------------------------------------------------------------------ helpers

    internal static void ReadCommon(RecipeBase r, RecipeForm form)
    {
        form.Name = r.Name;
        form.Enabled = r.Enabled;
        form.Attributes = r.Attributes?.Token;
        if (!string.IsNullOrEmpty(r.RequiresTrait))
            form.Requirements.Add($"Only players with the {r.RequiresTrait} trait can make this");
    }

    internal static StackSpec Spec(IRecipeIngredient ing)
    {
        var spec = new StackSpec
        {
            Type = ing.Type,
            Code = IsTagsOnly(ing.Code) ? null : ing.Code?.Clone(),
            WildcardName = ing.Name,
            AllowedVariants = ing.AllowedVariants,
            SkipVariants = ing.SkipVariants,
            Tags = ing.Tags,
            Quantity = ing.Quantity,
        };
        if (ing is CraftingRecipeIngredient c)
        {
            spec.Attributes = c.Attributes?.Token;
            if (c.MatchingType == EnumRecipeMatchType.Exact && c.Code != null && !IsPattern(c.Code))
                spec.Resolved = c.ResolvedItemStack;
        }
        return spec;
    }

    internal static StackSpec Spec(JsonItemStack stack)
    {
        return new StackSpec
        {
            Type = stack.Type,
            Code = stack.Code?.Clone(),
            Quantity = stack.StackSize,
            Attributes = stack.Attributes?.Token,
            Resolved = stack.ResolvedItemstack,
        };
    }

    internal static StackSpec Spec(ItemStack stack)
    {
        return new StackSpec
        {
            Type = stack.Class,
            Code = stack.Collectible?.Code?.Clone(),
            Quantity = stack.StackSize,
            Resolved = stack,
        };
    }

    internal static SlotForm Slot(IRecipeIngredient ing)
    {
        var slot = new SlotForm { Accepts = { Spec(ing) } };
        if (ing is CraftingRecipeIngredient c)
        {
            slot.IsTool = c.IsTool;
            if (c.IsTool) slot.ToolDurabilityCost = c.ToolDurabilityCost;
            if (!c.Consume && !c.IsTool) slot.Extra["consumed"] = false;
            if (c.RecipeAttributes?.Token is JToken ra) slot.Extra["recipeAttributes"] = ra.DeepClone();
        }
        if (ing.ReturnedStack?.Code != null) slot.Returned = Spec(ing.ReturnedStack);
        return slot;
    }

    internal static bool IsTagsOnly(AssetLocation? code) =>
        code == null || (code.Domain == "*" && code.Path == "*");

    /// <summary>True when a code still stands for many: wildcards, placeholders or a regex.</summary>
    internal static bool IsPattern(AssetLocation code) =>
        code.Path.Contains('*') || code.Path.Contains('{') || code.Path.StartsWith('@') ||
        code.Domain == "*" || code.Path.Contains('(');
}

// ---------------------------------------------------------------------- grid

internal sealed class GridReader : IRecipeReader
{
    public string Shape => "grid";

    public RecipeForm Read(object recipe)
    {
        var g = (GridRecipe)recipe;
        var form = new RecipeForm();
        Readers.ReadCommon(g, form);

        // A registered recipe has lost Ingredients and IngredientPattern once the server has
        // built its asset packet (GridRecipe.FreeRAMServer); ResolvedIngredients keeps one
        // clone per cell with Id set to the pattern key, which is all we need.
        string pattern;
        var byKey = new List<(string Key, IRecipeIngredient Ing)>();
        if (g.ResolvedIngredients != null)
        {
            pattern = string.Concat(g.ResolvedIngredients.Select(c => c == null ? "_" : c.Id));
            foreach (var cell in g.ResolvedIngredients)
                if (cell != null && byKey.All(k => k.Key != cell.Id)) byKey.Add((cell.Id, cell));
        }
        else
        {
            pattern = NormalisePattern(g.IngredientPattern ?? "");
            foreach (var (key, ing) in g.Ingredients ?? new())
                if (pattern.Contains(key)) byKey.Add((key, ing));
        }

        foreach (var (key, ing) in byKey)
        {
            var slot = Readers.Slot(ing);
            slot.Key = key;
            form.Slots.Add(slot);
        }
        if (g.Output != null) form.Outputs.Add(new OutputForm { Stack = Readers.Spec(g.Output) });

        var rows = new JArray();
        for (int r = 0; r < g.Height; r++)
            rows.Add(r * g.Width + g.Width <= pattern.Length ? pattern.Substring(r * g.Width, g.Width) : "");
        form.BlockName = "grid";
        form.Block = new JObject
        {
            ["width"] = g.Width,
            ["height"] = g.Height,
            ["shapeless"] = g.Shapeless,
            ["pattern"] = rows,
        };
        if (g.CopyAttributesFrom != null) form.Block["copyAttributesFrom"] = g.CopyAttributesFrom;
        if (g.RecipeGroup != 0) form.Extra["recipeGroup"] = g.RecipeGroup;
        return form;
    }

    /// <summary>What GridRecipe.Resolve does to the pattern, with spaces shown as empty cells.</summary>
    internal static string NormalisePattern(string raw) =>
        raw.Replace(",", "").Replace("\t", "").Replace("\r", "").Replace("\n", "").Replace(' ', '_');
}

// -------------------------------------------------------------------- voxels

internal sealed class VoxelReader : IRecipeReader
{
    public string Shape => "voxels";

    public RecipeForm Read(object recipe)
    {
        var v = (LayeredVoxelRecipe)recipe;
        var form = new RecipeForm();
        Readers.ReadCommon(v, form);
        if (v is SmithingRecipe s && s.Code != null) form.IdentityCode = s.Code.ToString();
        foreach (var ing in v.Ingredients ?? Array.Empty<CraftingRecipeIngredient>())
            form.Slots.Add(Readers.Slot(ing));
        if (v.Output != null) form.Outputs.Add(new OutputForm { Stack = Readers.Spec(v.Output) });

        // Pattern layers are bottom to top (GenVoxels puts layer i at y = i). Anything but
        // `_` or a space is a filled voxel.
        form.Voxels = new JArray();
        foreach (var layer in v.Pattern ?? Array.Empty<string[]>())
            form.Voxels.Add(new JArray(layer.Select(row =>
                new string(row.Select(c => c is '_' or ' ' ? '_' : '#').ToArray()))));
        return form;
    }
}

// -------------------------------------------------------------------- barrel

internal sealed class BarrelReader : IRecipeReader
{
    public string Shape => "barrel";

    public RecipeForm Read(object recipe)
    {
        var b = (BarrelRecipe)recipe;
        var form = new RecipeForm { IdentityCode = b.Code };
        Readers.ReadCommon(b, form);
        foreach (var ing in b.Ingredients ?? Array.Empty<BarrelRecipeIngredient>())
        {
            var slot = Readers.Slot(ing);
            if (ing.Litres >= 0) slot.Primary.Litres = ing.Litres;
            if (ing.ConsumeQuantity != null) slot.Extra["consumeQuantity"] = ing.ConsumeQuantity;
            if (ing.ConsumeLitres != null) slot.Extra["consumeLitres"] = ing.ConsumeLitres;
            form.Slots.Add(slot);
        }
        if (b.Output != null)
        {
            var output = new OutputForm { Stack = Readers.Spec(b.Output) };
            if (b.Output.Litres > 0) output.Stack.Litres = b.Output.Litres;
            form.Outputs.Add(output);
        }
        form.BlockName = "barrel";
        form.Block = new JObject { ["sealHours"] = b.SealHours };
        return form;
    }
}

// --------------------------------------------------------------------- alloy

internal sealed class AlloyReader : IRecipeReader
{
    public string Shape => "alloy";

    public RecipeForm Read(object recipe)
    {
        var a = (AlloyRecipe)recipe;
        var form = new RecipeForm { Enabled = a.Enabled };
        foreach (var ing in a.Ingredients ?? Array.Empty<MetalAlloyIngredient>())
            form.Slots.Add(new SlotForm
            {
                MinRatio = Math.Round(ing.MinRatio, 6),
                MaxRatio = Math.Round(ing.MaxRatio, 6),
                Accepts = { Readers.Spec(ing) },
            });
        if (a.Output != null) form.Outputs.Add(new OutputForm { Stack = Readers.Spec(a.Output) });
        form.BlockName = "alloy";
        form.Block = new JObject();
        return form;
    }
}

// ------------------------------------------------------------------- cooking

internal sealed class CookingReader : IRecipeReader
{
    public string Shape => "cooking";

    public RecipeForm Read(object recipe)
    {
        var c = (CookingRecipe)recipe;
        var form = new RecipeForm { Enabled = c.Enabled, IdentityCode = c.Code };
        // One slot per recipe ingredient; its valid stacks are the alternatives. The schema's
        // ingredient has a single code, so that is the first valid stack and the full list
        // goes into extra.validStacks.
        foreach (var ing in c.Ingredients ?? Array.Empty<CookingRecipeIngredient>())
        {
            var slot = new SlotForm
            {
                Role = ing.Code,
                MinQuantity = ing.MinQuantity,
                MaxQuantity = ing.MaxQuantity,
            };
            var valid = new JArray();
            foreach (var vs in ing.ValidStacks ?? Array.Empty<CookingRecipeStack>())
            {
                slot.Accepts.Add(Readers.Spec(vs));
                var entry = new JObject { ["code"] = vs.Code?.ToString(), ["kind"] = Kind(vs.Type) };
                if (vs.CookedStack?.Code != null) entry["cookedStack"] = vs.CookedStack.Code.ToString();
                valid.Add(entry);
            }
            if (slot.Accepts.Count == 0) continue;
            slot.Extra["validStacks"] = valid;
            if (ing.TypeName != "unknown") slot.Extra["typeName"] = ing.TypeName;
            if (ing.PortionSizeLitres > 0) slot.Extra["portionSizeLitres"] = ing.PortionSizeLitres;
            form.Slots.Add(slot);
        }
        form.BlockName = "cooking";
        form.Block = new JObject();
        if (c.Code != null) form.Block["code"] = c.Code;
        if (c.CooksInto?.Code != null) form.Outputs.Add(new OutputForm { Stack = Readers.Spec(c.CooksInto) });
        if (c.IsFood) form.Extra["isFood"] = true;
        return form;
    }

    internal static string Kind(EnumItemClass type) => type == EnumItemClass.Block ? "block" : "item";
}

// ------------------------------------------------------ generic: IRecipeBase

internal sealed class BaseRecipeReader : IRecipeReader
{
    public string Shape => "generic";

    public RecipeForm Read(object recipe)
    {
        var r = (IRecipeBase)recipe;
        var form = new RecipeForm { Name = r.Name, Enabled = r.Enabled };
        if (recipe is RecipeBase rb) Readers.ReadCommon(rb, form);
        foreach (var ing in r.RecipeIngredients) form.Slots.Add(Readers.Slot(ing));
        var output = r.RecipeOutput switch
        {
            CraftingRecipeIngredient c => Readers.Spec(c),
            JsonItemStack j => Readers.Spec(j),
            { ResolvedItemStack: ItemStack s } => Readers.Spec(s),
            _ => null,
        };
        if (output != null) form.Outputs.Add(new OutputForm { Stack = output });
        return form;
    }
}

// ------------------------------------------------------ generic: reflection

/// <summary>
/// Reads recipe classes that share no engine base class (most mod machines) by their
/// public members: `Ingredients`/`Inputs` and `Output(s)`/`Product(s)`/`Result(s)`, holding
/// stacks, ingredients, or objects with an array of either (one slot, several choices).
/// </summary>
internal sealed class ReflectiveReader : IRecipeReader
{
    private static readonly string[] IngredientNames = { "ingredients", "ingredient", "inputs", "input" };
    private static readonly string[] OutputNames = { "outputs", "output", "products", "product", "results", "result" };

    private readonly Member[] _ingredients;
    private readonly Member[] _outputs;
    private readonly Member[] _smelting;
    private readonly Member? _name, _enabled, _code, _attributes;
    private readonly Member[] _scalars;

    public string Shape => "generic";

    private ReflectiveReader(Type type)
    {
        var members = Member.All(type);
        _ingredients = members.Where(m => IngredientNames.Contains(m.Name.ToLowerInvariant()) && HoldsStacks(m.Type)).ToArray();
        _outputs = members.Where(m => OutputNames.Contains(m.Name.ToLowerInvariant()) && HoldsStacks(m.Type)).ToArray();
        // ACulinaryArtillery's simmering names its product only as combustible properties.
        _smelting = _outputs.Length == 0
            ? members.Where(m => typeof(CombustibleProperties).IsAssignableFrom(m.Type)).ToArray()
            : Array.Empty<Member>();
        _name = members.FirstOrDefault(m => m.Name == "Name" && m.Type == typeof(AssetLocation));
        _enabled = members.FirstOrDefault(m => m.Name == "Enabled" && m.Type == typeof(bool));
        _code = members.FirstOrDefault(m => m.Name == "Code" && (m.Type == typeof(string) || m.Type == typeof(AssetLocation)));
        _attributes = members.FirstOrDefault(m => m.Name == "Attributes" && m.Type == typeof(Vintagestory.API.Datastructures.JsonObject));
        // The registry-assigned id is left out: it is a load-order counter, not recipe data.
        _scalars = members.Where(m => m != _name && m != _enabled && m != _code && IsScalar(m.Type) &&
                                      !m.Name.Equals("RecipeId", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public static IRecipeReader Create(Type type)
    {
        var reader = new ReflectiveReader(type);
        if (reader._ingredients.Length == 0 || reader._outputs.Length + reader._smelting.Length == 0)
            throw new NotSupportedException(
                $"{type.FullName} has no recognisable ingredient and output members " +
                $"(looked for {string.Join("/", IngredientNames)} and {string.Join("/", OutputNames)})");
        return reader;
    }

    public RecipeForm Read(object recipe)
    {
        var form = new RecipeForm
        {
            Name = _name?.Get(recipe) as AssetLocation,
            Enabled = _enabled?.Get(recipe) as bool? ?? true,
            IdentityCode = _code?.Get(recipe)?.ToString(),
            Attributes = (_attributes?.Get(recipe) as Vintagestory.API.Datastructures.JsonObject)?.Token,
        };
        foreach (var m in _ingredients)
            foreach (var item in Items(m.Get(recipe)))
            {
                var slot = SlotOf(item);
                if (slot != null) form.Slots.Add(slot);
            }
        foreach (var m in _outputs)
            foreach (var item in Items(m.Get(recipe)))
            {
                var spec = SpecOf(item);
                if (spec == null) continue;
                var output = new OutputForm { Stack = spec };
                AddScalars(item, output.Extra, "Litres", v => spec.Litres = Convert.ToDouble(v));
                form.Outputs.Add(output);
            }
        foreach (var m in _smelting)
            if (m.Get(recipe) is CombustibleProperties { SmeltedStack: JsonItemStack smelted } cp)
            {
                var output = new OutputForm { Stack = Readers.Spec(smelted) };
                output.Stack.Quantity = smelted.StackSize;
                output.Extra["from"] = m.Name;
                if (cp.MeltingPoint > 0) output.Extra["temperature"] = cp.MeltingPoint;
                if (cp.MeltingDuration > 0) output.Extra["durationSeconds"] = cp.MeltingDuration;
                if (cp.SmeltedRatio > 0) output.Extra["inputRatio"] = cp.SmeltedRatio;
                form.Outputs.Add(output);
            }
        foreach (var m in _scalars)
        {
            var v = m.Get(recipe);
            if (v != null) form.Extra[Camel(m.Name)] = JToken.FromObject(v is AssetLocation a ? a.ToString() : v);
        }
        return form;
    }

    private static SlotForm? SlotOf(object item)
    {
        if (item is IRecipeIngredient ing)
        {
            var slot = Readers.Slot(ing);
            AddScalars(item, slot.Extra, "Litres", v => { var l = Convert.ToDouble(v); if (l >= 0) slot.Primary.Litres = l; });
            return slot;
        }
        var direct = SpecOf(item);
        if (direct != null)
        {
            var slot = new SlotForm { Accepts = { direct } };
            AddScalars(item, slot.Extra, "Litres", v => direct.Litres = Convert.ToDouble(v));
            return slot;
        }
        // A container of alternatives, e.g. ACulinaryArtillery's DoughIngredient.Inputs.
        var choices = Member.All(item.GetType()).Where(m => HoldsStacks(m.Type))
            .SelectMany(m => Items(m.Get(item))).Select(SpecOf).OfType<StackSpec>().ToList();
        if (choices.Count == 0) return null;
        var multi = new SlotForm();
        multi.Accepts.AddRange(choices);
        return multi;
    }

    private static StackSpec? SpecOf(object? item) => item switch
    {
        IRecipeIngredient i => Readers.Spec(i),
        JsonItemStack j => Readers.Spec(j),
        ItemStack s => Readers.Spec(s),
        _ => null,
    };

    /// <summary>Copies simple public values of a stack-like object into `extra`, except base-class ones.</summary>
    private static void AddScalars(object item, JObject extra, string special, System.Action<object> onSpecial)
    {
        var baseType = item is IRecipeIngredient ? typeof(CraftingRecipeIngredient)
                     : item is JsonItemStack ? typeof(JsonItemStack) : null;
        foreach (var m in Member.All(item.GetType()))
        {
            if (!IsScalar(m.Type) || (baseType != null && m.DeclaringType.IsAssignableFrom(baseType))) continue;
            var v = m.Get(item);
            if (v == null) continue;
            if (m.Name == special) onSpecial(v);
            else extra[Camel(m.Name)] = JToken.FromObject(v is AssetLocation a ? a.ToString() : v);
        }
    }

    private static IEnumerable<object> Items(object? value) => value switch
    {
        null => Array.Empty<object>(),
        string => Array.Empty<object>(),
        IEnumerable e => e.Cast<object?>().OfType<object>(),
        _ => new[] { value },
    };

    private static bool HoldsStacks(Type t)
    {
        var element = Element(t);
        if (IsStack(element)) return true;
        // One level of nesting: an object with an array of stacks.
        return !element.IsPrimitive && element != typeof(string) &&
               Member.All(element).Any(m => IsStack(Element(m.Type)));
    }

    private static bool IsStack(Type t) =>
        typeof(IRecipeIngredient).IsAssignableFrom(t) || typeof(JsonItemStack).IsAssignableFrom(t) ||
        typeof(ItemStack).IsAssignableFrom(t);

    private static Type Element(Type t)
    {
        if (t.IsArray) return t.GetElementType()!;
        if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t)) return t.GetGenericArguments()[0];
        return t;
    }

    private static bool IsScalar(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t == typeof(AssetLocation) || t.IsEnum;
    }

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    internal sealed class Member
    {
        public required string Name;
        public required Type Type;
        public required Type DeclaringType;
        public required System.Func<object, object?> Get;

        public static List<Member> All(Type type)
        {
            var list = new List<Member>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                list.Add(new Member { Name = f.Name, Type = f.FieldType, DeclaringType = f.DeclaringType!, Get = f.GetValue });
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.GetIndexParameters().Length == 0)
                    list.Add(new Member { Name = p.Name, Type = p.PropertyType, DeclaringType = p.DeclaringType!, Get = o => SafeGet(p, o) });
            return list;
        }

        // Mod recipe classes have computed properties that throw before the recipe is
        // resolved (a parsed definition); such a value is simply absent.
        private static object? SafeGet(PropertyInfo p, object o)
        {
            try { return p.GetValue(o); }
            catch (TargetInvocationException) { return null; }
        }
    }
}
