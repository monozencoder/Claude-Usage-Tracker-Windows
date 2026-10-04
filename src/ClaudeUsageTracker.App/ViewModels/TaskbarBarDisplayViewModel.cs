using System.Text.RegularExpressions;
using ClaudeUsageTracker.App.Localization;
using ClaudeUsageTracker.App.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinForms = System.Windows.Forms;

namespace ClaudeUsageTracker.App.ViewModels;

/// <summary>
/// One monitor's row in the settings: whether its taskbar gets the usage bars, and how far
/// they're shifted. Named by Windows' display number, and told apart further by its resolution
/// and where it sits, since "the other monitor" means nothing once there are three.
/// </summary>
public sealed partial class TaskbarBarDisplayViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _apply;
    private readonly int _number;
    private readonly bool _isMain;
    private readonly System.Drawing.Size _size;

    // Place among the monitors from left to right (0-based), and how many there are.
    private readonly int _index;
    private readonly int _count;

    private bool _show;
    private int _offset;

    private TaskbarBarDisplayViewModel(AppSettings settings, Action apply, WinForms.Screen screen, int number, int index, int count)
    {
        _settings = settings;
        _apply = apply;
        DeviceName = screen.DeviceName;
        _number = number;
        _isMain = screen.Primary;
        _size = screen.Bounds.Size;
        _index = index;
        _count = count;

        var saved = settings.TaskbarBarDisplays.GetValueOrDefault(DeviceName);
        _show = saved?.Show ?? true;
        _offset = Math.Clamp(saved?.Offset ?? 0, AppSettings.MinTaskbarBarOffset, AppSettings.MaxTaskbarBarOffset);
    }

    /// <summary>The monitors connected right now, in Windows' display-number order.</summary>
    /// <param name="apply">Saves the settings and tells the app they changed.</param>
    public static IEnumerable<TaskbarBarDisplayViewModel> ForConnectedMonitors(AppSettings settings, Action apply)
    {
        var screens = WinForms.Screen.AllScreens;
        var leftToRight = screens.OrderBy(screen => screen.Bounds.Left).ThenBy(screen => screen.Bounds.Top).ToList();
        return screens
            .Select((screen, i) => (screen, number: DisplayNumber(screen.DeviceName) ?? i + 1))
            .OrderBy(display => display.number)
            .Select(display => new TaskbarBarDisplayViewModel(
                settings, apply, display.screen, display.number, leftToRight.IndexOf(display.screen), screens.Length));
    }

    /// <summary>The monitor's device name (e.g. "\\.\DISPLAY2"), the key of its settings.</summary>
    public string DeviceName { get; }

    // Slider range (as doubles: Slider.Minimum/Maximum don't take an int).
    public double MinOffset => AppSettings.MinTaskbarBarOffset;
    public double MaxOffset => AppSettings.MaxTaskbarBarOffset;

    public string Title => Loc.Format(_isMain ? "Settings_TaskbarBarDisplayMain" : "Settings_TaskbarBarDisplay", _number);

    public string Description => Loc.Format("Settings_TaskbarBarDisplayDescription", _size.Width, _size.Height, Position);

    public bool Show
    {
        get => _show;
        set
        {
            if (SetProperty(ref _show, value))
                Save();
        }
    }

    /// <summary>Horizontal shift of the bars on this monitor (negative is left); the slider moves them live.
    /// Also set by the caller when the bars are dragged along the taskbar while this window is open.</summary>
    public int Offset
    {
        get => _offset;
        set
        {
            if (!SetProperty(ref _offset, value))
                return;
            ResetOffsetCommand.NotifyCanExecuteChanged();
            Save();
        }
    }

    /// <summary>Puts the bars back where they're placed automatically.</summary>
    [RelayCommand(CanExecute = nameof(CanResetOffset))]
    private void ResetOffset() => Offset = 0;

    private bool CanResetOffset() => Offset != 0;

    /// <summary>Rebuilds the text after the UI language changed.</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }

    private string Position =>
        _count == 1 ? Loc.Get("Settings_DisplayPositionOnly")
        : _index == 0 ? Loc.Get("Settings_DisplayPositionLeft")
        : _index == _count - 1 ? Loc.Get("Settings_DisplayPositionRight")
        : Loc.Format("Settings_DisplayPositionMiddle", _index + 1);

    private void Save()
    {
        _settings.TaskbarBarDisplays[DeviceName] = new TaskbarBarDisplaySettings { Show = _show, Offset = _offset };
        _apply();
    }

    // "\\.\DISPLAY2" -> 2, the number Windows' display settings show for it.
    private static int? DisplayNumber(string deviceName) =>
        Regex.Match(deviceName, @"DISPLAY(\d+)") is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;
}
