using GridMonitor.Core.Alarms;
using GridMonitor.Core.Models;
using static GridMonitor.Core.Tests.TestData;

namespace GridMonitor.Core.Tests;

public class AlarmEngineTests
{
    private readonly AlarmEngine _engine = new([Feeder]);

    [Fact]
    public void NormalLoad_RaisesNoAlarm()
    {
        var changes = _engine.Evaluate(Reading(currentA: 200));

        Assert.Empty(changes);
        Assert.Equal(0, _engine.ActiveCount);
    }

    [Fact]
    public void HighLoad_RaisesWarning_ThenEscalatesToCritical()
    {
        var warning = Assert.Single(_engine.Evaluate(Reading(currentA: 370))); // 92.5% of 400 A
        Assert.Equal(AlarmSeverity.Warning, warning.Severity);
        Assert.Equal("overload", warning.Kind);
        Assert.True(warning.IsActive);

        var critical = Assert.Single(_engine.Evaluate(Reading(currentA: 430))); // 107.5%
        Assert.Equal(AlarmSeverity.Critical, critical.Severity);
        Assert.Equal(1, _engine.ActiveCount);
    }

    [Fact]
    public void SameAlarm_IsNotRaisedTwice()
    {
        _engine.Evaluate(Reading(currentA: 370));

        Assert.Empty(_engine.Evaluate(Reading(currentA: 375)));
    }

    [Fact]
    public void Deadband_PreventsFlickeringNearTheLimit()
    {
        _engine.Evaluate(Reading(currentA: 370)); // raise at 92.5%

        // 88.75% is below the 90% limit but inside the 3% deadband, so the alarm stays.
        Assert.Empty(_engine.Evaluate(Reading(currentA: 355)));
        Assert.Equal(1, _engine.ActiveCount);

        // 80% is below 90% - 3%, so the alarm clears.
        var cleared = Assert.Single(_engine.Evaluate(Reading(currentA: 320)));
        Assert.False(cleared.IsActive);
        Assert.Equal(0, _engine.ActiveCount);
    }

    [Fact]
    public void LowVoltage_RaisesUndervoltageAlarm()
    {
        var alarm = Assert.Single(_engine.Evaluate(Reading(currentA: 200, voltageKv: 12.9))); // 0.935 pu

        Assert.Equal("undervoltage", alarm.Kind);
    }

    [Fact]
    public void OpeningBreaker_ClearsActiveAlarms()
    {
        _engine.Evaluate(Reading(currentA: 430, voltageKv: 12.9));
        Assert.Equal(2, _engine.ActiveCount);

        var changes = _engine.Evaluate(Reading(currentA: 0, voltageKv: 0, breaker: BreakerState.Open));

        Assert.Equal(2, changes.Count);
        Assert.All(changes, a => Assert.False(a.IsActive));
        Assert.Equal(0, _engine.ActiveCount);
    }
}
