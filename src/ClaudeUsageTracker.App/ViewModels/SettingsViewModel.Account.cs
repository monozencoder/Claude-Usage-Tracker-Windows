using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Services;
using ClaudeUsageTracker.Core.ClaudeCode;
using ClaudeUsageTracker.Core.Usage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

// The Account page: which Claude account is tracked, signing in, and Test connection.
public partial class SettingsViewModel
{
    /// <summary>Raised with each Test connection result, so the flyout and tray can show it too.</summary>
    public event Action<UsageFetchResult>? ConnectionTested;

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

    // The last lookup by any settings window (or PreloadAccountAsync), so the next window opens
    // with the account already filled in rather than "Checking…" and no button until the CLI answers.
    private static AccountInfo? s_lastAccount;

    private sealed record AccountInfo(ClaudeCredentialLookup Lookup, bool Installed, ClaudeAuthStatus? Status);

    /// <summary>
    /// Looks the account up ahead of the first settings window, which then shows it at once.
    /// </summary>
    public static async Task PreloadAccountAsync(IClaudeCodeEnvironment claudeCode) =>
        s_lastAccount = await LookUpAccountAsync(claudeCode);

    // Uses `claude auth status`, which only reads Claude Code's local state (no prompt, no usage
    // consumed). The lookup may shell out to wsl.exe / the Claude CLI, so it runs off the UI thread.
    private static Task<AccountInfo> LookUpAccountAsync(IClaudeCodeEnvironment claudeCode) => Task.Run(() =>
    {
        var lookup = claudeCode.FindCredentials();
        var installed = claudeCode.IsCliInstalled;
        return new AccountInfo(lookup, installed, installed ? claudeCode.GetAuthStatus() : null);
    });

    // Shows the last known account straight away; LoadAccountAsync then brings it up to date.
    private void ShowLastAccount()
    {
        if (s_lastAccount is not { } account)
            return;
        _account = account;
        ApplyAccount(account);
    }

    /// <summary>Refreshes the account section.</summary>
    public async Task LoadAccountAsync()
    {
        if (_loadingAccount)
            return;
        _loadingAccount = true;
        try
        {
            s_lastAccount = _account = await LookUpAccountAsync(_claudeCode);
            ApplyAccount(_account);
        }
        finally
        {
            _loadingAccount = false;
        }
    }

    // Rebuilt from the last lookup, in the new language.
    private void RefreshAccountLanguage()
    {
        if (_account is { } account)
            ApplyAccount(account);
        else
            AccountSummary = Loc.Get("Settings_CheckingSignIn");
    }

    /// <summary>Builds the account section's text from a lookup, in the current UI language.</summary>
    private void ApplyAccount(AccountInfo account)
    {
        var (lookup, installed, status) = account;
        var location = lookup.Location;
        var signedIn = lookup.Credentials is not null;
        AccountSummary = location switch
        {
            { IsWsl: true } when signedIn => Loc.Format("Settings_UsingWsl", location.DisplayName),
            { IsWsl: true } => Loc.Format("Settings_WslExpired", location.DisplayName),
            not null when signedIn => DescribeSignedIn(status),
            not null => Loc.Get("Settings_Expired"),
            null when installed => Loc.Get("Error_NotSignedIn"),
            null => Loc.Get("Settings_NotInstalled")
        };
        AccountActionText = !installed ? null
            : signedIn && location is { IsWsl: false } ? Loc.Get("Settings_SwitchAccount")
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
        var started = _claudeCode.StartLogin();
        StatusIsError = !started;
        StatusMessage = started
            ? Loc.Get("Settings_FinishSignIn")
            : Loc.Get("Error_CouldNotStartClaude");
    }

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
                _ => UsageFetchErrorText.Describe(result)
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
