using System;
using System.Windows.Threading;
using GridMonitor.Core.Threading;

namespace GridMonitor.App.Services;

/// <summary>
/// Marshals background updates onto the WPF UI thread. Uses <see cref="Dispatcher.InvokeAsync(Action, DispatcherPriority)"/>
/// so the background thread never blocks waiting for the UI, and a lower priority so user input
/// (clicks, scrolling) is always handled before data refreshes.
/// </summary>
public sealed class WpfDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Post(Action action) => _dispatcher.InvokeAsync(action, DispatcherPriority.Background);
}
