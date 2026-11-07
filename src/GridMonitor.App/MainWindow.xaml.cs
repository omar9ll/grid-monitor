using System.Windows;

namespace GridMonitor.App;

/// <summary>
/// No logic lives here: everything the window shows comes from data binding to
/// <see cref="Core.ViewModels.MainViewModel"/> (the MVVM pattern).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
