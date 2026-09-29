using Vintagestory.API.Config;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// Switches the process-wide locale to English for the lifetime of the scope. Names come
/// from GetHeldItemName, which many classes override and which all read Lang.Get, so
/// passing "en" to each lookup is not enough; the server's configured language must not
/// leak into the export.
/// </summary>
internal sealed class EnglishLocale : IDisposable
{
    public const string Code = "en";

    private readonly string? _previous;

    public EnglishLocale()
    {
        _previous = Lang.CurrentLocale;
        if (_previous != Code) Lang.ChangeLanguage(Code);
    }

    public void Dispose()
    {
        if (_previous != null && _previous != Code) Lang.ChangeLanguage(_previous);
    }
}
