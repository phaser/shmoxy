using System.Text.RegularExpressions;

namespace shmoxy.api.tests;

/// <summary>
/// Consistency checks between scripts/start.sh, the Dockerfile and the application's own
/// configuration. These values live in separate files with nothing but convention holding
/// them together, and both bugs they guard against failed silently -- the app started,
/// reported healthy, and quietly did the wrong thing.
///
/// These are static checks on the shell script, not an execution of it: they catch the
/// specific drift that bit us, not every possible scripting mistake.
/// </summary>
public class StartScriptTests
{
    /// <summary>
    /// The volume mount target must be the directory the container writes its state to.
    /// When these drifted apart the SQLite database and data protection keys lived in the
    /// container's writable layer: saved traces, sessions and settings were destroyed on
    /// every container recreation, and the new keys invalidated the browser's antiforgery
    /// cookie. A local run was unaffected, which is why it went unnoticed.
    /// </summary>
    [Fact]
    public void MountTarget_MatchesDockerfileDataDirectory()
    {
        var configuredDirectory = Regex.Match(
            ReadRepositoryFile("Dockerfile"),
            @"ENV\s+ApiConfig__DataDirectory=(?<path>\S+)").Groups["path"].Value;

        var mountTarget = Regex.Match(
            ReadRepositoryFile("scripts", "start.sh"),
            @"-v\s+shmoxy-data:(?<path>[^\s\\]+)").Groups["path"].Value;

        Assert.False(
            string.IsNullOrEmpty(configuredDirectory),
            "Dockerfile must set ApiConfig__DataDirectory so the state path is explicit");
        Assert.False(
            string.IsNullOrEmpty(mountTarget),
            "scripts/start.sh must mount the shmoxy-data volume");

        Assert.Equal(configuredDirectory, mountTarget);
    }

    /// <summary>
    /// The Dockerfile must pin the data directory explicitly. Falling back to the platform
    /// application-data directory inside a container puts the database in the writable
    /// layer, where a volume mount cannot reach it.
    /// </summary>
    [Fact]
    public void Dockerfile_SetsDataDirectoryExplicitly()
    {
        Assert.Matches(@"ENV\s+ApiConfig__DataDirectory=\S+", ReadRepositoryFile("Dockerfile"));
    }

    /// <summary>
    /// The bare-metal path must announce the port the proxy will actually bind.
    ///
    /// ProxyProcessManager prefers the persisted proxy config over ApiConfig:ProxyPort, so a
    /// saved port silently overrides --proxy-port. Docker hides this by mapping the
    /// requested host port onto the persisted one; bare metal has no mapping, so printing
    /// the requested port left the user pointing a browser at a dead port.
    /// </summary>
    [Fact]
    public void BareMetalPath_AnnouncesEffectiveProxyPort()
    {
        var startScript = ReadRepositoryFile("scripts", "start.sh");

        Assert.Contains("EFFECTIVE_PROXY_PORT", startScript);
        Assert.Matches(
            @"Starting shmoxy API on port \$API_PORT \(proxy on port \$EFFECTIVE_PROXY_PORT\)",
            startScript);
    }

    /// <summary>
    /// A persisted port that disagrees with --proxy-port must be surfaced, not swallowed.
    /// </summary>
    [Fact]
    public void BareMetalPath_WarnsWhenPersistedPortOverridesRequestedPort()
    {
        var startScript = ReadRepositoryFile("scripts", "start.sh");

        Assert.Contains("WARNING: persisted proxy config", startScript);
    }

    private static string ReadRepositoryFile(params string[] relativePath) =>
        File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(relativePath).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dockerfile")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
