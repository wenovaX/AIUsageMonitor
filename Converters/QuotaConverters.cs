using System.Globalization;

namespace AIUsageMonitor.Converters;

public class PercentageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int p) return p / 100.0;
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class PercentageColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double p = 0;
        if (value is int i) p = i;
        else if (value is double d) p = d;
        else if (value is float f) p = f;
        else return Colors.Gray;

        // If a parameter is provided, it's used as the "target" color for high values
        Color targetColor = parameter is Color c ? c : Colors.Green;

        if (p > 50) return targetColor;
        if (p > 20) return Colors.Orange;
        return Colors.Red;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool val && val;
        if (parameter is Color c)
        {
            return b ? c : Color.FromArgb("#64748b"); // Slate 500 as default inactive
        }
        return b ? Colors.Green : Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
}

