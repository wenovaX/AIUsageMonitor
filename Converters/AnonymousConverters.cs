using System.Globalization;
using AIUsageMonitor.Models;

namespace AIUsageMonitor.Converters;


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
