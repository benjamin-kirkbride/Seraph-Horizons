using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace SeraphHorizons.Mod.Admin;

/// <summary>
/// The conventions every command under <c>/sh ore</c> and <c>/sh trade</c> follows (#458, #459,
/// docs/admin-tools.md), applied to the whole tree whichever feature registered a command:
/// <list type="bullet">
/// <item><c>--json</c> anywhere in the arguments replaces the answer with one JSON object
/// (<see cref="AdminOutput"/>). A precondition on the <c>ore</c> and <c>trade</c> nodes takes the
/// flag out of the raw arguments before any parser sees it; the game runs a node's preconditions
/// when the call reaches it, before its subcommands. Handlers attach their data with
/// <see cref="Attach"/>; the rest answer with the generic shape.</item>
/// <item>Privilege <c>controlserver</c> on every command (the players' <c>/sh company</c> sits
/// outside both nodes). The game checks a command's own privilege, or its nearest parent's, only
/// where the handler runs, so a subcommand that set none would take whatever the shared <c>/sh</c>
/// root got from the first feature to register it.</item>
/// </list>
/// Both are applied to each node's handlers the first time a call reaches the node, so commands that
/// later waves register (orders, deliveries, visitors) get them too. Handlers are swapped through the
/// game's private <c>ChatCommandImpl.handler</c>; if that is gone, <c>--json</c> is still stripped
/// and the commands answer in text.
/// </summary>
public static class AdminCommands
{
    private sealed class CallState
    {
        public bool Json;
        public JsonObject? Data;
    }

    private static readonly ConditionalWeakTable<TextCommandCallingArgs, CallState> Calls = new();
    private static readonly ConditionalWeakTable<IChatCommand, object> Wrapped = new();
    private static readonly ConditionalWeakTable<IChatCommand, object> Installed = new();

    private static readonly AccessTools.FieldRef<ChatCommandImpl, OnCommandDelegate>? Handler = Try();

    /// <summary>Why handlers can't be wrapped on this game version, or null.</summary>
    public static string? Unsupported => Handler == null ? "ChatCommandImpl.handler is gone" : null;

    /// <summary>Applies the conventions to a node (<c>ore</c> or <c>trade</c>) and everything under it.</summary>
    public static void Install(IChatCommand node)
    {
        if (Installed.TryGetValue(node, out _)) return;
        Installed.Add(node, new object());
        node.WithPreCondition(args =>
        {
            var words = new List<string>();
            while (args.RawArgs.Length > 0) words.Add(args.RawArgs.PopWord());
            bool json = AdminOutput.StripFlag(words);
            args.RawArgs = new CmdArgs(words.ToArray());
            Calls.AddOrUpdate(args, new CallState { Json = json });
            Apply(node, path: node.Name);
            return TextCommandResult.Success();
        });
    }

    /// <summary>Whether this call asked for JSON.</summary>
    public static bool Json(TextCommandCallingArgs args) => Calls.TryGetValue(args, out var s) && s.Json;

    /// <summary>Adds structured fields to this call's JSON answer (ignored without <c>--json</c>).</summary>
    public static void Attach(TextCommandCallingArgs args, JsonObject data)
    {
        if (!Calls.TryGetValue(args, out var s) || !s.Json) return;
        s.Data ??= new JsonObject();
        foreach (var (key, value) in data.ToList())
        {
            data.Remove(key);
            s.Data[key] = value;
        }
    }

    /// <summary>An answer built by a handler that knows its shape: text, or with <c>--json</c> the
    /// output's JSON.</summary>
    public static TextCommandResult Answer(TextCommandCallingArgs args, AdminOutput output)
    {
        if (Json(args)) Attach(args, (JsonObject)output.Data.DeepClone());
        string text = AdminOutput.ChatSafe(output.ToText());
        return output.Ok ? TextCommandResult.Success(text) : TextCommandResult.Error(text);
    }

    private static void Apply(IChatCommand command, string path)
    {
        if (command is ChatCommandImpl c && c.GetPrivilege() != Privilege.controlserver)
            command.RequiresPrivilege(Privilege.controlserver);
        if (Handler != null && command is ChatCommandImpl impl && !Wrapped.TryGetValue(command, out _))
        {
            Wrapped.Add(command, new object());
            if (Handler(impl) is { } inner)
            {
                string name = path;
                Handler(impl) = args => Wrap(inner, args, name);
            }
        }
        foreach (var (name, sub) in command.AllSubcommands)
            Apply(sub, path + " " + name);
    }

    private static TextCommandResult Wrap(OnCommandDelegate inner, TextCommandCallingArgs args, string path)
    {
        TextCommandResult result;
        try
        {
            result = inner(args);
        }
        catch (Exception e) when (Json(args))
        {
            result = TextCommandResult.Error(e.Message);
        }
        if (result.Status == EnumCommandStatus.Deferred) return result;
        if (!Json(args))
        {
            result.StatusMessage = AdminOutput.ChatSafe(result.StatusMessage);
            return result;
        }
        Calls.TryGetValue(args, out var state);
        var output = AdminOutput.FromText(path, result.StatusMessage, result.Status != EnumCommandStatus.Error);
        if (state?.Data is { } data)
            foreach (var (key, value) in data.ToList())
            {
                data.Remove(key);
                output.Data[key] = value;
            }
        result.StatusMessage = AdminOutput.ChatSafe(output.ToJson());
        return result;
    }

    private static AccessTools.FieldRef<ChatCommandImpl, OnCommandDelegate>? Try()
    {
        try
        {
            return AccessTools.FieldRefAccess<ChatCommandImpl, OnCommandDelegate>("handler");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
