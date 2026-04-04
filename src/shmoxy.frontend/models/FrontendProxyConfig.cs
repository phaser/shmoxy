using shmoxy.shared.ipc;

namespace shmoxy.frontend.services;

public class FrontendProxyConfig
{
    public int Port { get; set; } = 8080;
    public int HttpsPort { get; set; }
    public string? CertPath { get; set; }
    public string? KeyPath { get; set; }
    public string LogLevel { get; set; } = "Info";
    public string CertStoragePath { get; set; } = "";
    public List<string> PassthroughHosts { get; set; } = new();
    public bool SessionLoggingEnabled { get; set; }
    public bool ValidateUpstreamCertificates { get; set; }
    public bool DisableCaching { get; set; }
    public List<ClientCertConfig> ClientCertificates { get; set; } = new();
    public int ConnectionPoolSizePerHost { get; set; } = 4;
    public int ConnectionPoolIdleTimeoutSeconds { get; set; } = 60;

    public static FrontendProxyConfig Default => new();
}
