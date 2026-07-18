using System.Windows;
using MusicDisplay.Services;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MusicDisplay;

public partial class ControlPanelWindow : Window
{
    private sealed record MonitorOption(string Label, WinForms.Screen Screen);

    private readonly AppSettings _settings;
    private readonly NowPlayingService _nowPlayingService = new();
    private readonly DisplayWindow _displayWindow = new();

    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _trayToggleMenuItem;

    private bool _isDisplayVisible;
    private bool _isBlanked;
    private bool _suppressMonitorSelectionHandling;
    private bool _isExiting;

    public ControlPanelWindow(bool startMinimized)
    {
        InitializeComponent();

        _settings = SettingsService.Load();

        PopulateMonitors();
        InitializeLayoutSelection();
        StartWithWindowsCheckBox.IsChecked = AutostartService.IsEnabled();

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
        WindowState = WindowState.Minimized;
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
        BlankButton.Content = "Blank Screen";
        BlankButton.IsEnabled = visible;

        _settings.DisplayVisible = visible;
        SettingsService.Save(_settings);
    }

    private void BlankButton_Click(object sender, RoutedEventArgs e)
    {
        _isBlanked = !_isBlanked;
        _displayWindow.SetBlanked(_isBlanked);
        BlankButton.Content = _isBlanked ? "Unblank" : "Blank Screen";
    }

    private void OnDisplayDismissedByUser() => Dispatcher.Invoke(() => SetDisplayVisible(false));

    private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = StartWithWindowsCheckBox.IsChecked == true;
        AutostartService.SetEnabled(enabled);
        _settings.StartWithWindows = enabled;
        SettingsService.Save(_settings);
    }

    private void OnNowPlayingChanged(NowPlayingInfo? info)
    {
        Dispatcher.Invoke(() =>
        {
            _displayWindow.UpdateNowPlaying(info);

            NowPlayingStatusText.Text = info != null && !string.IsNullOrWhiteSpace(info.Title)
                ? $"{info.Title} — {info.Artist}"
                : "No music detected";
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

        _displayWindow.Close();
        Close();

        System.Windows.Application.Current.Shutdown();
    }
}
