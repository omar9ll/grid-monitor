using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GridMonitor.Core.Models;

namespace GridMonitor.Core.Data;

/// <summary>
/// Generates realistic-looking telemetry on a background task and hands it to consumers
/// through a bounded <see cref="Channel{T}"/> (producer/consumer). If the consumer falls
/// behind, the oldest readings are dropped instead of memory growing without limit.
/// </summary>
public sealed class SimulatedDataSource : IDataSource
{
    private readonly ConcurrentDictionary<string, BreakerState> _breakers = new();
    private readonly ConcurrentDictionary<string, double> _loadFactor = new();
    private readonly Random _random;
    private readonly TimeSpan _interval;

    public SimulatedDataSource(IReadOnlyList<FeederConfig> feeders, TimeSpan interval, int? seed = null)
    {
        Feeders = feeders;
        _interval = interval;
        _random = seed is null ? new Random() : new Random(seed.Value);
        foreach (var feeder in feeders)
        {
            _breakers[feeder.Id] = BreakerState.Closed;
            _loadFactor[feeder.Id] = 0.4 + _random.NextDouble() * 0.4;
        }
    }

    public IReadOnlyList<FeederConfig> Feeders { get; }

    /// <summary>Default demo network: two substations, six feeders.</summary>
    public static IReadOnlyList<FeederConfig> DemoFeeders { get; } =
    [
        new("F101", "Feeder 101 - Industrial Park", "Westdale TS", 27.6, 600),
        new("F102", "Feeder 102 - Downtown", "Westdale TS", 27.6, 600),
        new("F103", "Feeder 103 - Hospital", "Westdale TS", 27.6, 400),
        new("F201", "Feeder 201 - Residential North", "Stoney Creek DS", 13.8, 400),
        new("F202", "Feeder 202 - Residential South", "Stoney Creek DS", 13.8, 400),
        new("F203", "Feeder 203 - Commercial Strip", "Stoney Creek DS", 13.8, 300),
    ];

    public BreakerState GetBreakerState(string feederId) => _breakers[feederId];

    public async IAsyncEnumerable<TelemetryReading> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<TelemetryReading>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

        var producer = Task.Run(() => ProduceAsync(channel.Writer, cancellationToken), CancellationToken.None);

        try
        {
            await foreach (var reading in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return reading;
            }
        }
        finally
        {
            await producer.ConfigureAwait(false);
        }
    }

    public async Task SendCommandAsync(BreakerCommand command, CancellationToken cancellationToken = default)
    {
        if (!_breakers.ContainsKey(command.FeederId))
        {
            throw new ArgumentException($"Unknown feeder '{command.FeederId}'.", nameof(command));
        }

        // Simulate the round trip to a remote device.
        await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        _breakers[command.FeederId] = command.TargetState;
    }

    /// <summary>Creates one reading for a feeder. Internal so tests can call it directly.</summary>
    internal TelemetryReading NextReading(FeederConfig feeder, DateTimeOffset now)
    {
        var breaker = _breakers[feeder.Id];

        // Random walk on the load factor, with an occasional spike to trigger alarms.
        var factor = _loadFactor[feeder.Id] + (_random.NextDouble() - 0.5) * 0.04;
        if (_random.NextDouble() < 0.002)
        {
            factor += 0.35;
        }

        factor = Math.Clamp(factor, 0.05, 1.3);
        _loadFactor[feeder.Id] = factor;

        // Voltage sags slightly as load increases; noise of about ±0.5%.
        var voltage = feeder.NominalVoltageKv * (1.02 - 0.05 * factor + (_random.NextDouble() - 0.5) * 0.01);
        var current = breaker == BreakerState.Closed ? feeder.RatedCurrentA * factor : 0.0;

        if (breaker == BreakerState.Open)
        {
            voltage = 0.0;
        }

        return new TelemetryReading(feeder.Id, Math.Round(voltage, 3), Math.Round(current, 1), breaker, now);
    }

    private async Task ProduceAsync(ChannelWriter<TelemetryReading> writer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var feeder in Feeders)
                {
                    writer.TryWrite(NextReading(feeder, now));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            writer.TryComplete();
        }
    }
}
