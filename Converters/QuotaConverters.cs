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
        if (value is int p)
        {
            if (p > 50) return Colors.Green;
            if (p > 20) return Colors.Orange;
            return Colors.Red;
        }
        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
}

public class IntToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i && i > 0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class InversePercentageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int p) return 100 - p;
        return 100;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => !string.IsNullOrEmpty(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
