using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon's current order (Eidolon/README.md, "Orders"): one at a time, its code and arguments in
/// the watched attributes (<see cref="OrderKey"/>, saved with the world and shown in its info), and the
/// running instance made from them (<see cref="EidolonOrders"/>), which <see cref="AiTaskEidolonOrder"/>
/// runs while the eidolon can work. An order says how it is doing through <see cref="SetStatus"/>
/// (a lang key, shown under it: "no way there"). No order: it stands where it is.
/// </summary>
public class EntityBehaviorEidolonOrders(Entity entity) : EntityBehavior(entity)
{
    public const string Code = "seraphhorizons.eidolonOrders";
    public const string OrderKey = "seraphhorizons:order";
    public const string StatusKey = "seraphhorizons:orderStatus";

    private IEidolonOrder? _running;
    private bool _made;

    public override string PropertyName() => Code;

    /// <summary>The order's code, or null for none.</summary>
    public string? OrderCode => entity.WatchedAttributes.GetTreeAttribute(OrderKey)?.GetString("code");

    /// <summary>The order's arguments (its tree, <c>code</c> included), or null.</summary>
    public ITreeAttribute? OrderArgs => entity.WatchedAttributes.GetTreeAttribute(OrderKey);

    /// <summary>The order's running instance (server side), made on first use; null when there is no
    /// order or its code is not registered (an order of a later version, kept until replaced).</summary>
    public IEidolonOrder? Current
    {
        get
        {
            if (!_made && entity is EntityLaborEidolon eidolon && OrderArgs is { } args && OrderCode is { } code)
                _running = EidolonOrders.Create(eidolon, code, args);
            _made = true;
            return _running;
        }
    }

    /// <summary>Gives it an order (server side): <paramref name="code"/> as registered, with its
    /// arguments written into <paramref name="args"/> (may be null). The order running now stops at once.</summary>
    public void SetOrder(string code, ITreeAttribute? args = null)
    {
        var tree = args as TreeAttribute ?? new TreeAttribute();
        tree.SetString("code", code);
        Replace(tree);
    }

    /// <summary>No order: it stands where it is.</summary>
    public void ClearOrder() => Replace(null);

    /// <summary>The running order's status line (a lang key with its arguments), or none (null).</summary>
    public void SetStatus(string? langKey, params object[] args)
    {
        if (langKey == null)
        {
            entity.WatchedAttributes.RemoveAttribute(StatusKey);
            return;
        }
        var tree = new TreeAttribute();
        tree.SetString("key", langKey);
        tree.SetString("args", string.Join("\u001f", args.Select(a => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture))));
        entity.WatchedAttributes[StatusKey] = tree;
        entity.WatchedAttributes.MarkPathDirty(StatusKey);
    }

    /// <summary>Called by the order task when the order says it is done.</summary>
    internal void Done(IEidolonOrder order)
    {
        if (ReferenceEquals(order, _running))
            Replace(null);
    }

    private void Replace(TreeAttribute? tree)
    {
        // The task that ran the old order sees it changed and ends (AiTaskEidolonOrder.ContinueExecute).
        _running = null;
        _made = false;
        if (tree == null)
            entity.WatchedAttributes.RemoveAttribute(OrderKey);
        else
        {
            entity.WatchedAttributes[OrderKey] = tree;
            entity.WatchedAttributes.MarkPathDirty(OrderKey);
        }
        SetStatus(null);
        if (entity.GetBehavior<EntityBehaviorTaskAI>() is { } ai)
            ai.TaskManager.StopTask(typeof(AiTaskEidolonOrder));
    }

    public override void GetInfoText(StringBuilder infotext)
    {
        string? code = OrderCode;
        infotext.AppendLine(code == null
            ? Lang.Get("seraphhorizons:eidolon-order-none")
            : Lang.Get("seraphhorizons:eidolon-order", Lang.Get("seraphhorizons:eidolon-order-" + code)));
        if (entity.WatchedAttributes.GetTreeAttribute(StatusKey) is { } status && status.GetString("key") is { Length: > 0 } key)
        {
            object[] args = (status.GetString("args") ?? "").Split('\u001f', StringSplitOptions.RemoveEmptyEntries);
            infotext.AppendLine(Lang.Get(key, args));
        }
    }
}
