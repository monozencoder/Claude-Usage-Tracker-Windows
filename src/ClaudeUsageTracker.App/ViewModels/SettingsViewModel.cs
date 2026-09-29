using System.Globalization;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.Themes;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Platform.ClaudeCode;
using ClaudeUsageTracker.Platform.Startup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

/// <summary>
/// State and actions for SettingsWindow. Edits are held here and only written to
/// <see cref="AppSettingsStore.Current"/> on <see cref="SaveCommand"/>; the theme is
/// previewed live and reverted by <see cref="DiscardUnsavedPreview"/> if not saved.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    /// <summary>Amount the -/+ buttons, arrow keys and mouse wheel change the interval by.</summary>
    public const int RefreshIntervalStep = 5;

    private const int MinInterval = AppSettings.MinRefreshIntervalSeconds;
    private const int MaxInterval = AppSettings.MaxRefreshIntervalSeconds;

    private readonly AppSettingsStore _settingsStore;
    private readonly ILaunchAtLoginService _launchAtLoginService;
    private readonly UsageFetcher _usageFetcher;
    private readonly AppTheme _savedTheme;
    private readonly AppLanguage _savedLanguage;
    private bool _saved;

    // Last in-range value, restored if the box is left empty.
    private int _lastValidRefreshInterval;

    public SettingsViewModel(AppSettingsStore settingsStore, ILaunchAtLoginService launchAtLoginService, UsageFetcher usageFetcher)
    {
        _settingsStore = settingsStore;
        _launchAtLoginService = launchAtLoginService;
        _usageFetcher = usageFetcher;

        var settings = settingsStore.Current;
        AvoidTokenUsage = settings.AvoidTokenUsage;
        RefreshIntervalSeconds = _lastValidRefreshInterval = settings.RefreshIntervalSeconds;
        NotificationsEnabled = settings.NotificationsEnabled;
        ShowFlyoutOnStartup = settings.ShowFlyoutOnStartup;
        AlwaysOnTop = settings.AlwaysOnTop;
        LaunchAtLoginEnabled = launchAtLoginService.IsEnabled;
        Theme = _savedTheme = settings.Theme;
        Language = _savedLanguage = settings.Language;

        Loc.LanguageChanged += OnUiLanguageChanged;
    }

    /// <summary>Raised after the settings have been written to disk.</summary>
    public event Action? Saved;

    /// <summary>Raised with each Test connection result, so the flyout and tray can show it too.</summary>
    public event Action<UsageFetchResult>? ConnectionTested;

    /// <summary>Raw text of the interval box. Kept as a string so a half-typed or empty value doesn't fight the binding.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalError), nameof(RefreshIntervalHint), nameof(HasRefreshIntervalError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
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
    /// Always shown under the interval box: the error if there is one, otherwise what the
    /// interval does in the current mode (its range and cost, or the fixed token-free interval).
    /// </summary>
    public string RefreshIntervalHint => RefreshIntervalError ?? (AvoidTokenUsage
        ? Loc.Format("Settings_IntervalFixedTokenFree", AppSettings.TokenFreeRefreshIntervalSeconds / 60)
        : Loc.Format("Settings_IntervalRangeTokens", MinInterval, MaxInterval));

    /// <summary>The interval box only applies in the default (token-using) mode.</summary>
    public bool IsRefreshIntervalEditable => !AvoidTokenUsage;

    /// <summary>The interval to persist: the typed value clamped to the allowed range.</summary>
    public int RefreshIntervalSeconds
    {
        get => ParsedRefreshInterval is { } s ? Math.Clamp(s, MinInterval, MaxInterval) : _lastValidRefreshInterval;
        set => RefreshIntervalText = Math.Clamp(value, MinInterval, MaxInterval).ToString(CultureInfo.InvariantCulture);
    }

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private bool _launchAtLoginEnabled;

    [ObservableProperty]
    private bool _showFlyoutOnStartup;

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalError), nameof(RefreshIntervalHint), nameof(HasRefreshIntervalError), nameof(IsRefreshIntervalEditable))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TestConnectionCommand))]
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

    private int? ParsedRefreshInterval =>
        int.TryParse(RefreshIntervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;

    private bool IsRefreshIntervalValid => RefreshIntervalError is null;

    partial void OnRefreshIntervalTextChanged(string value)
    {
        if (IsRefreshIntervalValid)
            _lastValidRefreshInterval = ParsedRefreshInterval!.Value;
    }

    // Live preview; DiscardUnsavedPreview puts the saved theme/language back if the window is closed without saving.
    partial void OnThemeChanged(AppTheme value) => ThemeManager.Apply(value);

    partial void OnLanguageChanged(AppLanguage value) => Loc.Apply(value);

    // XAML text follows Loc on its own; strings built here have to be rebuilt.
    private async void OnUiLanguageChanged()
    {
        OnPropertyChanged(nameof(RefreshIntervalError));
        OnPropertyChanged(nameof(RefreshIntervalHint));
        StatusMessage = null;
        await LoadAccountAsync();
    }

    /// <summary>Snaps the typed value into range (or restores the last valid one if the box is empty).</summary>
    public void NormalizeRefreshInterval() => RefreshIntervalSeconds = RefreshIntervalSeconds;

    /// <summary>Call when the window closes: reverts the previewed theme unless it was saved.</summary>
    public void DiscardUnsavedPreview()
    {
        Loc.LanguageChanged -= OnUiLanguageChanged;
        if (_saved)
            return;

        ThemeManager.Apply(_savedTheme);
        Loc.Apply(_savedLanguage);
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
            var (resolution, installed, status) = await Task.Run(() =>
            {
                var resolution = ClaudeCredentialResolver.Resolve();
                var installed = ClaudeCli.IsInstalled;
                return (resolution, installed, installed ? ClaudeCli.GetAuthStatus() : null);
            });

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
        finally
        {
            _loadingAccount = false;
        }
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
            StatusIsError = result.Usage is null;
            StatusMessage = result switch
            {
                { Usage: { } usage } => Loc.Format("Settings_Connected", usage.SessionPercentage, usage.WeeklyPercentage),
                { UsageEndpointRateLimited: true } => Loc.Get("Settings_ConnectedRateLimited"),
                _ => result.Error
            };
            ConnectionTested?.Invoke(result);

            await LoadAccountAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanTestConnection() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        NormalizeRefreshInterval();

        var settings = _settingsStore.Current;
        settings.RefreshIntervalSeconds = RefreshIntervalSeconds;
        settings.NotificationsEnabled = NotificationsEnabled;
        settings.ShowFlyoutOnStartup = ShowFlyoutOnStartup;
        settings.AlwaysOnTop = AlwaysOnTop;
        settings.AvoidTokenUsage = AvoidTokenUsage;
        settings.Theme = Theme;
        settings.Language = Language;
        _settingsStore.Save();
        _launchAtLoginService.SetEnabled(LaunchAtLoginEnabled);

        _saved = true;
        Saved?.Invoke();
    }

    private bool CanSave() => !IsBusy && IsRefreshIntervalValid;
}
