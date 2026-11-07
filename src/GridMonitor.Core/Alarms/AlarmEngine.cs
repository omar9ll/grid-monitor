using GridMonitor.Core.Models;

namespace GridMonitor.Core.Alarms;

/// <summary>Limits for one alarm condition, as a fraction of the feeder's rating.</summary>
public sealed record AlarmLimits(
    double WarningLoading = 0.90,
    double CriticalLoading = 1.05,
    double LowVoltagePu = 0.95,
    double Deadband = 0.03);

/// <summary>
/// Evaluates readings against limits and raises or clears alarms. A deadband (hysteresis)
/// stops an alarm from flickering on and off when a value hovers right at the limit.
/// </summary>
public sealed class AlarmEngine
{
    private readonly AlarmLimits _limits;
    private readonly Dictionary<string, FeederConfig> _feeders;
    private readonly Dictionary<(string FeederId, string Kind), AlarmSeverity> _active = new();

    public AlarmEngine(IEnumerable<FeederConfig> feeders, AlarmLimits? limits = null)
    {
        _limits = limits ?? new AlarmLimits();
        _feeders = feeders.ToDictionary(f => f.Id);
    }

    public int ActiveCount => _active.Count;

    /// <summary>Returns alarms that changed state (newly raised, escalated or cleared).</summary>
    public IReadOnlyList<Alarm> Evaluate(TelemetryReading reading)
    {
        var feeder = _feeders[reading.FeederId];
        var changes = new List<Alarm>();

        if (reading.Breaker == BreakerState.Open)
        {
            // A de-energized feeder cannot be overloaded or low on voltage.
            Clear(feeder, "overload", reading.Timestamp, changes);
            Clear(feeder, "undervoltage", reading.Timestamp, changes);
            return changes;
        }

        // Overload, measured as current / rated current.
        var loading = reading.CurrentA / feeder.RatedCurrentA;
        AlarmSeverity? overload = loading >= _limits.CriticalLoading ? AlarmSeverity.Critical
            : loading >= _limits.WarningLoading ? AlarmSeverity.Warning
            : null;
        var overloadClearLevel = _limits.WarningLoading - _limits.Deadband;
        Update(feeder, "overload", overload, loading < overloadClearLevel,
            $"Loading {loading:P0} of rating ({reading.CurrentA:F0} A)", reading.Timestamp, changes);

        // Under-voltage, measured as V / nominal (per unit).
        var pu = reading.VoltageKv / feeder.NominalVoltageKv;
        AlarmSeverity? undervoltage = pu < _limits.LowVoltagePu ? AlarmSeverity.Warning : null;
        Update(feeder, "undervoltage", undervoltage, pu >= _limits.LowVoltagePu + _limits.Deadband,
            $"Low voltage {pu:F3} pu ({reading.VoltageKv:F2} kV)", reading.Timestamp, changes);

        return changes;
    }

    private void Update(FeederConfig feeder, string kind, AlarmSeverity? severity, bool canClear,
        string message, DateTimeOffset at, List<Alarm> changes)
    {
        var key = (feeder.Id, kind);
        var isActive = _active.TryGetValue(key, out var current);

        if (severity is { } s && (!isActive || s > current))
        {
            _active[key] = s;
            changes.Add(new Alarm(feeder.Id, feeder.Name, kind, message, s, at, IsActive: true));
        }
        else if (isActive && severity is null && canClear)
        {
            _active.Remove(key);
            changes.Add(new Alarm(feeder.Id, feeder.Name, kind, $"Cleared: {kind}", current, at, IsActive: false));
        }
    }

    private void Clear(FeederConfig feeder, string kind, DateTimeOffset at, List<Alarm> changes)
    {
        if (_active.Remove((feeder.Id, kind), out var severity))
        {
            changes.Add(new Alarm(feeder.Id, feeder.Name, kind, $"Cleared: {kind} (breaker open)", severity, at, IsActive: false));
        }
    }
}
