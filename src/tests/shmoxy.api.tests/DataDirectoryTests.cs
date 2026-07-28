namespace shmoxy.api.tests;

/// <summary>
/// Guards the persistence of API state across restarts.
///
/// The bug these cover: the app wrote its SQLite database and data protection keys to the
/// platform application-data directory (<c>/root/.config/shmoxy-api</c> on Linux) while
/// scripts/start.sh mounted its volume at <c>/root/.local/share/shmoxy-api</c> -- a
/// different directory. Nothing failed loudly. The database was simply recreated empty on
/// every container recreation, taking saved traces, sessions and settings with it, and new
/// data protection keys broke the antiforgery cookie the browser still held.
///
/// A local run was unaffected, which is why this survived: the same code persists
/// correctly outside a container.
/// </summary>
public class DataDirectoryTests
{
    [Fact]
    public void ResolveDataDirectory_UsesConfiguredPath_WhenSet()
    {
        var configured = Path.Combine(Path.GetTempPath(), $"shmoxy-data-{Guid.NewGuid():N}");

        try
        {
            var resolved = Program.ResolveDataDirectory(configured);

            Assert.Equal(configured, resolved);
            Assert.True(Directory.Exists(resolved), "the configured directory should be created");
        }
        finally
        {
            if (Directory.Exists(configured))
                Directory.Delete(configured, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDataDirectory_FallsBackToApplicationData_WhenNotSet(string? configured)
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "shmoxy-api");

        var resolved = Program.ResolveDataDirectory(configured);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void GetConnectionStringFor_PutsDatabaseInsideDataDirectory()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"shmoxy-data-{Guid.NewGuid():N}");

        var connectionString = Program.GetConnectionStringFor(dataDirectory);

        Assert.Equal($"Data Source={Path.Combine(dataDirectory, "proxies.db")}", connectionString);
    }

}
