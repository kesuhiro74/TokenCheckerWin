using System.Globalization;

namespace TokenChecker.App;

// Resolves the AppLanguage setting to a concrete English/Japanese choice, once at
// startup (mirrors WindowsTheme.ResolveDark). System follows the OS UI language.
internal static class LanguageResolver
{
    public static bool ResolveJapanese(AppLanguage setting)
        => ResolveJapanese(setting, CultureInfo.CurrentUICulture);

    // Internal overload so the System branch is unit-testable with an explicit culture.
    internal static bool ResolveJapanese(AppLanguage setting, CultureInfo uiCulture)
        => setting switch
        {
            AppLanguage.Japanese => true,
            AppLanguage.English => false,
            _ => string.Equals(uiCulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase),
        };
}
