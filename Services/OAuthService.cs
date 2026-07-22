using System.Net;
using System.Diagnostics;

namespace AIUsageMonitor.Services;

public class OAuthService
{
    private const int Port = 9988;
    private readonly string _redirectUri = $"http://localhost:{Port}/oauth-callback";

    public string RedirectUri => _redirectUri;

    public async Task<string> ReceiveCodeAsync(string authUrl, CancellationToken cancellationToken = default)
    {
        using var listener = new HttpListener();
        try 
        {
            Log.Info($"Starting listener on {_redirectUri}/");
            listener.Prefixes.Add(_redirectUri + "/");
            listener.Start();
            Log.Info("Listener started successfully.");
        }
        catch (HttpListenerException ex)
        {
            Log.Error("Listener Error", ex);
            if (ex.ErrorCode == 5) // Access Denied
            {
                throw new Exception("Access Denied: Please run as Admin or add URL ACL for the port.");
            }
            else if (ex.ErrorCode == 183) // Already in use
            {
                throw new Exception("Port already in use. Please close other programs or change the port.");
            }
            throw;
        }

        try
        {
            // Open browser
            Log.Info($"Opening browser: {authUrl}");
            await Browser.Default.OpenAsync(authUrl, BrowserLaunchMode.External);

            // Wait for request with cancellation support
            Log.Info("Waiting for callback...");
            var contextTask = listener.GetContextAsync();
            var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
            var completedTask = await Task.WhenAny(contextTask, cancelTask);

            if (completedTask == cancelTask)
            {
                listener.Stop();
                throw new OperationCanceledException("Login was cancelled by user.");
            }

            var context = await contextTask;
            var request = context.Request;
            
            Log.Info($"Request received: {request.Url}");
            var code = request.QueryString["code"];
            var error = request.QueryString["error"];

            using (var response = context.Response)
            {
                string responseString = "<html><body style='font-family: sans-serif; text-align: center; padding-top: 50px;'>" +
                                        "<h1>AIUsageMonitor Login</h1>" +
                                        (string.IsNullOrEmpty(error) ? "<p style='color: green;'>Login successful! You can close this window now.</p>" : $"<p style='color: red;'>Error: {error}</p>") +
                                        "</body></html>";
                
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }

            listener.Stop();
            Log.Info("Listener stopped.");

            if (!string.IsNullOrEmpty(error))
                throw new Exception($"OAuth error: {error}");

            return code ?? throw new Exception("No code received");
        }
        catch (OperationCanceledException)
        {
            Log.Info("Login cancelled by user.");
            if (listener.IsListening) listener.Stop();
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Runtime Error", ex);
            if (listener.IsListening) listener.Stop();
            throw;
        }
    }
}
