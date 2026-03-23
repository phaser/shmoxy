using Microsoft.AspNetCore.Builder;
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
        services.AddScoped<ApiClient>();
        services.AddScoped<ThemeState>();

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
