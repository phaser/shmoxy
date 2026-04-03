using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using shmoxy.frontend.services;

namespace shmoxy.frontend.extensions;

public static class FluentUiBlazorConfiguration
{
    public static IServiceCollection AddBlazorFrontend(this IServiceCollection services)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddFluentUIComponents();
        services.AddHttpClient<ApiClient>((sp, client) =>
        {
            var server = sp.GetRequiredService<IServer>();
            var addressFeature = server.Features.Get<IServerAddressesFeature>();
            var address = addressFeature?.Addresses.FirstOrDefault();
            if (address is not null)
            {
                client.BaseAddress = new Uri(NormalizeBindAddress(address));
            }
        });
        services.AddScoped<ThemeState>();
        services.AddScoped<FrontendSettings>();
        services.AddSingleton<InspectionDataService>();
        services.AddSingleton<ProxyStatusService>();
        services.AddScoped<KeyboardShortcutService>();

        return services;
    }

    /// <summary>
    /// Replaces wildcard bind addresses (0.0.0.0, [::], +, *) with localhost
    /// so the address can be used as an HTTP connection target.
    /// </summary>
    public static string NormalizeBindAddress(string address)
    {
        var normalized = address
            .Replace("://+:", "://localhost:")
            .Replace("://*:", "://localhost:")
            .Replace("://0.0.0.0:", "://localhost:")
            .Replace("://[::]:", "://localhost:");
        return new Uri(normalized).ToString();
    }

    public static WebApplication UseBlazorFrontendMiddleware(this WebApplication app)
    {
        // Resolve versioned CyberChef filename (e.g. CyberChef_v10.22.1.html)
        // when CyberChef.html is requested but doesn't exist on disk.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (path != null && path.EndsWith("/cyberchef/CyberChef.html", StringComparison.OrdinalIgnoreCase))
            {
                var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
                var relativePath = path.TrimStart('/');
                var fileInfo = env.WebRootFileProvider.GetFileInfo(relativePath);
                if (!fileInfo.Exists)
                {
                    var dirPath = Path.GetDirectoryName(relativePath)!;
                    var dirContents = env.WebRootFileProvider.GetDirectoryContents(dirPath);
                    var versioned = dirContents.FirstOrDefault(f =>
                        f.Name.StartsWith("CyberChef_v", StringComparison.OrdinalIgnoreCase) &&
                        f.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
                    if (versioned != null)
                    {
                        context.Request.Path = path.Replace("CyberChef.html", versioned.Name);
                    }
                }
            }
            await next();
        });

        app.UseStaticFiles();
        app.UseAntiforgery();
        return app;
    }

    public static WebApplication MapBlazorFrontend(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
