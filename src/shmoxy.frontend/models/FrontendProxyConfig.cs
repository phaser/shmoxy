namespace shmoxy.frontend.services;

public class FrontendProxyConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8080;
    public bool EnableHttps { get; set; }
    public string CertificatePath { get; set; } = "";

    public static FrontendProxyConfig Default => new();
}
