using System.Collections.ObjectModel;
using System.Globalization;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.App.ViewModels;

// The notification thresholds: a short list of percentages, edited as chips.
public partial class SettingsViewModel
{
    /// <summary>The usage percentages a notification is sent at, ascending; each is a chip that can be removed.</summary>
    public ObservableCollection<int> NotificationThresholds { get; } = [];

    /// <summary>What's typed in the box that adds a threshold.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddThresholdCommand))]
    private string _newThresholdText = string.Empty;

    /// <summary>Whether there's room for another threshold; the box that adds one is hidden when there isn't.</summary>
    public bool CanAddMoreThresholds => NotificationThresholds.Count < AppSettings.MaxNotificationThresholds;

    public string NotificationThresholdsHint => Loc.Format("Settings_ThresholdsDescription", AppSettings.MaxNotificationThresholds);

    [RelayCommand(CanExecute = nameof(CanAddThreshold))]
    private void AddThreshold()
    {
        var added = ParsedNewThreshold!.Value;
        NewThresholdText = string.Empty;
        SetThresholds(NotificationThresholds.Append(added));
    }

    private bool CanAddThreshold() =>
        CanAddMoreThresholds && ParsedNewThreshold is { } threshold && !NotificationThresholds.Contains(threshold);

    // At least one stays: with none there would be nothing to notify about but the resets.
    [RelayCommand(CanExecute = nameof(CanRemoveThreshold))]
    private void RemoveThreshold(int threshold) => SetThresholds(NotificationThresholds.Where(existing => existing != threshold));

    private bool CanRemoveThreshold(int threshold) => NotificationThresholds.Count > 1;

    [RelayCommand(CanExecute = nameof(CanResetThresholds))]
    private void ResetThresholds() => SetThresholds(AppSettings.DefaultNotificationThresholds);

    private bool CanResetThresholds() => !NotificationThresholds.SequenceEqual(AppSettings.DefaultNotificationThresholds);

    /// <summary>Tooltip of the reset button, naming the defaults, e.g. "Reset to 75%, 90%, 95%".</summary>
    public string ResetThresholdsToolTip => Loc.Format("Settings_ThresholdsReset",
        string.Join(Loc.Get("Settings_ListSeparator"), AppSettings.DefaultNotificationThresholds.Select(threshold => $"{threshold}%")));

    // The typed percentage if it's a whole number within 1-100. A full-width number (an IME left on) is accepted.
    private int? ParsedNewThreshold =>
        int.TryParse(NewThresholdText.Normalize(System.Text.NormalizationForm.FormKC).Trim().TrimEnd('%'),
            NumberStyles.None, CultureInfo.InvariantCulture, out var threshold) && threshold is >= 1 and <= 100
            ? threshold
            : null;

    private void SetThresholds(IEnumerable<int> thresholds)
    {
        LoadThresholds([.. thresholds.Order()]);
        Apply();
    }

    /// <summary>Shows <paramref name="thresholds"/> (ascending) as the chips, without saving them.</summary>
    private void LoadThresholds(IReadOnlyList<int> thresholds)
    {
        if (NotificationThresholds.SequenceEqual(thresholds))
            return;

        NotificationThresholds.Clear();
        foreach (var threshold in thresholds)
            NotificationThresholds.Add(threshold);

        OnPropertyChanged(nameof(CanAddMoreThresholds));
        AddThresholdCommand.NotifyCanExecuteChanged();
        RemoveThresholdCommand.NotifyCanExecuteChanged();
        ResetThresholdsCommand.NotifyCanExecuteChanged();
    }
}
