namespace shmoxy.frontend.services;

public class FrontendProxyConfig
{
    public int Port { get; set; } = 8080;
    public string? CertPath { get; set; }
    public string? KeyPath { get; set; }
    public string LogLevel { get; set; } = "Info";
    public int MaxConcurrentConnections { get; set; } = Environment.ProcessorCount * 4;
    public string CertStoragePath { get; set; } = "";
    public List<string> PassthroughHosts { get; set; } = new();
    public bool SessionLoggingEnabled { get; set; }
    public bool ValidateUpstreamCertificates { get; set; }

    public static FrontendProxyConfig Default => new();
}
