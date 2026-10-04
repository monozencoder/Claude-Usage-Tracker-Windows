using System.Globalization;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.Themes;
using ClaudeUsageTracker.Core.Api;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Platform.ClaudeCode;
using ClaudeUsageTracker.Platform.Startup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

/// <summary>
/// One entry of a settings drop-down: the value it selects and its label, either a string key
/// (translated, so it follows the UI language) or fixed text (e.g. a language's own name).
/// </summary>
public sealed record ChoiceOption(object Value, string? TextKey = null, string? Text = null)
{
    public string Label => TextKey is { } key ? Loc.Get(key) : Text ?? string.Empty;
}

/// <summary>
/// State and actions for SettingsWindow. Every change is written to
/// <see cref="AppSettingsStore.Current"/> as soon as it's made (Windows 11 Settings style);
/// there is no Save/Cancel. The interval is the exception: it's written when stepped or
/// committed (focus leaves the box / the window closes), not on every keystroke.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    /// <summary>Amount the -/+ buttons, arrow keys and mouse wheel change the interval by.</summary>
    public const int RefreshIntervalStep = 5;

    private const int MinInterval = AppSettings.MinRefreshIntervalSeconds;
    private const int MaxInterval = AppSettings.MaxRefreshIntervalSeconds;

    // Rough cost of one token-using refresh: a "." prompt (~8 input tokens) with max_tokens = 1.
    private const int ApproxTokensPerRefresh = 10;

    private readonly AppSettingsStore _settingsStore;
    private readonly ILaunchAtLoginService _launchAtLoginService;
    private readonly UsageFetcher _usageFetcher;
    private readonly AppSettings _initialSettings;
    private readonly bool _initializing = true;

    // Last in-range value, restored if the box is left empty.
    private int _lastValidRefreshInterval;

    public SettingsViewModel(AppSettingsStore settingsStore, ILaunchAtLoginService launchAtLoginService, UsageFetcher usageFetcher)
    {
        _settingsStore = settingsStore;
        _launchAtLoginService = launchAtLoginService;
        _usageFetcher = usageFetcher;

        var settings = settingsStore.Current;
        _initialSettings = new AppSettings { AvoidTokenUsage = settings.AvoidTokenUsage, RefreshIntervalSeconds = settings.RefreshIntervalSeconds };
        AvoidTokenUsage = settings.AvoidTokenUsage;
        RefreshIntervalSeconds = _lastValidRefreshInterval = settings.RefreshIntervalSeconds;
        NotificationsEnabled = settings.NotificationsEnabled;
        ShowFlyoutOnStartup = settings.ShowFlyoutOnStartup;
        AlwaysOnTop = settings.AlwaysOnTop;
        FlyoutOpacityPercent = (int)Math.Round(settings.FlyoutOpacity * 100);
        LaunchAtLoginEnabled = launchAtLoginService.IsEnabled;
        Theme = settings.Theme;
        Language = settings.Language;

        Loc.LanguageChanged += OnUiLanguageChanged;
        _initializing = false;
    }

    public static IReadOnlyList<ChoiceOption> ThemeOptions { get; } =
    [
        new(AppTheme.System, "Settings_ThemeSystem"),
        new(AppTheme.Light, "Settings_ThemeLight"),
        new(AppTheme.Dark, "Settings_ThemeDark"),
    ];

    // Language names stay in their own language so each is findable whatever the UI shows.
    public static IReadOnlyList<ChoiceOption> LanguageOptions { get; } =
    [
        new(AppLanguage.System, "Settings_LanguageSystem"),
        new(AppLanguage.English, Text: "English"),
        new(AppLanguage.Japanese, Text: "日本語"),
    ];

    /// <summary>Raised after a change has been written to disk.</summary>
    public event Action? Applied;

    /// <summary>
    /// Whether how usage is fetched (mode or interval) differs from when the window opened,
    /// so the caller can refresh once on close rather than on every click.
    /// </summary>
    public bool FetchSettingsChanged =>
        _settingsStore.Current.AvoidTokenUsage != _initialSettings.AvoidTokenUsage
        || _settingsStore.Current.RefreshInterval != _initialSettings.RefreshInterval;

    /// <summary>Raised with each Test connection result, so the flyout and tray can show it too.</summary>
    public event Action<UsageFetchResult>? ConnectionTested;

    /// <summary>Raw text of the interval box. Kept as a string so a half-typed or empty value doesn't fight the binding.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalError), nameof(RefreshIntervalHint), nameof(HasRefreshIntervalError), nameof(TokensPerHourText))]
    private string _refreshIntervalText = string.Empty;

    /// <summary>Why the typed interval can't be saved, or null when it's fine (or not in use: token-free mode).</summary>
    public string? RefreshIntervalError => AvoidTokenUsage ? null : ParsedRefreshInterval switch
    {
        null => Loc.Format("Settings_IntervalEnter", MinInterval, MaxInterval),
        < MinInterval => Loc.Format("Settings_IntervalMin", MinInterval),
        > MaxInterval => Loc.Format("Settings_IntervalMax", MaxInterval),
        _ => null
    };

    public bool HasRefreshIntervalError => RefreshIntervalError is not null;

    /// <summary>
    /// Always shown beside the interval box: the error if there is one, otherwise the per-refresh
    /// cost. Fixed text (it doesn't follow the value or the mode); the value-dependent estimate is
    /// <see cref="TokensPerHourText"/>, shown under the box. The allowed range only surfaces in the
    /// error and <see cref="RefreshIntervalToolTip"/>, since stepping can't leave it anyway.
    /// </summary>
    public string RefreshIntervalHint => RefreshIntervalError
        ?? Loc.Format("Settings_IntervalTokensPerRefresh", ApproxTokensPerRefresh);

    public string RefreshIntervalToolTip => Loc.Format("Settings_IntervalToolTip", MinInterval, MaxInterval);

    /// <summary>Which model token-using refreshes prompt, e.g. "使用モデル: Claude Haiku 4.5 (自動選択)".</summary>
    public string ProbeModelText => Loc.Format("Settings_ProbeModel", ModelNames.ToDisplayName(_usageFetcher.ProbeModel));

    /// <summary>Rough tokens an hour at the typed interval, e.g. "約 600 トークン/時".</summary>
    public string TokensPerHourText => Loc.Format("Settings_TokensPerHour",
        Math.Round(3600.0 / RefreshIntervalSeconds * ApproxTokensPerRefresh).ToString("N0", Loc.Culture));

    /// <summary>The interval box only applies in the default (token-using) mode.</summary>
    public bool IsRefreshIntervalEditable => !AvoidTokenUsage;

    /// <summary>
    /// The typed value clamped to the allowed range. Setting it (step buttons, normalizing)
    /// commits the interval; typing alone doesn't.
    /// </summary>
    public int RefreshIntervalSeconds
    {
        get => ParsedRefreshInterval is { } s ? Math.Clamp(s, MinInterval, MaxInterval) : _lastValidRefreshInterval;
        set
        {
            RefreshIntervalText = Math.Clamp(value, MinInterval, MaxInterval).ToString(CultureInfo.InvariantCulture);
            Apply();
        }
    }

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private bool _launchAtLoginEnabled;

    [ObservableProperty]
    private bool _showFlyoutOnStartup;

    [ObservableProperty]
    private bool _alwaysOnTop;

    // Slider range (as doubles: Slider.Minimum/Maximum don't take an int).
    public double MinFlyoutOpacityPercent => AppSettings.MinFlyoutOpacityPercent;
    public double MaxFlyoutOpacityPercent => AppSettings.MaxFlyoutOpacityPercent;

    /// <summary>Flyout opacity in percent; the slider previews it live on an open flyout.</summary>
    [ObservableProperty]
    private int _flyoutOpacityPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalError), nameof(RefreshIntervalHint), nameof(HasRefreshIntervalError), nameof(IsRefreshIntervalEditable))]
    private bool _avoidTokenUsage;

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private AppLanguage _language;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    private bool _isBusy;

    /// <summary>Which Claude account is being tracked (or why none is).</summary>
    [ObservableProperty]
    private string _accountSummary = Loc.Get("Settings_CheckingSignIn");

    /// <summary>"Sign in" / "Switch account", or null to hide the button (e.g. Claude Code isn't installed).</summary>
    [ObservableProperty]
    private string? _accountActionText;

    [ObservableProperty]
    private bool _showInstallLink;

    private bool _loadingAccount;

    // Result of the last account lookup, kept so a language switch can re-render it without the CLI.
    private AccountInfo? _account;

    private sealed record AccountInfo(ClaudeCredentialResolver.Result Resolution, bool Installed, ClaudeAuthStatus? Status);

    private int? ParsedRefreshInterval =>
        int.TryParse(RefreshIntervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;

    private bool IsRefreshIntervalValid => RefreshIntervalError is null;

    partial void OnRefreshIntervalTextChanged(string value)
    {
        if (IsRefreshIntervalValid)
            _lastValidRefreshInterval = ParsedRefreshInterval!.Value;
    }

    partial void OnThemeChanged(AppTheme value)
    {
        ThemeManager.Apply(value);
        Apply();
    }

    partial void OnLanguageChanged(AppLanguage value)
    {
        Loc.Apply(value);
        Apply();
    }

    partial void OnNotificationsEnabledChanged(bool value) => Apply();

    partial void OnShowFlyoutOnStartupChanged(bool value) => Apply();

    partial void OnAlwaysOnTopChanged(bool value) => Apply();

    partial void OnFlyoutOpacityPercentChanged(int value) => Apply();

    partial void OnAvoidTokenUsageChanged(bool value) => Apply();

    partial void OnLaunchAtLoginEnabledChanged(bool value)
    {
        if (!_initializing)
            _launchAtLoginService.SetEnabled(value);
    }

    // XAML text follows Loc on its own; strings built here have to be rebuilt. The account
    // text is rebuilt from the last lookup rather than re-running the (slow) CLI, so it
    // switches language at the same moment as everything else.
    private void OnUiLanguageChanged()
    {
        OnPropertyChanged(nameof(RefreshIntervalError));
        OnPropertyChanged(nameof(RefreshIntervalHint));
        OnPropertyChanged(nameof(TokensPerHourText));
        OnPropertyChanged(nameof(RefreshIntervalToolTip));
        OnPropertyChanged(nameof(ProbeModelText));
        StatusMessage = null;
        if (_account is { } account)
            ApplyAccount(account);
        else
            AccountSummary = Loc.Get("Settings_CheckingSignIn");
    }

    /// <summary>
    /// Snaps the typed value into range (or restores the last valid one if the box is empty)
    /// and commits it. Called when focus leaves the box.
    /// </summary>
    public void NormalizeRefreshInterval() => RefreshIntervalSeconds = RefreshIntervalSeconds;

    /// <summary>Call when the window closes: commits a half-typed interval and detaches.</summary>
    public void Close()
    {
        NormalizeRefreshInterval();
        Loc.LanguageChanged -= OnUiLanguageChanged;
    }

    /// <summary>Writes the current state to disk.</summary>
    private void Apply()
    {
        if (_initializing)
            return;

        var settings = _settingsStore.Current;
        settings.RefreshIntervalSeconds = _lastValidRefreshInterval;
        settings.NotificationsEnabled = NotificationsEnabled;
        settings.ShowFlyoutOnStartup = ShowFlyoutOnStartup;
        settings.AlwaysOnTop = AlwaysOnTop;
        settings.FlyoutOpacityPercent = FlyoutOpacityPercent;
        settings.AvoidTokenUsage = AvoidTokenUsage;
        settings.Theme = Theme;
        settings.Language = Language;
        _settingsStore.Save();
        Applied?.Invoke();
    }

    /// <summary>
    /// Refreshes the account section. Uses <c>claude auth status</c>, which only reads
    /// Claude Code's local state (no prompt, no usage consumed). Resolving may shell out to
    /// wsl.exe / the Claude CLI, so it runs off the UI thread.
    /// </summary>
    public async Task LoadAccountAsync()
    {
        if (_loadingAccount)
            return;
        _loadingAccount = true;
        try
        {
            _account = await Task.Run(() =>
            {
                var resolution = ClaudeCredentialResolver.Resolve();
                var installed = ClaudeCli.IsInstalled;
                return new AccountInfo(resolution, installed, installed ? ClaudeCli.GetAuthStatus() : null);
            });
            ApplyAccount(_account);
        }
        finally
        {
            _loadingAccount = false;
        }
    }

    /// <summary>Builds the account section's text from a lookup, in the current UI language.</summary>
    private void ApplyAccount(AccountInfo account)
    {
        var (resolution, installed, status) = account;
        var source = resolution.Source;
        var signedIn = resolution.Credentials is not null;
        AccountSummary = source switch
        {
            { IsWsl: true } when signedIn => Loc.Format("Settings_UsingWsl", source.DisplayName),
            { IsWsl: true } => Loc.Format("Settings_WslExpired", source.DisplayName),
            not null when signedIn => DescribeSignedIn(status),
            not null => Loc.Get("Settings_Expired"),
            null when installed => Loc.Get("Error_NotSignedIn"),
            null => Loc.Get("Settings_NotInstalled")
        };
        AccountActionText = !installed ? null
            : signedIn && source is { IsWsl: false } ? Loc.Get("Settings_SwitchAccount")
            : Loc.Get("Settings_SignIn");
        ShowInstallLink = !installed;
    }

    private static string DescribeSignedIn(ClaudeAuthStatus? status) => status switch
    {
        { Email: { } email, SubscriptionDisplayName: { } plan } => Loc.Format("Settings_SignedInAsWithPlan", email, plan),
        { Email: { } email } => Loc.Format("Settings_SignedInAs", email),
        _ => Loc.Get("Settings_SignedIn")
    };

    [RelayCommand]
    private void SignIn()
    {
        var started = ClaudeCli.StartLogin();
        StatusIsError = !started;
        StatusMessage = started
            ? Loc.Get("Settings_FinishSignIn")
            : Loc.Get("Error_CouldNotStartClaude");
    }

    [RelayCommand]
    private void IncreaseRefreshInterval() =>
        RefreshIntervalSeconds = (RefreshIntervalSeconds / RefreshIntervalStep + 1) * RefreshIntervalStep;

    [RelayCommand]
    private void DecreaseRefreshInterval() =>
        RefreshIntervalSeconds = (RefreshIntervalSeconds - 1) / RefreshIntervalStep * RefreshIntervalStep;

    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        StatusMessage = Loc.Get("Settings_Testing");
        StatusIsError = false;
        try
        {
            // Tests the mode currently ticked here, even before it's saved.
            var result = await _usageFetcher.FetchAsync(AvoidTokenUsage);
            // Held back only because it was checked moments ago isn't a failure; everything else without usage is.
            StatusIsError = result is { Usage: null } and not { RetryAfter: not null, UsageEndpointRateLimited: false };
            StatusMessage = result switch
            {
                { RetryAfter: { } wait, UsageEndpointRateLimited: false } => Loc.Format("Settings_TestAvailableIn", (int)Math.Ceiling(wait.TotalMinutes)),
                { Usage: { } usage } => Loc.Format("Settings_Connected", usage.SessionPercentage, usage.WeeklyPercentage),
                { UsageEndpointRateLimited: true } => Loc.Get("Settings_ConnectedRateLimited"),
                _ => result.Error
            };
            ConnectionTested?.Invoke(result);
            OnPropertyChanged(nameof(ProbeModelText)); // the test may have switched away from a retired model

            await LoadAccountAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanTestConnection() => !IsBusy;
}
