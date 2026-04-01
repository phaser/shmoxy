using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using shmoxy.api.data;

namespace shmoxy.api.tests;

public class DataProtectionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DataProtectionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiConfig:AutoStartProxy"] = "false"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<ProxiesDbContext>(options =>
                    options.UseSqlite("Data Source=:memory:"));
            });
        });
    }

    [Fact]
    public void DataProtection_CanProtectAndUnprotect()
    {
        var provider = _factory.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = provider.CreateProtector("test-purpose");

        var original = "test-payload";
        var protectedData = protector.Protect(original);
        var unprotected = protector.Unprotect(protectedData);

        Assert.Equal(original, unprotected);
    }

    [Fact]
    public void DataProtection_KeysDirectoryExists()
    {
        // Verify keys are persisted to disk by checking the directory exists
        var keysDir = Path.Combine(Program.GetDefaultDataDirectory(), "keys");
        Assert.True(Directory.Exists(keysDir));
    }
}
