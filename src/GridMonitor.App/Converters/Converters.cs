using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GridMonitor.Core.Models;

namespace GridMonitor.App.Converters;

/// <summary>Turns a history of values into points for a <c>Polyline</c> trend line.</summary>
public sealed class SparklineConverter : IValueConverter
{
    public double Width { get; set; } = 260;

    public double Height { get; set; } = 60;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var points = new PointCollection();
        if (value is not double[] values || values.Length < 2)
        {
            return points;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var v in values)
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        var range = Math.Max(max - min, 0.001);
        var step = Width / (FeederHistoryLength - 1);
        var offset = FeederHistoryLength - values.Length; // right-align so new data enters from the right

        for (var i = 0; i < values.Length; i++)
        {
            var x = (offset + i) * step;
            var y = Height - ((values[i] - min) / range * (Height - 4)) - 2;
            points.Add(new Point(x, y));
        }

        points.Freeze(); // frozen objects render faster and are thread-safe
        return points;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static int FeederHistoryLength => Core.ViewModels.FeederViewModel.HistoryLength;
}

/// <summary>Maps a feeder status ("NORMAL", "WARNING", ...) to a colour.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Normal = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)));
    private static readonly Brush Warning = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)));
    private static readonly Brush Critical = Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
    private static readonly Brush Off = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "CRITICAL" => Critical,
        "WARNING" => Warning,
        "NORMAL" => Normal,
        AlarmSeverity.Critical => Critical,
        AlarmSeverity.Warning => Warning,
        _ => Off,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Breaker indicator colour. Utility convention: red = closed (energized), green = open.
/// </summary>
public sealed class BreakerToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Brushes.IndianRed : Brushes.MediumSeaGreen;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows a value only while a condition is true.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
