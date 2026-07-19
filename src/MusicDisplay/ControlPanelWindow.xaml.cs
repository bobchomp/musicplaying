using System.Windows;
using MusicDisplay.Services;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MusicDisplay;

public partial class ControlPanelWindow : Window
{
    private const string NdiSourceName = "Music Display";

    private sealed record MonitorOption(string Label, WinForms.Screen Screen);

    private readonly AppSettings _settings;
    private readonly NowPlayingService _nowPlayingService = new();
    private readonly DisplayWindow _displayWindow = new();
    private readonly NdiOutputService _ndiOutputService = new();

    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _trayToggleMenuItem;
    private PreviewWindow? _previewWindow;

    private bool _isDisplayVisible;
    private bool _isBlanked;
    private bool _suppressMonitorSelectionHandling;
    private bool _suppressVolumeSliderHandling;
    private bool _suppressNetworkFeedHandling;
    private bool _isExiting;
    private NowPlayingInfo? _lastNowPlayingInfo;

    public ControlPanelWindow(bool startMinimized)
    {
        InitializeComponent();

        _settings = SettingsService.Load();

        PopulateMonitors();
        InitializeLayoutSelection();
        StartWithWindowsCheckBox.IsChecked = AutostartService.IsEnabled();

        ShowClockCheckBox.IsChecked = _settings.ShowClock;
        _displayWindow.SetShowClock(_settings.ShowClock);

        InitializeVolumeControls();
        InitializeNetworkFeed();

        SetupTrayIcon();

        _displayWindow.DismissedByUser += OnDisplayDismissedByUser;
        _nowPlayingService.NowPlayingChanged += OnNowPlayingChanged;

        Loaded += async (_, _) => await _nowPlayingService.StartAsync();

        if (_settings.DisplayVisible)
        {
            SetDisplayVisible(true);
        }

        if (startMinimized)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
    }

    private void PopulateMonitors()
    {
        var options = new List<MonitorOption>();
        var screens = WinForms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            string label = $"Monitor {i + 1} — {screen.Bounds.Width}x{screen.Bounds.Height}"
                            + (screen.Primary ? " (Primary)" : string.Empty);
            options.Add(new MonitorOption(label, screen));
        }

        _suppressMonitorSelectionHandling = true;
        MonitorComboBox.ItemsSource = options;

