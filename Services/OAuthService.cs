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
            Debug.WriteLine($"[OAuth] Starting listener on {_redirectUri}/");
            listener.Prefixes.Add(_redirectUri + "/");
            listener.Start();
            Debug.WriteLine("[OAuth] Listener started successfully.");
        }
        catch (HttpListenerException ex)
        {
            Debug.WriteLine($"[OAuth] Listener Error: {ex.Message} (Error Code: {ex.ErrorCode})");
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
            Debug.WriteLine($"[OAuth] Opening browser: {authUrl}");
            await Browser.Default.OpenAsync(authUrl, BrowserLaunchMode.External);

            // Wait for request with cancellation support
            Debug.WriteLine("[OAuth] Waiting for callback...");
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
            
            Debug.WriteLine($"[OAuth] Request received: {request.Url}");
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
            Debug.WriteLine("[OAuth] Listener stopped.");

            if (!string.IsNullOrEmpty(error))
                throw new Exception($"OAuth error: {error}");

            return code ?? throw new Exception("No code received");
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[OAuth] Login cancelled by user.");
            if (listener.IsListening) listener.Stop();
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OAuth] Runtime Error: {ex.Message}");
            if (listener.IsListening) listener.Stop();
            throw;
        }
    }
}
