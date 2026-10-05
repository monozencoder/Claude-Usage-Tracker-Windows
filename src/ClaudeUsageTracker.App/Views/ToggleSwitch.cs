using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace ClaudeUsageTracker.App.Views;

/// <summary>
/// Slides the knob of a toggle switch (ToggleSwitchStyle in Themes/Styles.xaml) when the user
/// flips it. The template's IsChecked trigger holds the knob's resting position; this only plays
/// the 0.15s slide between the two, and only on a toggle that is already on screen — so a toggle
/// that is on does not slide in from "off" when its page is first shown.
/// </summary>
public static class ToggleSwitch
{
    private const double KnobOffLeft = 2;
    private const double KnobOnLeft = 20;

    public static readonly DependencyProperty AnimateKnobProperty = DependencyProperty.RegisterAttached(
        "AnimateKnob", typeof(bool), typeof(ToggleSwitch), new PropertyMetadata(false, OnAnimateKnobChanged));

    public static bool GetAnimateKnob(DependencyObject element) => (bool)element.GetValue(AnimateKnobProperty);

    public static void SetAnimateKnob(DependencyObject element, bool value) => element.SetValue(AnimateKnobProperty, value);

    private static void OnAnimateKnobChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToggleButton toggle)
            return;

        toggle.Checked -= OnChecked;
        toggle.Unchecked -= OnUnchecked;
        if ((bool)e.NewValue)
        {
            toggle.Checked += OnChecked;
            toggle.Unchecked += OnUnchecked;
        }
    }

    private static void OnChecked(object sender, RoutedEventArgs e) => Slide((ToggleButton)sender, KnobOffLeft, KnobOnLeft);

    private static void OnUnchecked(object sender, RoutedEventArgs e) => Slide((ToggleButton)sender, KnobOnLeft, KnobOffLeft);

    private static void Slide(ToggleButton toggle, double from, double to)
    {
        if (!toggle.IsLoaded || toggle.Template?.FindName("Knob", toggle) is not FrameworkElement knob)
            return;

        knob.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(0.15))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }
}
