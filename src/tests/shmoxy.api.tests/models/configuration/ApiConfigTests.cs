using shmoxy.api.models.configuration;

namespace shmoxy.api.tests.models.configuration;

public class ApiConfigTests
{
    [Fact]
    public void AutoStartProxy_DefaultsToTrue()
    {
        var config = new ApiConfig();
        Assert.True(config.AutoStartProxy);
    }
}
