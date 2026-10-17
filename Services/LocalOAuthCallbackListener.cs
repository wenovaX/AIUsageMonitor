using System.Net;
using System.Security.Cryptography;
using System.Text;
using AIUsageMonitor.Localization;

namespace AIUsageMonitor.Services;

public static class LocalOAuthCallbackListener
{
    public static string CreateState() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static async Task<string> ReceiveCodeAsync(
        string redirectUri,
        string authUrl,
        string expectedState,
        string providerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedState))
            throw new InvalidOperationException("OAuth state must be generated before starting the callback listener.");

        using var listener = new HttpListener();
        var listenerPrefix = redirectUri.TrimEnd('/') + "/";

        try
        {
            Log.Info($"Starting {providerName} OAuth listener on {listenerPrefix}");
            listener.Prefixes.Add(listenerPrefix);
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Log.Error($"{providerName} OAuth listener error", ex);
            if (ex.ErrorCode == 5)
                throw new InvalidOperationException("Access denied while opening the OAuth callback listener.", ex);
            if (ex.ErrorCode == 183)
                throw new InvalidOperationException("The OAuth callback port is already in use.", ex);
            throw;
        }

        try
        {
            Log.Info($"Opening {providerName} authorization URL.");
            await Browser.Default.OpenAsync(authUrl, BrowserLaunchMode.External);

            var contextTask = listener.GetContextAsync();
            var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
            if (await Task.WhenAny(contextTask, cancellationTask) == cancellationTask)
            {
                listener.Stop();
                throw new OperationCanceledException(cancellationToken);
            }

            var context = await contextTask;
            var request = context.Request;
            var returnedState = request.QueryString["state"];
            var code = request.QueryString["code"];
            var providerError = request.QueryString["error"];

            string? callbackError = null;
            if (!string.Equals(returnedState, expectedState, StringComparison.Ordinal))
                callbackError = "OAuth state validation failed.";
            else if (!string.IsNullOrWhiteSpace(providerError))
                callbackError = providerError;
            else if (string.IsNullOrWhiteSpace(code))
                callbackError = "No authorization code was received.";

            await RespondAsync(context.Response, providerName, callbackError);

            if (callbackError is not null)
                throw new InvalidOperationException($"{providerName} OAuth callback failed: {callbackError}");

            return code!;
        }
        catch (OperationCanceledException)
        {
            Log.Info($"{providerName} OAuth login cancelled.");
            if (listener.IsListening)
                listener.Stop();
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"{providerName} OAuth callback failed", ex);
            if (listener.IsListening)
                listener.Stop();
            throw;
        }
    }

    private static async Task RespondAsync(HttpListenerResponse response, string providerName, string? error)
    {
        using (response)
        {
            var messageText = error is null
                ? LocalizationManager.Format("oauth.callback_success", providerName)
                : LocalizationManager.Format("oauth.callback_failed", error);
            var messageColor = error is null ? "#10b981" : "#ef4444";
            var message = $"<p style='color:{messageColor}'>{WebUtility.HtmlEncode(messageText)}</p>";
            var html = $"<html><body style='font-family:sans-serif;text-align:center;padding-top:50px'>" +
                       $"<h1>AIUsageMonitor</h1>{message}</body></html>";
            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
    }
}
