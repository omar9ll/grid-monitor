using System;
using System.Windows;
using GridMonitor.App.Services;
using GridMonitor.Core.Data;
using GridMonitor.Core.ViewModels;

namespace GridMonitor.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Composition root: this is the only place that knows about concrete types.
        // Swap SimulatedDataSource for a real protocol adapter and nothing else changes.
        var source = new SimulatedDataSource(SimulatedDataSource.DemoFeeders, TimeSpan.FromMilliseconds(50));
        _viewModel = new MainViewModel(source, new WpfDispatcher(Dispatcher), TimeSpan.FromMilliseconds(200));

        var window = new MainWindow { DataContext = _viewModel };
        window.Show();
        _viewModel.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Cancels the background tasks; the process is exiting so we do not block on them.
        _ = _viewModel?.DisposeAsync().AsTask();
        base.OnExit(e);
    }
}
