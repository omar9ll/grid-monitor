using System.Collections.ObjectModel;
using GridMonitor.Core.Alarms;
using GridMonitor.Core.Data;
using GridMonitor.Core.Models;
using GridMonitor.Core.Mvvm;
using GridMonitor.Core.Processing;
using GridMonitor.Core.Threading;

namespace GridMonitor.Core.ViewModels;

/// <summary>
/// The dashboard. Owns the feeders, the alarm list and the telemetry pipeline. Contains no
/// WPF types, so all of its logic is unit-tested without a UI.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    public const int MaxAlarmHistory = 200;

    private readonly AlarmEngine _alarmEngine;
    private readonly TelemetryProcessor _processor;
    private readonly Dictionary<string, FeederViewModel> _byId;
    private readonly Dictionary<string, Dictionary<string, AlarmSeverity>> _activeByFeeder = new();

    private bool _isRunning;
    private double _readingsPerSecond;
    private int _activeAlarmCount;
    private DateTimeOffset? _lastUpdate;
    private double _totalLoadMva;

    public MainViewModel(IDataSource source, IUiDispatcher dispatcher, TimeSpan? uiRefreshInterval = null)
    {
        Feeders = new ObservableCollection<FeederViewModel>(source.Feeders.Select(f => new FeederViewModel(f, source)));
        _byId = Feeders.ToDictionary(f => f.Id);
        _alarmEngine = new AlarmEngine(source.Feeders);
        _processor = new TelemetryProcessor(source, dispatcher, uiRefreshInterval ?? TimeSpan.FromMilliseconds(200), ApplyBatch);

        StartCommand = new RelayCommand(Start, () => !IsRunning);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsRunning);
        ClearAlarmHistoryCommand = new RelayCommand(() => Alarms.Clear());
    }

    public ObservableCollection<FeederViewModel> Feeders { get; }

    /// <summary>Alarm events, newest first.</summary>
    public ObservableCollection<Alarm> Alarms { get; } = new();

    public RelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public RelayCommand ClearAlarmHistoryCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                StartCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => IsRunning ? "Live" : "Stopped";

    public double ReadingsPerSecond
    {
        get => _readingsPerSecond;
        private set => SetProperty(ref _readingsPerSecond, value);
    }

    public int ActiveAlarmCount
    {
        get => _activeAlarmCount;
        private set => SetProperty(ref _activeAlarmCount, value);
    }

    public double TotalLoadMva
    {
        get => _totalLoadMva;
        private set => SetProperty(ref _totalLoadMva, value);
    }

    public DateTimeOffset? LastUpdate
    {
        get => _lastUpdate;
        private set => SetProperty(ref _lastUpdate, value);
    }

    public void Start()
    {
        _processor.Start();
        IsRunning = true;
    }

    public async Task StopAsync()
    {
        await _processor.StopAsync().ConfigureAwait(true);
        IsRunning = false;
    }

    public async ValueTask DisposeAsync() => await _processor.DisposeAsync().ConfigureAwait(false);

    /// <summary>
    /// Applies a coalesced batch of readings. Called on the UI thread by the processor;
    /// public so tests can drive the view model directly.
    /// </summary>
    public void ApplyBatch(IReadOnlyList<TelemetryReading> batch, double readingsPerSecond)
    {
        foreach (var reading in batch)
        {
            if (!_byId.TryGetValue(reading.FeederId, out var feeder))
            {
                continue;
            }

            feeder.Apply(reading);

            foreach (var alarm in _alarmEngine.Evaluate(reading))
            {
                Track(alarm);
                Alarms.Insert(0, alarm);
                if (Alarms.Count > MaxAlarmHistory)
                {
                    Alarms.RemoveAt(Alarms.Count - 1);
                }
            }

            feeder.AlarmSeverity = HighestActive(feeder.Id);
        }

        ReadingsPerSecond = Math.Round(readingsPerSecond, 1);
        ActiveAlarmCount = _alarmEngine.ActiveCount;
        TotalLoadMva = Feeders.Sum(f => f.LoadMva);
        LastUpdate = batch.Count > 0 ? batch.Max(r => r.Timestamp) : LastUpdate;
    }

    private void Track(Alarm alarm)
    {
        if (!_activeByFeeder.TryGetValue(alarm.FeederId, out var active))
        {
            active = new Dictionary<string, AlarmSeverity>();
            _activeByFeeder[alarm.FeederId] = active;
        }

        if (alarm.IsActive)
        {
            active[alarm.Kind] = alarm.Severity;
        }
        else
        {
            active.Remove(alarm.Kind);
        }
    }

    private AlarmSeverity? HighestActive(string feederId) =>
        _activeByFeeder.TryGetValue(feederId, out var active) && active.Count > 0
            ? active.Values.Max()
            : null;
}
