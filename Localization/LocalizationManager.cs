using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AIUsageMonitor.Localization;

public static class LocalizationManager
{
    private const string FallbackLanguage = "en";
    private static readonly object Sync = new();
    private static IReadOnlyDictionary<string, string> _fallbackStrings = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string> _activeStrings = new Dictionary<string, string>();
    private static bool _initialized;

    public static string ActiveLanguage { get; private set; } = FallbackLanguage;

    public static void Initialize(string? languageCode)
    {
        lock (Sync)
        {
            _fallbackStrings = LoadCatalog(FallbackLanguage);
            ActiveLanguage = ResolveLanguage(languageCode);
            _activeStrings = ActiveLanguage == FallbackLanguage
                ? _fallbackStrings
                : LoadCatalog(ActiveLanguage);
            _initialized = true;
        }
    }

    public static string NormalizePreference(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) ||
            string.Equals(languageCode, "system", StringComparison.OrdinalIgnoreCase))
            return "system";

        string language = languageCode.Trim().Split('-', '_')[0];
        return language.Equals("ko", StringComparison.OrdinalIgnoreCase) ? "ko" : "en";
    }

    public static string Text(string key)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (_activeStrings.TryGetValue(key, out string? activeValue))
            return activeValue;

        if (_fallbackStrings.TryGetValue(key, out string? fallbackValue))
            return fallbackValue;

        Debug.WriteLine($"Missing localization key: {key}");
        return $"!{key}!";
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.GetCultureInfo(ActiveLanguage), Text(key), arguments);

    private static string ResolveLanguage(string? languageCode)
    {
        string preference = NormalizePreference(languageCode);
        if (preference != "system")
            return preference;

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase)
            ? "ko"
            : FallbackLanguage;
    }

    private static Dictionary<string, string> LoadCatalog(string language)
    {
        string resourceName = $"AIUsageMonitor.Resources.Strings.{language}.json";
        using Stream stream = typeof(LocalizationManager).GetTypeInfo().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Localization resource '{resourceName}' was not embedded.");
        using JsonDocument document = JsonDocument.Parse(stream);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Localization resource '{resourceName}' must contain a JSON object.");

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Localization resource '{resourceName}' contains an invalid entry.");

            if (!strings.TryAdd(property.Name, property.Value.GetString() ?? string.Empty))
                throw new InvalidDataException($"Localization resource '{resourceName}' contains duplicate key '{property.Name}'.");
        }

        return strings;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        Initialize("system");
    }
}
