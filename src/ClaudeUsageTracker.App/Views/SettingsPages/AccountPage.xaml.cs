using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace ClaudeUsageTracker.App.Views.SettingsPages;

public partial class AccountPage : UserControl
{
    public AccountPage() => InitializeComponent();

    private void OnInstallLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
