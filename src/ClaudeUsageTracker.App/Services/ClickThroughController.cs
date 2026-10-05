using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClaudeUsageTracker.App.Services;

/// <summary>
/// Makes windows click-through: the mouse goes to whatever is behind them, as if they weren't
/// there. Such a window can't be reached with the mouse at all, so holding Ctrl suspends it —
/// the window is clickable and draggable again for as long as the key is down.
/// </summary>
public sealed class ClickThroughController : IDisposable
{
    // How often Ctrl is checked. Short enough that pressing it and clicking right away works.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly List<(Func<bool> Enabled, Func<IEnumerable<Window>> Windows)> _targets = [];
    private readonly DispatcherTimer _timer = new() { Interval = PollInterval };

    public ClickThroughController()
    {
        _timer.Tick += (_, _) => Apply();
    }

    /// <summary>
    /// Adds windows to manage. Both are asked again on every check, so the setting can change and
    /// the windows can come and go.
    /// </summary>
    public void Add(Func<bool> enabled, Func<IEnumerable<Window>> windows) => _targets.Add((enabled, windows));

    /// <summary>Applies the current settings now; call after any of them changed.</summary>
    public void Refresh()
    {
        Apply();
        // Nothing to watch Ctrl for while it's off everywhere. Windows created while the timer is
        // stopped start out clickable, which is then what they should be.
        _timer.IsEnabled = _targets.Any(target => target.Enabled());
    }

    public void Dispose() => _timer.Stop();

    private void Apply()
    {
        var suspended = GetAsyncKeyState(VkControl) < 0;
        foreach (var (enabled, windows) in _targets)
        {
            var clickThrough = enabled() && !suspended;
            foreach (var window in windows())
                SetClickThrough(window, clickThrough);
        }
    }

    // WS_EX_TRANSPARENT on a layered window (which AllowsTransparency makes these) takes it out of
    // hit testing entirely.
    private static void SetClickThrough(Window window, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(handle, GwlExStyle);
        var wanted = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        if (wanted != style)
            SetWindowLongPtr(handle, GwlExStyle, wanted);
    }

    private const int VkControl = 0x11;
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern long SetWindowLongPtr(IntPtr hWnd, int index, long value);
}
