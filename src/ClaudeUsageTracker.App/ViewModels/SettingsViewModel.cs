using System.Globalization;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.App.Themes;
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
    private bool _saved;

    // Last in-range value, restored if the box is left empty.
    private int _lastValidRefreshInterval;

    public SettingsViewModel(AppSettingsStore settingsStore, ILaunchAtLoginService launchAtLoginService, UsageFetcher usageFetcher)
    {
        _settingsStore = settingsStore;
        _launchAtLoginService = launchAtLoginService;
        _usageFetcher = usageFetcher;

        var settings = settingsStore.Current;
        RefreshIntervalSeconds = _lastValidRefreshInterval = settings.RefreshIntervalSeconds;
        NotificationsEnabled = settings.NotificationsEnabled;
        ShowFlyoutOnStartup = settings.ShowFlyoutOnStartup;
        LaunchAtLoginEnabled = launchAtLoginService.IsEnabled;
        Theme = _savedTheme = settings.Theme;
    }

    /// <summary>Raised after the settings have been written to disk.</summary>
    public event Action? Saved;

    /// <summary>Raw text of the interval box. Kept as a string so a half-typed or empty value doesn't fight the binding.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _refreshIntervalText = string.Empty;

    /// <summary>Why the typed interval can't be saved, or null when it's fine (nothing is shown then).</summary>
    public string? RefreshIntervalError => ParsedRefreshInterval switch
    {
        null => $"Enter {MinInterval}–{MaxInterval} seconds",
        < MinInterval => $"Minimum is {MinInterval} seconds",
        > MaxInterval => $"Maximum is {MaxInterval} seconds",
        _ => null
    };

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
    private AppTheme _theme;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TestConnectionCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _credentialsSummary = "Checking for Claude Code CLI credentials...";

    private int? ParsedRefreshInterval =>
        int.TryParse(RefreshIntervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;

    private bool IsRefreshIntervalValid => RefreshIntervalError is null;

    partial void OnRefreshIntervalTextChanged(string value)
    {
        if (IsRefreshIntervalValid)
            _lastValidRefreshInterval = ParsedRefreshInterval!.Value;
    }

    // Live preview; DiscardUnsavedPreview puts the saved theme back if the window is closed without saving.
    partial void OnThemeChanged(AppTheme value) => ThemeManager.Apply(value);

    /// <summary>Snaps the typed value into range (or restores the last valid one if the box is empty).</summary>
    public void NormalizeRefreshInterval() => RefreshIntervalSeconds = RefreshIntervalSeconds;

    /// <summary>Call when the window closes: reverts the previewed theme unless it was saved.</summary>
    public void DiscardUnsavedPreview()
    {
        if (!_saved)
            ThemeManager.Apply(_savedTheme);
    }

    public async Task LoadCredentialsSummaryAsync()
    {
        // Resolving may shell out to wsl.exe / the Claude CLI, so keep it off the UI thread.
        var resolution = await Task.Run(ClaudeCredentialResolver.Resolve);
        CredentialsSummary = resolution switch
        {
            { Credentials: not null } => "Credentials found and look valid.",
            { CredentialsFileFound: true } => "Credentials found, but the token is expired. Run \"claude\" in a terminal to refresh it.",
            _ => "No credentials found on Windows or in any installed WSL distro. Install Claude Code and run \"claude\" to sign in."
        };
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
        StatusMessage = "Testing connection...";
        StatusIsError = false;
        try
        {
            var result = await _usageFetcher.FetchAsync();
            StatusIsError = result.Usage is null;
            StatusMessage = result.Usage is { } usage
                ? $"Connected. Session usage: {usage.SessionPercentage:0.#}%, weekly: {usage.WeeklyPercentage:0.#}%."
                : result.Error;

            await LoadCredentialsSummaryAsync();
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
        settings.Theme = Theme;
        _settingsStore.Save();
        _launchAtLoginService.SetEnabled(LaunchAtLoginEnabled);

        _saved = true;
        Saved?.Invoke();
    }

    private bool CanSave() => !IsBusy && IsRefreshIntervalValid;
}
