using System.Text.Json;

namespace AIUsageMonitor.Services;

public readonly record struct LegacyAccountTokens(string? AccessToken, string? RefreshToken);

public static class LegacyAccountTokenReader
{
    public static LegacyAccountTokens FindForAccount(JsonElement root, string accountId)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return default;

        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("id", out var idProperty) ||
                idProperty.ValueKind != JsonValueKind.String ||
                !string.Equals(idProperty.GetString(), accountId, StringComparison.Ordinal))
                continue;

            return new LegacyAccountTokens(
                GetStringProperty(element, "access_token"),
                GetStringProperty(element, "refresh_token"));
        }

        return default;
    }

    private static string? GetStringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
