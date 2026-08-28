using System.Diagnostics;
using System.Windows;
using MusicDisplay.Services;

namespace MusicDisplay;

/// <summary>
/// The "a newer version is available" nag. Owns downloading the installer and launching it
/// itself; app-level shutdown is left to the caller (ControlPanelWindow), which subscribes to
/// InstallStarted and calls its own centralized ExitApplication rather than this window
/// shutting the app down directly — the same "views raise events, ControlPanelWindow
/// orchestrates" split already used for Show Music Video.
/// </summary>
public partial class UpdateAvailableWindow : Window
{
    private readonly UpdateService _updateService;
    private readonly UpdateInfo _update;

    /// <summary>Raised once the installer has actually been launched — the app should now exit
    /// so Setup can replace its files.</summary>
    public event Action? InstallStarted;

    public UpdateAvailableWindow(UpdateService updateService, UpdateInfo update)
    {
        InitializeComponent();
        _updateService = updateService;
        _update = update;
        MessageText.Text = $"A newer version of Music Display is available (v{update.Version}). Please update now.";
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        DownloadProgressBar.Visibility = Visibility.Visible;
        ProgressStatusText.Visibility = Visibility.Visible;
        ProgressStatusText.Text = "Downloading update…";

        var progress = new Progress<double>(fraction =>
        {
            DownloadProgressBar.Value = fraction * 100;
            ProgressStatusText.Text = $"Downloading update… {fraction:P0}";
        });

        var installerPath = await _updateService.DownloadInstallerAsync(_update, progress);
        if (installerPath == null)
        {
            ProgressStatusText.Text = "Download failed — see %AppData%\\MusicDisplay\\update-debug.log for details.";
            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            return;
        }

        try
        {
            // /VERYSILENT: no wizard pages, no progress window — Setup just replaces the files
            // and exits. /SUPPRESSMSGBOXES answers any prompt it would otherwise show (e.g. "quit
            // and continue?") with its default/safest answer instead of blocking with a dialog
            // nobody's there to click; /NORESTART skips a reboot prompt (this app never needs
            // one). installer.iss's own app-relaunch [Run] entry still fires afterward (it isn't
            // skipifsilent), so the app comes back up on its own once Setup finishes — the user
            // sees this window close and, a few seconds later, the app reappear, with nothing
            // in between.
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            ProgressStatusText.Text = "Couldn't launch the installer — download it yourself from the GitHub releases page.";
            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            return;
        }

        // Close this window before raising InstallStarted, not after — the handler shuts the
        // whole app down (Application.Shutdown()), and WPF disallows Show/Close/EnsureHandle
        // calls once that's underway.
        Close();
        InstallStarted?.Invoke();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e) => Close();
}
