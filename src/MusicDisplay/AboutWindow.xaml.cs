using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace MusicDisplay;

public partial class AboutWindow : Window
{
    private const string RepositoryUrl = "https://github.com/bobchomp/musicplaying";

    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version != null
            ? $"Version {version.Major}.{version.Minor}.{version.Build}"
            : string.Empty;
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = RepositoryUrl, UseShellExecute = true });
        }
        catch (Exception)
        {
            // Best-effort; nothing actionable if a browser can't be launched.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
