using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MusicDisplay;

public partial class App : Application
{
    // Fixed GUID-style name so a second launch can detect the first instance and exit quietly
    // instead of spawning duplicate tray icons and duplicate SMTC listeners.
    private const string SingleInstanceMutexName = "MusicDisplay-SingleInstance-3F2A9E9E-9F5B-4E2A-8E3C-5B2E7C1A2B44";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        var controlPanel = new ControlPanelWindow(startMinimized);
        MainWindow = controlPanel;

        if (!startMinimized)
        {
            controlPanel.Show();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Music Display hit an unexpected error and needs to continue running in the background:\n\n{e.Exception.Message}",
            "Music Display",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
