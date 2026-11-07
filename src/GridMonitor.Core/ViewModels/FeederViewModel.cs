using GridMonitor.Core.Collections;
using GridMonitor.Core.Data;
using GridMonitor.Core.Models;
using GridMonitor.Core.Mvvm;

namespace GridMonitor.Core.ViewModels;

/// <summary>One feeder card on the dashboard.</summary>
public sealed class FeederViewModel : ObservableObject
{
    public const int HistoryLength = 120;

    private readonly IDataSource _source;
    private readonly RingBuffer<double> _loadHistory = new(HistoryLength);

    private double _voltageKv;
    private double _currentA;
    private double _loadMva;
    private BreakerState _breaker = BreakerState.Closed;
    private AlarmSeverity? _alarmSeverity;
    private double[] _loadHistorySnapshot = [];
    private bool _isCommandPending;

    public FeederViewModel(FeederConfig config, IDataSource source)
    {
        Config = config;
        _source = source;
        ToggleBreakerCommand = new AsyncRelayCommand(ToggleBreakerAsync, () => !IsCommandPending);
    }

    public FeederConfig Config { get; }

    public string Id => Config.Id;

    public string Name => Config.Name;

    public string Substation => Config.Substation;

    public double VoltageKv
    {
        get => _voltageKv;
        private set => SetProperty(ref _voltageKv, value);
    }

    public double CurrentA
    {
        get => _currentA;
        private set
        {
            if (SetProperty(ref _currentA, value))
            {
                OnPropertyChanged(nameof(LoadingPercent));
            }
        }
    }

    public double LoadMva
    {
        get => _loadMva;
        private set => SetProperty(ref _loadMva, value);
    }

    /// <summary>Current as a percentage of the feeder's rated current.</summary>
    public double LoadingPercent => Config.RatedCurrentA > 0 ? 100 * CurrentA / Config.RatedCurrentA : 0;

    public BreakerState Breaker
    {
        get => _breaker;
        private set
        {
            if (SetProperty(ref _breaker, value))
            {
                OnPropertyChanged(nameof(IsClosed));
                OnPropertyChanged(nameof(ToggleLabel));
            }
        }
    }

    public bool IsClosed => Breaker == BreakerState.Closed;

    public string ToggleLabel => IsClosed ? "Open breaker" : "Close breaker";

    /// <summary>Highest active alarm severity on this feeder, or null if healthy.</summary>
    public AlarmSeverity? AlarmSeverity
    {
        get => _alarmSeverity;
        set
        {
            if (SetProperty(ref _alarmSeverity, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public string Status => AlarmSeverity switch
    {
        Models.AlarmSeverity.Critical => "CRITICAL",
        Models.AlarmSeverity.Warning => "WARNING",
        _ => IsClosed ? "NORMAL" : "DE-ENERGIZED",
    };

    /// <summary>Snapshot of recent load values (MVA), oldest first, for the trend line.</summary>
    public double[] LoadHistory
    {
        get => _loadHistorySnapshot;
        private set => SetProperty(ref _loadHistorySnapshot, value);
    }

    public bool IsCommandPending
    {
        get => _isCommandPending;
        private set
        {
            if (SetProperty(ref _isCommandPending, value))
            {
                ToggleBreakerCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand ToggleBreakerCommand { get; }

    /// <summary>Applies a reading. Must be called on the UI thread.</summary>
    public void Apply(TelemetryReading reading)
    {
        VoltageKv = reading.VoltageKv;
        CurrentA = reading.CurrentA;
        LoadMva = reading.LoadMva;
        Breaker = reading.Breaker;
        _loadHistory.Add(reading.LoadMva);
        LoadHistory = _loadHistory.ToArray();
        OnPropertyChanged(nameof(Status));
    }

    private async Task ToggleBreakerAsync()
    {
        IsCommandPending = true;
        try
        {
            var target = IsClosed ? BreakerState.Open : BreakerState.Closed;
            await _source.SendCommandAsync(new BreakerCommand(Id, target)).ConfigureAwait(true);
        }
        finally
        {
            IsCommandPending = false;
        }
    }
}
