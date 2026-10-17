using System.Security.Cryptography;
using System.Text;

namespace AIUsageMonitor.Services;

public static class LocalDataProtector
{
    public static string Protect(string? value, string purpose)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        using var aes = Aes.Create();
        aes.Key = GetStableKey(purpose);
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(value);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return $"enc1:{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(cipherBytes)}";
    }

    public static string Unprotect(string? value, string purpose)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!value.StartsWith("enc1:", StringComparison.Ordinal))
            return value;

        var parts = value.Split(':');
        if (parts.Length != 3)
            return value;

        using var aes = Aes.Create();
        aes.Key = GetStableKey(purpose);
        aes.IV = Convert.FromBase64String(parts[1]);
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var cipherBytes = Convert.FromBase64String(parts[2]);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] GetStableKey(string purpose)
    {
        var source = $"{Environment.MachineName}|{Environment.UserName}|{purpose}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(source));
    }
}