        var savedIndex = options.FindIndex(o => o.Screen.DeviceName == _settings.MonitorDeviceName);
        MonitorComboBox.SelectedIndex = savedIndex >= 0 ? savedIndex : 0;
        _suppressMonitorSelectionHandling = false;
    }

    private WinForms.Screen? SelectedScreen => (MonitorComboBox.SelectedItem as MonitorOption)?.Screen;

    private void InitializeLayoutSelection()
    {
        var radio = _settings.Layout switch
        {
            DisplayLayout.Left => LayoutLeftRadio,
            DisplayLayout.Right => LayoutRightRadio,
            _ => LayoutCenteredRadio,
        };
        radio.IsChecked = true;
    }

    private void InitializeVolumeControls()
    {
        _suppressVolumeSliderHandling = true;
        VolumeSlider.Value = SystemVolumeService.GetVolume() * 100;
        _suppressVolumeSliderHandling = false;

        MuteButton.Content = SystemVolumeService.GetMute() ? "Unmute" : "Mute";
    }

    private void InitializeNetworkFeed()
    {
        if (_settings.NetworkFeedEnabled)
        {
            SetNetworkFeedEnabled(true);
        }
    }

    private void NetworkFeedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressNetworkFeedHandling)
        {
            return;
        }

        SetNetworkFeedEnabled(NetworkFeedCheckBox.IsChecked == true);
    }

    private void SetNetworkFeedEnabled(bool enabled)
    {
        bool actuallyEnabled = enabled && _ndiOutputService.Start(NdiSourceName);
        if (!enabled)
        {
            _ndiOutputService.Stop();
        }

        NetworkFeedStatusText.Text = actuallyEnabled
            ? $"Broadcasting as \"{Environment.MachineName} ({NdiSourceName})\" — assign this source in NDI Virtual Input (or your NDI-aware software) on the receiving computer."
            : enabled
                ? "NDI Runtime not found. Install it (see the installer's Network Feed option, or ndi.video), then try again."
                : "Lets other computers add this as a live feed (e.g. via NDI Virtual Input in EasyWorship). Requires the free NDI Runtime.";

        _suppressNetworkFeedHandling = true;
        NetworkFeedCheckBox.IsChecked = actuallyEnabled;
        _suppressNetworkFeedHandling = false;

        NetworkFeedCheckBox.IsEnabled = _ndiOutputService.IsAvailable;

        _settings.NetworkFeedEnabled = actuallyEnabled;
        SettingsService.Save(_settings);
    }

    private void LayoutRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton { Tag: string tag })
        {
            return;
        }

        if (!Enum.TryParse<DisplayLayout>(tag, out var layout))
        {
            return;
        }

        _settings.Layout = layout;
        SettingsService.Save(_settings);
        _displayWindow.SetLayout(layout);
        _previewWindow?.SetLayout(layout);
        _ndiOutputService.SetLayout(layout);
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewWindow == null)
        {
            _previewWindow = new PreviewWindow { Owner = this };
            _previewWindow.Closed += (_, _) => _previewWindow = null;
            _previewWindow.SetLayout(_settings.Layout);
            _previewWindow.SetBlanked(_isBlanked);
            _previewWindow.SetShowClock(_settings.ShowClock);
            _previewWindow.UpdateNowPlaying(_lastNowPlayingInfo);
            _previewWindow.Show();
        }
        else
        {
            _previewWindow.Activate();
        }
    }

    private void SetupTrayIcon()
    {
        var trayMenu = new WinForms.ContextMenuStrip();

        var openItem = new WinForms.ToolStripMenuItem("Open Control Panel");
        openItem.Click += (_, _) => RestoreFromTray();
        trayMenu.Items.Add(openItem);

        _trayToggleMenuItem = new WinForms.ToolStripMenuItem("Show Display");
        _trayToggleMenuItem.Click += (_, _) => ToggleDisplay();
        trayMenu.Items.Add(_trayToggleMenuItem);

        trayMenu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        trayMenu.Items.Add(exitItem);

        Drawing.Icon? icon = null;
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            icon = Drawing.Icon.ExtractAssociatedIcon(exePath);
        }

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = icon ?? Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Music Display",
            ContextMenuStrip = trayMenu,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;

        // WPF forbids showing/closing windows from within another window's Closing handler (it
        // throws "Cannot set Visibility to Visible or call Show, ShowDialog, Close, or
        // WindowInteropHelper.EnsureHandle while a Window is closing"). Defer to the next
        // dispatcher cycle, after this Closing dispatch has fully unwound.
        Dispatcher.BeginInvoke(new Action(ShowCloseConfirmation));
    }

    private void ShowCloseConfirmation()
    {
        var dialog = new CloseConfirmationWindow { Owner = this };
        dialog.ShowDialog();

        switch (dialog.Choice)
        {
            case CloseChoice.MinimizeToTray:
                WindowState = WindowState.Minimized;
                break;
            case CloseChoice.ExitProgram:
                ExitApplication();
                break;
            case CloseChoice.Cancel:
            default:
                break;
        }
    }

    private void MonitorComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressMonitorSelectionHandling)
        {
            return;
        }

        var screen = SelectedScreen;
        if (screen == null)
        {
            return;
        }

        _settings.MonitorDeviceName = screen.DeviceName;
        SettingsService.Save(_settings);

        if (_isDisplayVisible)
        {
            _displayWindow.MoveToScreen(screen);
        }
    }

    private void ToggleDisplayButton_Click(object sender, RoutedEventArgs e) => ToggleDisplay();

    private void ToggleDisplay() => SetDisplayVisible(!_isDisplayVisible);

    private void SetDisplayVisible(bool visible)
    {
        var screen = SelectedScreen ?? WinForms.Screen.PrimaryScreen;

        if (visible && screen != null)
        {
            _displayWindow.ShowOnScreen(screen);
        }
        else
        {
            _displayWindow.HideDisplay();
        }

        _isDisplayVisible = visible;
        ToggleDisplayButton.Content = visible ? "Hide Display" : "Show Display";
        if (_trayToggleMenuItem != null)
        {
            _trayToggleMenuItem.Text = visible ? "Hide Display" : "Show Display";
        }

        _isBlanked = false;
        _displayWindow.SetBlanked(false);
        _previewWindow?.SetBlanked(false);
        _ndiOutputService.SetBlanked(false);
        BlankButton.Content = "Blank Screen";
        BlankButton.IsEnabled = visible;

        _settings.DisplayVisible = visible;
        SettingsService.Save(_settings);
    }

    private void BlankButton_Click(object sender, RoutedEventArgs e)
    {
        _isBlanked = !_isBlanked;
        _displayWindow.SetBlanked(_isBlanked);
        _previewWindow?.SetBlanked(_isBlanked);
        _ndiOutputService.SetBlanked(_isBlanked);
        BlankButton.Content = _isBlanked ? "Unblank" : "Blank Screen";
    }

    private void OnDisplayDismissedByUser() => Dispatcher.Invoke(() => SetDisplayVisible(false));

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e) => await _nowPlayingService.PlayPauseAsync();

    private async void PreviousButton_Click(object sender, RoutedEventArgs e) => await _nowPlayingService.PreviousAsync();

    private async void NextButton_Click(object sender, RoutedEventArgs e) => await _nowPlayingService.NextAsync();

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        bool nowMuted = !SystemVolumeService.GetMute();
        SystemVolumeService.SetMute(nowMuted);
        MuteButton.Content = nowMuted ? "Unmute" : "Mute";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeSliderHandling)
        {
            return;
        }

        SystemVolumeService.SetVolume((float)(e.NewValue / 100));
    }

    private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = StartWithWindowsCheckBox.IsChecked == true;
        AutostartService.SetEnabled(enabled);
        _settings.StartWithWindows = enabled;
        SettingsService.Save(_settings);
    }

    private void ShowClockCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool show = ShowClockCheckBox.IsChecked == true;
        _settings.ShowClock = show;
        SettingsService.Save(_settings);
        _displayWindow.SetShowClock(show);
        _previewWindow?.SetShowClock(show);
        _ndiOutputService.SetShowClock(show);
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void OnNowPlayingChanged(NowPlayingInfo? info)
    {
        Dispatcher.Invoke(() =>
        {
            _lastNowPlayingInfo = info;
            _displayWindow.UpdateNowPlaying(info);
            _previewWindow?.UpdateNowPlaying(info);
            _ndiOutputService.UpdateNowPlaying(info);

            NowPlayingStatusText.Text = info != null && !string.IsNullOrWhiteSpace(info.Title)
                ? $"{info.Title} — {info.Artist}"
                : "No music detected";

            PlayPauseButton.Content = info?.IsPlaying == true ? "Pause" : "Play";
        });
    }

    private void ExitApplication()
    {
        _isExiting = true;

        _nowPlayingService.NowPlayingChanged -= OnNowPlayingChanged;
        _nowPlayingService.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _ndiOutputService.Dispose();
        _previewWindow?.Close();
        _displayWindow.Close();
        Close();

        System.Windows.Application.Current.Shutdown();
    }
}
