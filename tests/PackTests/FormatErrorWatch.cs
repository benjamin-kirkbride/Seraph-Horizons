using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Keeps admin answers away from the translation formatter (<c>AdminOutput.ChatSafe</c>). The game
/// hands a player's single-line command answer to <c>Lang.GetL</c>, which <c>string.Format</c>s it,
/// so a brace in it (every <c>--json</c> answer) logs <c>Input string was not in a correct
/// format</c> rather than failing the command; an answer with a newline is sent as is (1.22.7
/// <c>ChatCommandApi.Execute</c>). Atlas (0.15) doesn't use that path: it runs every answer through
/// <c>Lang.Get</c> for <c>CommandResult.Message</c>, newline or not, and the error that logs is the
/// harness's, so it is ignored here and the game's rule is checked on the raw answer instead
/// (<see cref="AssertChatSafe"/>). What the watch still catches is formatting anywhere else while
/// the command runs, e.g. a handler passing an answer through <c>Lang.Get</c>.
/// </summary>
internal sealed class FormatErrorWatch : IDisposable
{
    private readonly ILogger logger;
    private readonly List<string> errors = [];

    public FormatErrorWatch(ILogger logger)
    {
        this.logger = logger;
        logger.EntryAdded += OnEntry;
    }

    public IReadOnlyList<string> Errors
    {
        get { lock (errors) return errors.ToList(); }
    }

    /// <summary>Fails if a player would see this answer go through <c>Lang.GetL</c> with a brace in it.</summary>
    public static void AssertChatSafe(string command, TextCommandResult raw)
    {
        string text = raw.StatusMessage ?? "";
        bool formatted = text.Length > 0 && !text.Contains('\n') && raw.MessageParams == null;
        Assert.False(formatted && text.IndexOfAny(['{', '}']) >= 0,
            $"{command}: a player's single-line answer is formatted by Lang.GetL and this one has a brace: {text}");
    }

    private void OnEntry(EnumLogType type, string message, object[] args)
    {
        if (type is not (EnumLogType.Error or EnumLogType.Fatal)) return;
        string text = message + " " + string.Join(" ", args ?? []);
        if (!text.Contains("Input string was not in a correct format")) return;
        string stack = Environment.StackTrace;
        if (stack.Contains("Atlas.Internal.Hosting.ConsoleCommands")) return;
        lock (errors) errors.Add(text + "\nlogged from:\n" + stack);
    }

    public void Dispose() => logger.EntryAdded -= OnEntry;
}
