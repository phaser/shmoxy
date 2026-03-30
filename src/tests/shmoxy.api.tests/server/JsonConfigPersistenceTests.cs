using Microsoft.Extensions.Logging;
using Moq;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.tests.server;

public class JsonConfigPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonConfigPersistence _persistence;

    public JsonConfigPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"shmoxy-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var logger = new Mock<ILogger<JsonConfigPersistence>>();
        _persistence = new JsonConfigPersistence(logger.Object, _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsNull()
    {
        var result = await _persistence.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var config = new ProxyConfig
        {
            Port = 9999,
            LogLevel = ProxyConfig.LogLevelEnum.Warn,
            MaxConcurrentConnections = 42,
            PassthroughHosts = ["example.com", "*.test.com"],
            TempPassthroughMaxConnections = 5,
            TempPassthroughTimeoutSeconds = 60
        };

        await _persistence.SaveAsync(config);
        var loaded = await _persistence.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(9999, loaded.Port);
        Assert.Equal(ProxyConfig.LogLevelEnum.Warn, loaded.LogLevel);
        Assert.Equal(42, loaded.MaxConcurrentConnections);
        Assert.Equal(["example.com", "*.test.com"], loaded.PassthroughHosts);
        Assert.Equal(5, loaded.TempPassthroughMaxConnections);
        Assert.Equal(60, loaded.TempPassthroughTimeoutSeconds);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectory()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "dir");
        var logger = new Mock<ILogger<JsonConfigPersistence>>();
        var persistence = new JsonConfigPersistence(logger.Object, nestedDir);

        await persistence.SaveAsync(new ProxyConfig { Port = 1234 });

        var loaded = await persistence.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(1234, loaded.Port);
    }

    [Fact]
    public async Task SaveAsync_OverwritesPrevious()
    {
        await _persistence.SaveAsync(new ProxyConfig { Port = 1111 });
        await _persistence.SaveAsync(new ProxyConfig { Port = 2222 });

        var loaded = await _persistence.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(2222, loaded.Port);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsNull()
    {
        var configPath = Path.Combine(_tempDir, "proxy-config.json");
        await File.WriteAllTextAsync(configPath, "not valid json {{{");

        var loaded = await _persistence.LoadAsync();
        Assert.Null(loaded);
    }
}
