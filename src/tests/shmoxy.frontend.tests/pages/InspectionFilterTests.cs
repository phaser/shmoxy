using shmoxy.frontend.pages;
using Xunit;

namespace shmoxy.frontend.tests.pages;

public class InspectionFilterTests
{
    [Theory]
    [InlineData(200, "all", true)]
    [InlineData(404, "all", true)]
    [InlineData(null, "all", true)]
    [InlineData(200, "2xx", true)]
    [InlineData(299, "2xx", true)]
    [InlineData(300, "2xx", false)]
    [InlineData(301, "3xx", true)]
    [InlineData(399, "3xx", true)]
    [InlineData(200, "3xx", false)]
    [InlineData(404, "4xx", true)]
    [InlineData(499, "4xx", true)]
    [InlineData(500, "4xx", false)]
    [InlineData(500, "5xx", true)]
    [InlineData(503, "5xx", true)]
    [InlineData(499, "5xx", false)]
    [InlineData(400, "errors", true)]
    [InlineData(500, "errors", true)]
    [InlineData(200, "errors", false)]
    [InlineData(301, "errors", false)]
    [InlineData(null, "2xx", false)]
    [InlineData(null, "errors", false)]
    public void MatchesStatusCodeFilter_FiltersCorrectly(int? statusCode, string filter, bool expected)
    {
        Assert.Equal(expected, Inspection.MatchesStatusCodeFilter(statusCode, filter));
    }
}
