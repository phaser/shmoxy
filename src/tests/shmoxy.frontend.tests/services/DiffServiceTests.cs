using shmoxy.frontend.models;
using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class DiffServiceTests
{
    // --- DiffLines ---

    [Fact]
    public void DiffLines_IdenticalTexts_AllUnchanged()
    {
        var rows = DiffService.DiffLines("a\nb\nc", "a\nb\nc");

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(DiffKind.Unchanged, r.Kind));
    }

    [Fact]
    public void DiffLines_AddedLine_MarkedAdded()
    {
        var rows = DiffService.DiffLines("a\nc", "a\nb\nc");

        Assert.Equal(3, rows.Count);
        Assert.Equal(DiffKind.Unchanged, rows[0].Kind);
        Assert.Equal(DiffKind.Added, rows[1].Kind);
        Assert.Null(rows[1].LeftLine);
        Assert.Equal("b", rows[1].RightLine);
        Assert.Equal(DiffKind.Unchanged, rows[2].Kind);
    }

    [Fact]
    public void DiffLines_RemovedLine_MarkedRemoved()
    {
        var rows = DiffService.DiffLines("a\nb\nc", "a\nc");

        Assert.Equal(3, rows.Count);
        Assert.Equal(DiffKind.Removed, rows[1].Kind);
        Assert.Equal("b", rows[1].LeftLine);
        Assert.Null(rows[1].RightLine);
    }

    [Fact]
    public void DiffLines_ModifiedLine_PairedAsChanged()
    {
        var rows = DiffService.DiffLines("a\nold\nc", "a\nnew\nc");

        Assert.Equal(3, rows.Count);
        Assert.Equal(DiffKind.Changed, rows[1].Kind);
        Assert.Equal("old", rows[1].LeftLine);
        Assert.Equal("new", rows[1].RightLine);
    }

    [Fact]
    public void DiffLines_UnevenChange_PairsThenAdds()
    {
        var rows = DiffService.DiffLines("a\nx\nz", "a\ny1\ny2\nz");

        Assert.Equal(4, rows.Count);
        Assert.Equal(DiffKind.Changed, rows[1].Kind);
        Assert.Equal(DiffKind.Added, rows[2].Kind);
        Assert.Equal("y2", rows[2].RightLine);
    }

    [Fact]
    public void DiffLines_EmptyLeft_AllAdded()
    {
        var rows = DiffService.DiffLines("", "a\nb");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(DiffKind.Added, r.Kind));
    }

    [Fact]
    public void DiffLines_NormalizesCrLf()
    {
        var rows = DiffService.DiffLines("a\r\nb", "a\nb");

        Assert.All(rows, r => Assert.Equal(DiffKind.Unchanged, r.Kind));
    }

    // --- DiffNamedValues ---

    [Fact]
    public void DiffNamedValues_AlignsByName()
    {
        var left = new List<KeyValuePair<string, string>> { new("Host", "a.com"), new("Accept", "json") };
        var right = new List<KeyValuePair<string, string>> { new("host", "a.com"), new("Authorization", "Bearer x") };

        var diff = DiffService.DiffNamedValues(left, right, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(3, diff.Count);
        Assert.Equal(DiffKind.Unchanged, diff.Single(d => d.Name == "Host").Kind);
        Assert.Equal(DiffKind.Removed, diff.Single(d => d.Name == "Accept").Kind);
        Assert.Equal(DiffKind.Added, diff.Single(d => d.Name == "Authorization").Kind);
    }

    [Fact]
    public void DiffNamedValues_ValueChange_MarkedChanged()
    {
        var left = new List<KeyValuePair<string, string>> { new("Content-Length", "10") };
        var right = new List<KeyValuePair<string, string>> { new("Content-Length", "20") };

        var diff = DiffService.DiffNamedValues(left, right, StringComparer.OrdinalIgnoreCase);

        var entry = Assert.Single(diff);
        Assert.Equal(DiffKind.Changed, entry.Kind);
        Assert.Equal("10", entry.LeftValue);
        Assert.Equal("20", entry.RightValue);
    }

    [Fact]
    public void DiffNamedValues_CollapsesRepeatedNames()
    {
        var left = new List<KeyValuePair<string, string>> { new("Set-Cookie", "a=1"), new("Set-Cookie", "b=2") };
        var right = new List<KeyValuePair<string, string>> { new("Set-Cookie", "a=1, b=2") };

        var diff = DiffService.DiffNamedValues(left, right, StringComparer.OrdinalIgnoreCase);

        var entry = Assert.Single(diff);
        Assert.Equal(DiffKind.Unchanged, entry.Kind);
    }

    [Fact]
    public void DiffNamedValues_NullInputs_Empty()
    {
        var diff = DiffService.DiffNamedValues(null, null, StringComparer.OrdinalIgnoreCase);

        Assert.Empty(diff);
    }

    // --- DiffUrls ---

    [Fact]
    public void DiffUrls_SameBaseDifferentQuery_AlignsParams()
    {
        var diff = DiffService.DiffUrls(
            "https://api.example.com/v1/users?page=1&size=10",
            "https://api.example.com/v1/users?page=2&filter=active");

        Assert.False(diff.BaseChanged);
        Assert.Equal(DiffKind.Changed, diff.QueryParams.Single(p => p.Name == "page").Kind);
        Assert.Equal(DiffKind.Removed, diff.QueryParams.Single(p => p.Name == "size").Kind);
        Assert.Equal(DiffKind.Added, diff.QueryParams.Single(p => p.Name == "filter").Kind);
    }

    [Fact]
    public void DiffUrls_PathChange_SetsBaseChanged()
    {
        var diff = DiffService.DiffUrls("https://a.com/v1/users", "https://a.com/v2/users");

        Assert.True(diff.BaseChanged);
        Assert.Empty(diff.QueryParams);
    }

    [Fact]
    public void DiffUrls_UnescapesQueryValues()
    {
        var diff = DiffService.DiffUrls(
            "https://a.com/?q=hello%20world",
            "https://a.com/?q=hello%20world");

        var param = Assert.Single(diff.QueryParams);
        Assert.Equal("hello world", param.LeftValue);
        Assert.Equal(DiffKind.Unchanged, param.Kind);
    }

    [Fact]
    public void ParseQuery_HandlesValuelessParams()
    {
        var parsed = DiffService.ParseQuery("flag&x=1");

        Assert.Equal(2, parsed.Count);
        Assert.Equal("flag", parsed[0].Key);
        Assert.Equal("", parsed[0].Value);
    }

    // --- DiffBody ---

    [Fact]
    public void DiffBody_BothEmpty_KindEmpty()
    {
        var diff = DiffService.DiffBody(null, null, null, "", null, null);

        Assert.Equal(BodyDiffKind.Empty, diff.Kind);
        Assert.True(diff.Identical);
    }

    [Fact]
    public void DiffBody_BinarySide_NoTextDiff()
    {
        var base64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6 });

        var diff = DiffService.DiffBody(null, base64, "image/png", "text", null, "text/plain");

        Assert.Equal(BodyDiffKind.Binary, diff.Kind);
        Assert.False(diff.Identical);
        Assert.Null(diff.Rows);
        Assert.Equal(6, diff.LeftSize);
    }

    [Fact]
    public void DiffBody_IdenticalBinary_Identical()
    {
        var base64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });

        var diff = DiffService.DiffBody(null, base64, "image/png", null, base64, "image/png");

        Assert.Equal(BodyDiffKind.Binary, diff.Kind);
        Assert.True(diff.Identical);
    }

    [Fact]
    public void DiffBody_EquivalentJson_DifferentFormatting_IsIdentical()
    {
        var diff = DiffService.DiffBody(
            "{\"a\":1,\"b\":2}", null, "application/json",
            "{ \"a\": 1, \"b\": 2 }", null, "application/json");

        Assert.Equal(BodyDiffKind.Text, diff.Kind);
        Assert.True(diff.Identical);
    }

    [Fact]
    public void DiffBody_JsonValueChange_ProducesChangedRow()
    {
        var diff = DiffService.DiffBody(
            "{\"status\":\"ok\",\"count\":1}", null, "application/json",
            "{\"status\":\"error\",\"count\":1}", null, "application/json");

        Assert.Equal(BodyDiffKind.Text, diff.Kind);
        Assert.False(diff.Identical);
        Assert.Contains(diff.Rows!, r => r.Kind == DiffKind.Changed);
        Assert.Contains(diff.Rows!, r => r.Kind == DiffKind.Unchanged);
    }

    [Fact]
    public void DiffBody_OversizedBody_FallsBackToTooLarge()
    {
        var big = new string('x', DiffService.MaxDiffableBodyChars + 1);

        var diff = DiffService.DiffBody(big, null, "text/plain", big, null, "text/plain");

        Assert.Equal(BodyDiffKind.TooLarge, diff.Kind);
        Assert.True(diff.Identical);
        Assert.Null(diff.Rows);
    }

    [Fact]
    public void DiffBody_TooManyLines_FallsBackToTooLarge()
    {
        var manyLines = string.Join('\n', Enumerable.Range(0, DiffService.MaxDiffableLines + 1).Select(i => i.ToString()));

        var diff = DiffService.DiffBody(manyLines, null, "text/plain", "short", null, "text/plain");

        Assert.Equal(BodyDiffKind.TooLarge, diff.Kind);
        Assert.False(diff.Identical);
    }
}
