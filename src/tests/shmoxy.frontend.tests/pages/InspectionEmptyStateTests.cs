using shmoxy.frontend.pages;
using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.pages;

/// <summary>
/// The empty inspection table used to always read "Start the proxy to see requests",
/// regardless of state. That was actively misleading when the proxy was already running:
/// it pointed at the proxy while the real fault was the event stream, and it contradicted
/// the Proxy tab. These pin the message to the actual state.
/// </summary>
public class InspectionEmptyStateTests
{
    [Fact]
    public void Disconnected_TellsUserEventsArentArriving()
    {
        var message = Inspection.DescribeEmptyState(StreamConnectionState.Disconnected, totalRows: 0);

        Assert.Contains("INSP-DISCONNECTED", message);
        Assert.DoesNotContain("Start the proxy", message);
    }

    [Fact]
    public void Reconnecting_SaysSo()
    {
        var message = Inspection.DescribeEmptyState(StreamConnectionState.Reconnecting, totalRows: 0);

        Assert.Contains("INSP-RECONNECTING", message);
    }

    [Fact]
    public void Connected_WithNoRows_SaysWaitingForTraffic()
    {
        var message = Inspection.DescribeEmptyState(StreamConnectionState.Connected, totalRows: 0);

        Assert.Contains("INSP-WAITING", message);
        Assert.DoesNotContain("Start the proxy", message);
    }

    [Fact]
    public void Connected_WithRowsHiddenByFilters_BlamesTheFilters()
    {
        var message = Inspection.DescribeEmptyState(StreamConnectionState.Connected, totalRows: 12);

        Assert.Contains("INSP-FILTERED", message);
        Assert.Contains("filters", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(StreamConnectionState.Connected, 0)]
    [InlineData(StreamConnectionState.Connected, 5)]
    [InlineData(StreamConnectionState.Reconnecting, 0)]
    [InlineData(StreamConnectionState.Disconnected, 0)]
    [InlineData(StreamConnectionState.Disconnected, 5)]
    public void EveryState_ProducesADiagnosableCode(StreamConnectionState state, int totalRows)
    {
        var message = Inspection.DescribeEmptyState(state, totalRows);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("INSP-", message);
    }
}
