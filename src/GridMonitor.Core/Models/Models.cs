namespace GridMonitor.Core.Models;

public enum BreakerState
{
    Closed,
    Open,
}

public enum AlarmSeverity
{
    Warning,
    Critical,
}

/// <summary>Static description of a feeder (a circuit leaving a substation).</summary>
public sealed record FeederConfig(
    string Id,
    string Name,
    string Substation,
    double NominalVoltageKv,
    double RatedCurrentA);

/// <summary>One telemetry sample for a feeder, as it would arrive from the field.</summary>
public readonly record struct TelemetryReading(
    string FeederId,
    double VoltageKv,
    double CurrentA,
    BreakerState Breaker,
    DateTimeOffset Timestamp)
{
    /// <summary>Apparent power in MVA: sqrt(3) * V(kV) * I(A) / 1000 for a three-phase circuit.</summary>
    public double LoadMva => Math.Sqrt(3) * VoltageKv * CurrentA / 1000.0;
}

/// <summary>A command an operator sends to the field.</summary>
public sealed record BreakerCommand(string FeederId, BreakerState TargetState);

/// <summary>An alarm raised or cleared by the alarm engine.</summary>
public sealed record Alarm(
    string FeederId,
    string FeederName,
    string Kind,
    string Message,
    AlarmSeverity Severity,
    DateTimeOffset RaisedAt,
    bool IsActive);
