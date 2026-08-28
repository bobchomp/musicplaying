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
            // Not silent: this opens the normal Setup wizard, same as if the user had downloaded
            // and double-clicked the installer themselves — InstallStarted below closes this app
            // well before Setup gets past its first couple of wizard pages and actually tries to
            // replace the running exe.
            Process.Start(new ProcessStartInfo { FileName = installerPath, UseShellExecute = true });
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
