using System.Globalization;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Converters;

public class AnonymousNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 3 && values[0] is bool isAnonymous && values[1] is string realName && values[2] is int index)
        {
            return isAnonymous ? $"User {index + 1}" : realName;
        }
        return "Unknown";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class AnonymousEmailConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 3 && values[0] is bool isAnonymous && values[1] is string realEmail && values[2] is string provider)
        {
            return isAnonymous ? provider.ToUpper() : realEmail;
        }
        return "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class AnonymousAvatarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string email && !string.IsNullOrEmpty(email))
        {
            return email[0].ToString().ToUpper();
        }
        return "U";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
