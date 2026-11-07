using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GridMonitor.Core.Mvvm;

/// <summary>
/// Base class for view models. Raises <see cref="PropertyChanged"/> so WPF data
/// binding refreshes the UI whenever a property value changes.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Sets the backing field and raises PropertyChanged only when the value actually changes,
    /// which avoids needless UI work when the same value arrives repeatedly.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
