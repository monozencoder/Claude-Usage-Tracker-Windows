using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace ClaudeUsageTracker.App.Localization;

/// <summary>
/// UI strings from Strings.resx (English) / Strings.ja.resx. XAML binds through the
/// indexer (see <see cref="TrExtension"/>), so switching language with <see cref="Apply"/>
/// updates every open window at once; code-built strings listen to <see cref="LanguageChanged"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static readonly ResourceManager Strings =
        new("ClaudeUsageTracker.App.Localization.Strings", typeof(Loc).Assembly);

    // Captured before anything overrides it, so "System" keeps meaning Windows' language.
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    public static CultureInfo Culture { get; private set; } = Resolve(AppLanguage.System);

    /// <summary>Raised after <see cref="Apply"/> switches to a different language.</summary>
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Strings.GetString(key, Culture) ?? key;

    public static string Get(string key) => Instance[key];

    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    public static void Apply(AppLanguage language)
    {
        var culture = Resolve(language);
        if (Equals(culture, Culture))
            return;

        Culture = culture;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    private static CultureInfo Resolve(AppLanguage language) => language switch
    {
        AppLanguage.Japanese => Japanese,
        AppLanguage.English => English,
        _ => SystemCulture.TwoLetterISOLanguageName == "ja" ? Japanese : English
    };
}
