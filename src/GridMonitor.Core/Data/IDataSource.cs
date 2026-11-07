using GridMonitor.Core.Models;

namespace GridMonitor.Core.Data;

/// <summary>
/// Anything that can stream telemetry and accept breaker commands. The app only depends on
/// this interface, so the simulator can be swapped for a real field protocol (e.g. DNP3 or
/// Modbus) or a test fake without touching any view model.
/// </summary>
public interface IDataSource
{
    IReadOnlyList<FeederConfig> Feeders { get; }

    /// <summary>Streams readings until <paramref name="cancellationToken"/> is cancelled.</summary>
    IAsyncEnumerable<TelemetryReading> ReadAllAsync(CancellationToken cancellationToken);

    Task SendCommandAsync(BreakerCommand command, CancellationToken cancellationToken = default);
}
