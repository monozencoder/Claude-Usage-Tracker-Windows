using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using static ClaudeUsageTracker.App.Interop.NativeMethods;

namespace ClaudeUsageTracker.App.Themes;

/// <summary>
/// Swaps the palette dictionary (Themes/Colors.*.xaml) merged at index 0 of
/// Application.Resources. Every palette brush is referenced via DynamicResource,
/// so open windows repaint immediately. In <see cref="AppTheme.System"/> mode it
/// follows Windows' "app mode" setting and re-applies when that changes.
/// </summary>
public static class ThemeManager
{
    // Undocumented but stable since Windows 10 20H1; toggles the native title bar
    // between light and dark so it matches the window content.
    private const int DwmwaUseImmersiveDarkMode = 20;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppTheme _mode = AppTheme.System;
    private static bool _listening;

    /// <summary>Matches the palette merged by App.xaml before <see cref="Apply"/> is first called.</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised on the UI thread after the palette switches between light and dark.</summary>
    public static event Action? Changed;

    public static void Apply(AppTheme mode)
    {
        _mode = mode;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }
        Update();
    }

    public static void Shutdown()
    {
        if (!_listening)
            return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _listening = false;
    }

    /// <summary>Makes a standard-chrome window's title bar match the current theme, now and on later changes.</summary>
    public static void TrackTitleBar(Window window)
    {
        void ApplyToWindow() => SetDarkTitleBar(window, IsDark);

        window.SourceInitialized += (_, _) => ApplyToWindow();
        Changed += ApplyToWindow;
        window.Closed += (_, _) => Changed -= ApplyToWindow;
    }

    private static void Update()
    {
        var dark = _mode switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => !SystemUsesLightTheme()
        };
        if (dark == IsDark)
            return;

        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Colors.{(dark ? "Dark" : "Light")}.xaml")
        };
        System.Windows.Application.Current.Resources.MergedDictionaries[0] = palette;
        IsDark = dark;
        Changed?.Invoke();
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Light/dark app-mode toggles arrive as the General category, on a SystemEvents thread.
        if (e.Category == UserPreferenceCategory.General && _mode == AppTheme.System)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(Update);
    }

    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    private static void SetDarkTitleBar(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
