using GridMonitor.Core.Collections;
using GridMonitor.Core.Data;
using GridMonitor.Core.Models;
using GridMonitor.Core.Mvvm;
using GridMonitor.Core.Processing;
using GridMonitor.Core.Threading;
using static GridMonitor.Core.Tests.TestData;

namespace GridMonitor.Core.Tests;

public class RingBufferTests
{
    [Fact]
    public void KeepsOnlyTheNewestItems_InOrder()
    {
        var buffer = new RingBuffer<int>(3);
        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        Assert.Equal(new[] { 3, 4, 5 }, buffer.ToArray());
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void RejectsNonPositiveCapacity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
}

public class SimulatedDataSourceTests
{
    [Fact]
    public void ReadingsStayWithinRealisticBounds()
    {
        var source = new SimulatedDataSource([Feeder], TimeSpan.FromMilliseconds(10), seed: 42);

        for (var i = 0; i < 500; i++)
        {
            var r = source.NextReading(Feeder, DateTimeOffset.UtcNow);
            Assert.InRange(r.VoltageKv, Feeder.NominalVoltageKv * 0.9, Feeder.NominalVoltageKv * 1.05);
            Assert.InRange(r.CurrentA, 0, Feeder.RatedCurrentA * 1.31);
        }
    }

    [Fact]
    public async Task OpenBreaker_DeEnergizesTheFeeder()
    {
        var source = new SimulatedDataSource([Feeder], TimeSpan.FromMilliseconds(10), seed: 1);

        await source.SendCommandAsync(new BreakerCommand(Feeder.Id, BreakerState.Open));
        var reading = source.NextReading(Feeder, DateTimeOffset.UtcNow);

        Assert.Equal(BreakerState.Open, reading.Breaker);
        Assert.Equal(0, reading.CurrentA);
        Assert.Equal(0, reading.VoltageKv);
    }

    [Fact]
    public async Task StreamsReadingsForEveryFeeder()
    {
        var feeders = SimulatedDataSource.DemoFeeders;
        var source = new SimulatedDataSource(feeders, TimeSpan.FromMilliseconds(5), seed: 7);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var seen = new HashSet<string>();

        await foreach (var reading in source.ReadAllAsync(cts.Token))
        {
            seen.Add(reading.FeederId);
            if (seen.Count == feeders.Count)
            {
                break;
            }
        }

        Assert.Equal(feeders.Count, seen.Count);
    }

    [Fact]
    public async Task UnknownFeederCommand_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SimulatedDataSource([Feeder], TimeSpan.FromMilliseconds(10))
                .SendCommandAsync(new BreakerCommand("NOPE", BreakerState.Open)));

    [Fact]
    public void LoadMva_UsesThreePhaseFormula()
    {
        var reading = Reading(currentA: 100, voltageKv: 13.8);

        Assert.Equal(Math.Sqrt(3) * 13.8 * 100 / 1000, reading.LoadMva, precision: 6);
    }
}

public class TelemetryProcessorTests
{
    [Fact]
    public void Batch_KeepsOnlyLatestReadingPerFeeder()
    {
        var processor = new TelemetryProcessor(new FakeDataSource(Feeder), new ImmediateDispatcher(),
            TimeSpan.FromMilliseconds(100), (_, _) => { });

        processor.Ingest(Reading(currentA: 100));
        processor.Ingest(Reading(currentA: 150));
        processor.Ingest(Reading(currentA: 50, feederId: "F2"));

        var batch = processor.TakeBatch();

        Assert.Equal(2, batch.Count);
        Assert.Equal(150, batch.Single(r => r.FeederId == "F1").CurrentA);
        Assert.Empty(processor.TakeBatch());
    }

    [Fact]
    public async Task EndToEnd_FlushesSimulatedDataToTheUi()
    {
        var source = new SimulatedDataSource(SimulatedDataSource.DemoFeeders, TimeSpan.FromMilliseconds(10), seed: 3);
        var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var processor = new TelemetryProcessor(source, new ImmediateDispatcher(),
            TimeSpan.FromMilliseconds(50), (batch, _) => received.TrySetResult(batch.Count));

        processor.Start();
        var count = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.InRange(count, 1, SimulatedDataSource.DemoFeeders.Count);
    }
}

public class CommandTests
{
    [Fact]
    public void RelayCommand_RespectsCanExecute()
    {
        var runs = 0;
        var enabled = false;
        var command = new RelayCommand(() => runs++, () => enabled);

        command.Execute(null);
        enabled = true;
        command.Execute(null);

        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task AsyncRelayCommand_IsDisabledWhileRunning()
    {
        var gate = new TaskCompletionSource();
        var command = new AsyncRelayCommand(() => gate.Task);

        var running = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));

        gate.SetResult();
        await running;
        Assert.True(command.CanExecute(null));
    }
}
