using System.Globalization;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Settings;
using ClaudeUsageTracker.Core.Api;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

// The Usage page: the fetch mode and, for the token-using one, the refresh interval.
public partial class SettingsViewModel
{
    /// <summary>Amount the -/+ buttons, arrow keys and mouse wheel change the interval by.</summary>
    public const int RefreshIntervalStep = 5;

    private const int MinInterval = AppSettings.MinRefreshIntervalSeconds;
    private const int MaxInterval = AppSettings.MaxRefreshIntervalSeconds;

    // Rough cost of one token-using refresh: a "." prompt (~8 input tokens) with max_tokens = 1.
    private const int ApproxTokensPerRefresh = 10;

    // Last in-range value, restored if the box is left empty.
    private int _lastValidRefreshInterval;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalError), nameof(RefreshIntervalHint), nameof(HasRefreshIntervalError), nameof(IsRefreshIntervalEditable))]
    private bool _avoidTokenUsage;

    partial void OnAvoidTokenUsageChanged(bool value) => Apply();

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

    private int? ParsedRefreshInterval =>
        int.TryParse(RefreshIntervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;

    private bool IsRefreshIntervalValid => RefreshIntervalError is null;

    partial void OnRefreshIntervalTextChanged(string value)
    {
        if (IsRefreshIntervalValid)
            _lastValidRefreshInterval = ParsedRefreshInterval!.Value;
    }

    /// <summary>
    /// Snaps the typed value into range (or restores the last valid one if the box is empty)
    /// and commits it. Called when focus leaves the box.
    /// </summary>
    public void NormalizeRefreshInterval() => RefreshIntervalSeconds = RefreshIntervalSeconds;

    [RelayCommand]
    private void IncreaseRefreshInterval() =>
        RefreshIntervalSeconds = (RefreshIntervalSeconds / RefreshIntervalStep + 1) * RefreshIntervalStep;

    [RelayCommand]
    private void DecreaseRefreshInterval() =>
        RefreshIntervalSeconds = (RefreshIntervalSeconds - 1) / RefreshIntervalStep * RefreshIntervalStep;
}
