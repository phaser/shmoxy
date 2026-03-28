using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
                client.BaseAddress = new Uri(address);
            }
        });
        services.AddScoped<ThemeState>();
        services.AddScoped<FrontendSettings>();
        services.AddSingleton<InspectionDataService>();
        services.AddSingleton<ProxyStatusService>();

        return services;
    }

    public static WebApplication UseBlazorFrontendMiddleware(this WebApplication app)
    {
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
