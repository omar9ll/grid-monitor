using GridMonitor.Core.Data;
using GridMonitor.Core.Models;

namespace GridMonitor.Core.Tests;

internal static class TestData
{
    public static readonly FeederConfig Feeder = new("F1", "Test Feeder", "Test TS", 13.8, 400);

    public static TelemetryReading Reading(double currentA, double voltageKv = 13.8,
        BreakerState breaker = BreakerState.Closed, string feederId = "F1") =>
        new(feederId, voltageKv, currentA, breaker, DateTimeOffset.UtcNow);
}

/// <summary>A data source that records commands and emits nothing. Used to test view models.</summary>
internal sealed class FakeDataSource : IDataSource
{
    public FakeDataSource(params FeederConfig[] feeders) => Feeders = feeders;

    public IReadOnlyList<FeederConfig> Feeders { get; }

    public List<BreakerCommand> Commands { get; } = new();

    public async IAsyncEnumerable<TelemetryReading> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default);
        yield break;
    }

    public Task SendCommandAsync(BreakerCommand command, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        return Task.CompletedTask;
    }
}
