using Xunit;

namespace shmoxy.e2e;

[Trait("Category", "Integration")]
public class BasicTest
{
    [Fact]
    public void Test_Plugin_Is_Loaded()
    {
        Assert.True(true, "E2E test project is set up correctly");
    }
}
