using System.Text;
using System.Text.Json;

namespace AIUsageMonitor.Services;

public static class JsonFileUtility
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(json, options);

    public static string Serialize<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options);

    public static Task<string> ReadTextAsync(string path) => File.ReadAllTextAsync(path, Utf8);

    public static string ReadText(string path) => File.ReadAllText(path, Utf8);

    public static async Task<T?> ReadAsync<T>(string path, JsonSerializerOptions? options = null)
    {
        var json = await ReadTextAsync(path);
        return Deserialize<T>(json, options);
    }

    public static async Task WriteAsync<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        var json = Serialize(value, options);
        await File.WriteAllTextAsync(path, json, Utf8);
    }

    public static T? Read<T>(string path, JsonSerializerOptions? options = null) =>
        Deserialize<T>(ReadText(path), options);

    public static void Write<T>(string path, T value, JsonSerializerOptions? options = null) =>
        File.WriteAllText(path, Serialize(value, options), Utf8);
}
