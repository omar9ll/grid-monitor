using System.ComponentModel;
using GridMonitor.Core.Models;
using GridMonitor.Core.Threading;
using GridMonitor.Core.ViewModels;
using static GridMonitor.Core.Tests.TestData;

namespace GridMonitor.Core.Tests;

public class ViewModelTests
{
    private readonly FakeDataSource _source = new(Feeder);

    [Fact]
    public void ApplyBatch_UpdatesFeederValues()
    {
        var vm = new MainViewModel(_source, new ImmediateDispatcher());

        vm.ApplyBatch([Reading(currentA: 200, voltageKv: 13.7)], readingsPerSecond: 30);

        var feeder = Assert.Single(vm.Feeders);
        Assert.Equal(200, feeder.CurrentA);
        Assert.Equal(13.7, feeder.VoltageKv);
        Assert.Equal(50, feeder.LoadingPercent, precision: 6);
        Assert.Equal(30, vm.ReadingsPerSecond);
        Assert.Single(feeder.LoadHistory);
    }

    [Fact]
    public void ApplyBatch_RaisesPropertyChangedForBinding()
    {
        var vm = new MainViewModel(_source, new ImmediateDispatcher());
        var feeder = vm.Feeders[0];
        var changed = new List<string?>();
        ((INotifyPropertyChanged)feeder).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ApplyBatch([Reading(currentA: 200)], 1);

        Assert.Contains(nameof(FeederViewModel.CurrentA), changed);
        Assert.Contains(nameof(FeederViewModel.LoadingPercent), changed);
        Assert.Contains(nameof(FeederViewModel.LoadHistory), changed);
    }

    [Fact]
    public void ApplyBatch_UpdatesLastUpdateText()
    {
        var vm = new MainViewModel(_source, new ImmediateDispatcher());
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("-", vm.LastUpdateText);

        var reading = Reading(currentA: 200);
        vm.ApplyBatch([reading], 1);

        Assert.Equal(reading.Timestamp.ToLocalTime().ToString("HH:mm:ss.f"), vm.LastUpdateText);
        Assert.Contains(nameof(MainViewModel.LastUpdateText), changed);
    }

    [Fact]
    public void Overload_AddsAlarm_AndMarksFeeder()
    {
        var vm = new MainViewModel(_source, new ImmediateDispatcher());

        vm.ApplyBatch([Reading(currentA: 430)], 1);

        var alarm = Assert.Single(vm.Alarms);
        Assert.Equal(AlarmSeverity.Critical, alarm.Severity);
        Assert.Equal(1, vm.ActiveAlarmCount);
        Assert.Equal(AlarmSeverity.Critical, vm.Feeders[0].AlarmSeverity);
        Assert.Equal("CRITICAL", vm.Feeders[0].Status);

        vm.ApplyBatch([Reading(currentA: 100)], 1);

        Assert.Equal(2, vm.Alarms.Count); // raise + clear
        Assert.False(vm.Alarms[0].IsActive);
        Assert.Null(vm.Feeders[0].AlarmSeverity);
    }

    [Fact]
    public async Task ToggleBreaker_SendsOpenCommand_WhenClosed()
    {
        var vm = new MainViewModel(_source, new ImmediateDispatcher());
        var feeder = vm.Feeders[0];

        await feeder.ToggleBreakerCommand.ExecuteAsync();

        var command = Assert.Single(_source.Commands);
        Assert.Equal(new BreakerCommand("F1", BreakerState.Open), command);
    }

    [Fact]
    public async Task StartStop_UpdatesState()
    {
        var vm = new MainViewModel(_source, new ImmediateDispatcher());
        Assert.True(vm.StartCommand.CanExecute(null));

        vm.Start();

        Assert.True(vm.IsRunning);
        Assert.Equal("Live", vm.StatusText);
        Assert.False(vm.StartCommand.CanExecute(null));

        await vm.StopAsync();

        Assert.False(vm.IsRunning);
        Assert.Equal("Stopped", vm.StatusText);
    }
}
