using System;
using System.Text;
using System.Net.Sockets;

namespace shmoxy;

/// <summary>
/// Handles requests directed at the proxy itself (info page, cert download).
/// </summary>
public class InfoPageHandler : IDisposable
{
    private readonly ProxyConfig _config;
    private readonly TlsHandler _tlsHandler;
    private readonly DateTime _startTime;
    private readonly int _listeningPort;
    private bool _disposed;

    /// <summary>
    /// Creates a new info page handler.
    /// </summary>
    /// <param name="config">Proxy configuration.</param>
    /// <param name="tlsHandler">TLS handler for certificate export.</param>
    /// <param name="startTime">Server start time for uptime calculation.</param>
    /// <param name="listeningPort">Actual listening port (overrides config.Port when OS-assigned).</param>
    public InfoPageHandler(ProxyConfig config, TlsHandler tlsHandler, DateTime startTime, int listeningPort = 0)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tlsHandler = tlsHandler ?? throw new ArgumentNullException(nameof(tlsHandler));
        _startTime = startTime;
        _listeningPort = listeningPort > 0 ? listeningPort : config.Port;
    }

    /// <summary>
    /// Handles a request directed at the proxy itself.
    /// Routes to info page, cert download, or 404 based on path.
    /// </summary>
    public async Task HandleAsync(TcpClient client, string method, string path)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));

        try
        {
            var normalizedPath = NormalizePath(path);

            switch (normalizedPath.ToLowerInvariant())
            {
                case "/":
                case "/index.html":
                    await SendHtmlResponseAsync(client, GenerateInfoPage());
                    break;

                case "/shmoxy-ca.pem":
                    await SendFileDownloadAsync(
                        client, 
                        "shmoxy-ca.pem", 
                        "application/x-pem-file", 
                        Encoding.UTF8.GetBytes(_tlsHandler.ExportRootCertificatePem()));
                    break;

                case "/shmoxy-ca.crt":
                    await SendFileDownloadAsync(
                        client,
                        "shmoxy-ca.crt",
                        "application/vnd.ms-pkiseccert",
                        _tlsHandler.ExportRootCertificateDer());
                    break;

                default:
                    await Send404ResponseAsync(client);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Error handling request for path '{path}': {ex.Message}");
            await Send500ResponseAsync(client);
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
            return path.Substring(0, queryIndex);

        return path;
    }

    private string GenerateInfoPage()
    {
        var uptime = DateTime.UtcNow - _startTime;
        var certSubject = _tlsHandler.RootCertificate.Subject;
        var certExpiry = _tlsHandler.RootCertificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss UTC");

        return HtmlTemplate(_listeningPort, FormatUptime(uptime), certSubject, certExpiry);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
        if (uptime.TotalMinutes >= 1)
            return $"{uptime.Minutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }

    private string HtmlTemplate(int port, string uptime, string certSubject, string certExpiry) => $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Shmoxy Proxy - Information</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ font-family: system-ui, sans-serif; background: #f5f5f5; color: #333; line-height: 1.6; padding: 20px; }}
        .container {{ max-width: 900px; margin: 0 auto; }}
        h1 {{ color: #2c3e50; margin-bottom: 10px; font-size: 28px; }}
        h2 {{ color: #34495e; margin-top: 30px; margin-bottom: 15px; font-size: 22px; border-bottom: 2px solid #3498db; padding-bottom: 5px; }}
        h3 {{ color: #444; margin-top: 20px; margin-bottom: 10px; font-size: 18px; }}
        .subtitle {{ color: #7f8c8d; font-size: 16px; margin-bottom: 30px; }}
        table {{ width: 100%; border-collapse: collapse; background: white; box-shadow: 0 2px 5px rgba(0,0,0,0.1); margin-bottom: 20px; }}
        th, td {{ padding: 12px 15px; text-align: left; border-bottom: 1px solid #eee; }}
        th {{ background: #3498db; color: white; font-weight: 600; width: 150px; }}
        tr:hover {{ background: #f8f9fa; }}
        .download-section {{ background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 5px rgba(0,0,0,0.1); margin-bottom: 30px; }}
        .btn {{ display: inline-block; padding: 12px 24px; background: #3498db; color: white; text-decoration: none; border-radius: 5px; margin-right: 10px; font-weight: 600; }}
        .btn:hover {{ background: #2980b9; }}
        .btn-secondary {{ background: #27ae60; }}
        ol {{ margin-left: 20px; margin-top: 10px; }}
        li {{ margin-bottom: 8px; }}
        code {{ background: #f4f4f4; padding: 2px 6px; border-radius: 3px; font-family: monospace; font-size: 14px; color: #e74c3c; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Shmoxy Proxy Server</h1>
        <p class=""subtitle"">HTTP/HTTPS Proxy with TLS Termination and Certificate Inspection</p>

        <table>
            <tr><th>Listening Port</th><td>{port}</td></tr>
            <tr><th>Uptime</th><td>{uptime}</td></tr>
            <tr><th>Root CA Subject</th><td>{certSubject}</td></tr>
            <tr><th>Certificate Expiry</th><td>{certExpiry}</td></tr>
        </table>

        <div class=""download-section"">
            <h2>Download Root CA Certificate</h2>
            <p>To use this proxy, you must install and trust the root CA certificate below.</p>
            <br>
            <a href=""shmoxy-ca.pem"" class=""btn"">Download PEM Format</a>
            <a href=""shmoxy-ca.crt"" class=""btn btn-secondary"">Download CRT Format (Windows/macOS)</a>
        </div>

        <h2>Installation Instructions</h2>

        <h3>Windows Installation (Command Line)</h3>
        <p>Open PowerShell as Administrator and run:</p>
        <code style=""display:block; padding:10px; margin:10px 0; background:#f4f4f4;"">certutil -addstore -f ""ROOT"" shmoxy-ca.crt</code>

        <h3>Windows Installation (GUI)</h3>
        <ol>
            <li>Double-click the downloaded certificate file</li>
            <li>Click 'Install Certificate...'</li>
            <li>Select 'Local Machine' and click Next</li>
            <li>Choose 'Place all certificates in the following store'</li>
            <li>Select 'Trusted Root Certification Authorities'</li>
        </ol>

        <h3>macOS Installation (Command Line)</h3>
        <p>Open Terminal and run:</p>
        <code style=""display:block; padding:10px; margin:10px 0; background:#f4f4f4;"">sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain shmoxy-ca.crt</code>

        <h3>macOS Installation (GUI)</h3>
        <ol>
            <li>Double-click the downloaded certificate file to open in Keychain Access</li>
            <li>Find 'Shmoxy Proxy CA' under 'Certificates'</li>
            <li>Expand 'Trust' and set to 'Always Trust'</li>
        </ol>

        <h3>Linux (Debian/Ubuntu)</h3>
        <p>Copy the certificate and update:</p>
        <code style=""display:block; padding:10px; margin:10px 0; background:#f4f4f4;"">sudo cp shmoxy-ca.pem /usr/local/share/ca-certificates/</code>
        <code style=""display:block; padding:10px; margin:10px 0; background:#f4f4f4;"">sudo update-ca-certificates</code>

        <h3>Linux (RHEL/Fedora/CentOS)</h3>
        <p>Copy the certificate and update:</p>
        <code style=""display:block; padding:10px; margin:10px 0; background:#f4f4f4;"">sudo cp shmoxy-ca.pem /etc/pki/ca-trust/source/anchors/</code>
        <code style=""display:block; padding:10px; margin:10px 0; background:#f4f4f4;"">sudo update-ca-trust</code>

        <h3>Chrome on Windows/macOS</h3>
        <p>Chrome uses the OS certificate store. Follow your operating system instructions above.</p>

        <h3>Firefox Installation (GUI)</h3>
        <ol>
            <li>Open Firefox Settings > Privacy & Security</li>
            <li>Click 'View Certificates' under Certificates</li>
            <li>Click 'Import...' and select the certificate file</li>
        </ol>

        <h3>Microsoft Edge on Windows/macOS</h3>
        <p>Microsoft Edge uses the OS certificate store. Follow your operating system instructions above.</p>
    </div>
</body>
</html>";

    private async Task SendHtmlResponseAsync(TcpClient client, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Type: text/html; charset=utf-8\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
        await client.GetStream().WriteAsync(body);
    }

    private async Task SendFileDownloadAsync(TcpClient client, string filename, string contentType, byte[] data)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append($"Content-Type: {contentType}\r\n");
        sb.Append($"Content-Disposition: attachment; filename=\"{filename}\"\r\n");
        sb.Append($"Content-Length: {data.Length}\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
        await client.GetStream().WriteAsync(data);
    }

    private async Task Send404ResponseAsync(TcpClient client)
    {
        var body = Encoding.UTF8.GetBytes("<h1>404 Not Found</h1><p>The requested resource was not found on this server.</p>");
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 404 Not Found\r\n");
        sb.Append("Content-Type: text/html; charset=utf-8\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
        await client.GetStream().WriteAsync(body);
    }

    private async Task Send500ResponseAsync(TcpClient client)
    {
        var body = Encoding.UTF8.GetBytes("<h1>500 Internal Server Error</h1><p>An error occurred while processing your request.</p>");
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 500 Internal Server Error\r\n");
        sb.Append("Content-Type: text/html; charset=utf-8\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
        await client.GetStream().WriteAsync(body);
    }

    private void Log(string message)
    {
        if (_config.LogLevel <= ProxyConfig.LogLevelEnum.Debug)
            Console.WriteLine($"[DEBUG: [InfoPage] {message}]");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
