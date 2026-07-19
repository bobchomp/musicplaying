using System.Windows;

namespace MusicDisplay;

public enum CloseChoice
{
    Cancel,
    MinimizeToTray,
    ExitProgram,
}

/// <summary>Asks whether closing the control panel should minimize it to the tray or exit the app.</summary>
public partial class CloseConfirmationWindow : Window
{
    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    public CloseConfirmationWindow()
    {
        InitializeComponent();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.MinimizeToTray;
        Close();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.ExitProgram;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.Cancel;
        Close();
    }
}
