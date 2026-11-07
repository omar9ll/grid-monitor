namespace GridMonitor.Core.Threading;

/// <summary>
/// Runs work on the UI thread. WPF controls can only be touched from the thread that created
/// them, so background code posts its updates through this interface. The WPF app wraps
/// <c>Dispatcher.InvokeAsync</c>; tests use <see cref="ImmediateDispatcher"/>.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Runs the action immediately on the calling thread. Used in tests and console tools.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
