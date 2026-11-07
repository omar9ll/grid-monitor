using System.Collections.Concurrent;
using System.Diagnostics;
using GridMonitor.Core.Data;
using GridMonitor.Core.Models;
using GridMonitor.Core.Threading;

namespace GridMonitor.Core.Processing;

/// <summary>
/// Keeps the UI responsive under a fast data feed.
/// <list type="bullet">
/// <item>A background task consumes the stream and keeps only the latest reading per feeder.</item>
/// <item>A second task flushes those latest values to the UI thread at a fixed rate (e.g. 5x per
/// second) instead of once per reading, so the UI never gets flooded.</item>
/// </list>
/// </summary>
public sealed class TelemetryProcessor : IAsyncDisposable
{
    private readonly IDataSource _source;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeSpan _flushInterval;
    private readonly Action<IReadOnlyList<TelemetryReading>, double> _applyOnUiThread;
    private readonly ConcurrentDictionary<string, TelemetryReading> _latest = new();

    private CancellationTokenSource? _cts;
    private Task? _consumer;
    private Task? _flusher;
    private long _received;

    public TelemetryProcessor(
        IDataSource source,
        IUiDispatcher dispatcher,
        TimeSpan flushInterval,
        Action<IReadOnlyList<TelemetryReading>, double> applyOnUiThread)
    {
        _source = source;
        _dispatcher = dispatcher;
        _flushInterval = flushInterval;
        _applyOnUiThread = applyOnUiThread;
    }

    public bool IsRunning => _cts is not null;

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _consumer = Task.Run(() => ConsumeAsync(token), CancellationToken.None);
        _flusher = Task.Run(() => FlushLoopAsync(token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_consumer!, _flusher!).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Stores a reading, replacing any older unflushed reading for the same feeder.</summary>
    internal void Ingest(TelemetryReading reading)
    {
        _latest[reading.FeederId] = reading;
        Interlocked.Increment(ref _received);
    }

    /// <summary>Removes and returns the latest reading per feeder (coalesced batch).</summary>
    internal IReadOnlyList<TelemetryReading> TakeBatch()
    {
        var batch = new List<TelemetryReading>(_latest.Count);
        foreach (var key in _latest.Keys)
        {
            if (_latest.TryRemove(key, out var reading))
            {
                batch.Add(reading);
            }
        }

        return batch;
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        await foreach (var reading in _source.ReadAllAsync(token).ConfigureAwait(false))
        {
            Ingest(reading);
        }
    }

    private async Task FlushLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_flushInterval);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                stopwatch.Restart();
                var count = Interlocked.Exchange(ref _received, 0);
                var rate = elapsed > 0 ? count / elapsed : 0;

                var batch = TakeBatch();
                if (batch.Count > 0)
                {
                    _dispatcher.Post(() => _applyOnUiThread(batch, rate));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
