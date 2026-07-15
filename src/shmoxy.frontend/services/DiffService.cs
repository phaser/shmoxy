using shmoxy.frontend.models;

namespace shmoxy.frontend.services;

/// <summary>
/// Client-side diffing for the trace comparison view: line-based text diff
/// (LCS), name/value alignment for headers and query parameters, and
/// structural URL comparison. No external diff dependency.
/// </summary>
public static class DiffService
{
    /// <summary>Bodies larger than this (in characters, after pretty-printing) are not line-diffed.</summary>
    public const int MaxDiffableBodyChars = 512_000;

    /// <summary>Bodies with more lines than this are not line-diffed (keeps the LCS table small).</summary>
    public const int MaxDiffableLines = 2_000;

    /// <summary>
    /// Line-based diff of two texts. Consecutive removed/added runs are
    /// paired into <see cref="DiffKind.Changed"/> rows so edits render on
    /// one row instead of a removal plus an addition.
    /// </summary>
    public static List<LineDiffRow> DiffLines(string left, string right)
    {
        var leftLines = SplitLines(left);
        var rightLines = SplitLines(right);

        // Trim common prefix/suffix so the LCS table only covers the changed middle.
        var prefix = 0;
        while (prefix < leftLines.Length && prefix < rightLines.Length && leftLines[prefix] == rightLines[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < leftLines.Length - prefix && suffix < rightLines.Length - prefix
               && leftLines[^(suffix + 1)] == rightLines[^(suffix + 1)])
            suffix++;

        var rows = new List<LineDiffRow>();
        for (var i = 0; i < prefix; i++)
            rows.Add(new LineDiffRow(leftLines[i], rightLines[i], DiffKind.Unchanged));

        var leftMiddle = leftLines[prefix..(leftLines.Length - suffix)];
        var rightMiddle = rightLines[prefix..(rightLines.Length - suffix)];
        rows.AddRange(DiffMiddle(leftMiddle, rightMiddle));

        for (var i = suffix; i >= 1; i--)
            rows.Add(new LineDiffRow(leftLines[^i], rightLines[^i], DiffKind.Unchanged));

        return rows;
    }

    private static List<LineDiffRow> DiffMiddle(string[] left, string[] right)
    {
        // Standard LCS dynamic program over the (already trimmed) middle.
        var lcs = new int[left.Length + 1, right.Length + 1];
        for (var i = left.Length - 1; i >= 0; i--)
        {
            for (var j = right.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = left[i] == right[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var rows = new List<LineDiffRow>();
        var removed = new List<string>();
        var added = new List<string>();
        int x = 0, y = 0;

        void FlushPending()
        {
            var paired = Math.Min(removed.Count, added.Count);
            for (var k = 0; k < paired; k++)
                rows.Add(new LineDiffRow(removed[k], added[k], DiffKind.Changed));
            for (var k = paired; k < removed.Count; k++)
                rows.Add(new LineDiffRow(removed[k], null, DiffKind.Removed));
            for (var k = paired; k < added.Count; k++)
                rows.Add(new LineDiffRow(null, added[k], DiffKind.Added));
            removed.Clear();
            added.Clear();
        }

        while (x < left.Length && y < right.Length)
        {
            if (left[x] == right[y])
            {
                FlushPending();
                rows.Add(new LineDiffRow(left[x], right[y], DiffKind.Unchanged));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                removed.Add(left[x]);
                x++;
            }
            else
            {
                added.Add(right[y]);
                y++;
            }
        }
        while (x < left.Length)
            removed.Add(left[x++]);
        while (y < right.Length)
            added.Add(right[y++]);
        FlushPending();

        return rows;
    }

    /// <summary>
    /// Aligns two name/value lists (headers, query parameters) by name.
    /// Left-side order is preserved; names only present on the right are
    /// appended in their own order. Repeated names are collapsed by joining
    /// their values with ", " before comparison.
    /// </summary>
    public static List<NamedValueDiff> DiffNamedValues(
        List<KeyValuePair<string, string>>? left,
        List<KeyValuePair<string, string>>? right,
        StringComparer nameComparer)
    {
        var leftValues = Collapse(left, nameComparer);
        var rightValues = Collapse(right, nameComparer);

        var result = new List<NamedValueDiff>();
        foreach (var (name, leftValue) in leftValues)
        {
            if (rightValues.TryGetValue(name, out var rightValue))
            {
                result.Add(new NamedValueDiff(name, leftValue, rightValue,
                    leftValue == rightValue ? DiffKind.Unchanged : DiffKind.Changed));
            }
            else
            {
                result.Add(new NamedValueDiff(name, leftValue, null, DiffKind.Removed));
            }
        }

        foreach (var (name, rightValue) in rightValues)
        {
            if (!leftValues.ContainsKey(name))
                result.Add(new NamedValueDiff(name, null, rightValue, DiffKind.Added));
        }

        return result;
    }

    private static Dictionary<string, string> Collapse(
        List<KeyValuePair<string, string>>? pairs, StringComparer nameComparer)
    {
        var result = new Dictionary<string, string>(nameComparer);
        if (pairs is null)
            return result;

        foreach (var (name, value) in pairs)
        {
            result[name] = result.TryGetValue(name, out var existing)
                ? $"{existing}, {value}"
                : value;
        }
        return result;
    }

    /// <summary>
    /// Structural URL diff: everything before the query string is compared as
    /// one "base" string; query parameters are aligned by name.
    /// </summary>
    public static UrlDiff DiffUrls(string leftUrl, string rightUrl)
    {
        var (leftBase, leftQuery) = SplitUrl(leftUrl);
        var (rightBase, rightQuery) = SplitUrl(rightUrl);

        var queryParams = DiffNamedValues(
            ParseQuery(leftQuery), ParseQuery(rightQuery), StringComparer.Ordinal);

        return new UrlDiff(leftBase, rightBase, leftBase != rightBase, queryParams);
    }

    private static (string Base, string Query) SplitUrl(string url)
    {
        var queryIndex = url.IndexOf('?');
        return queryIndex < 0
            ? (url, string.Empty)
            : (url[..queryIndex], url[(queryIndex + 1)..]);
    }

    internal static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(query))
            return result;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = eq < 0 ? pair : pair[..eq];
            var value = eq < 0 ? string.Empty : pair[(eq + 1)..];
            result.Add(new KeyValuePair<string, string>(
                Uri.UnescapeDataString(name), Uri.UnescapeDataString(value)));
        }
        return result;
    }

    /// <summary>
    /// Compares two payload bodies. Binary payloads (base64 present) and
    /// oversized payloads are not line-diffed — only equality and sizes are
    /// reported. Text payloads are pretty-printed first so formatting noise
    /// doesn't drown real changes.
    /// </summary>
    public static BodyDiff DiffBody(
        string? leftText, string? leftBase64, string? leftContentType,
        string? rightText, string? rightBase64, string? rightContentType)
    {
        var leftIsBinary = !string.IsNullOrEmpty(leftBase64);
        var rightIsBinary = !string.IsNullOrEmpty(rightBase64);
        var leftSize = leftIsBinary ? Base64Size(leftBase64!) : ByteCount(leftText);
        var rightSize = rightIsBinary ? Base64Size(rightBase64!) : ByteCount(rightText);

        if (string.IsNullOrEmpty(leftText) && string.IsNullOrEmpty(rightText) && !leftIsBinary && !rightIsBinary)
            return new BodyDiff(BodyDiffKind.Empty, true, 0, 0, leftContentType, rightContentType, null);

        if (leftIsBinary || rightIsBinary)
        {
            var identical = leftIsBinary && rightIsBinary && leftBase64 == rightBase64;
            return new BodyDiff(BodyDiffKind.Binary, identical, leftSize, rightSize,
                leftContentType, rightContentType, null);
        }

        var (leftFormatted, _) = PayloadFormatter.Format(leftText ?? string.Empty, leftContentType);
        var (rightFormatted, _) = PayloadFormatter.Format(rightText ?? string.Empty, rightContentType);

        if (leftFormatted.Length > MaxDiffableBodyChars || rightFormatted.Length > MaxDiffableBodyChars
            || CountLines(leftFormatted) > MaxDiffableLines || CountLines(rightFormatted) > MaxDiffableLines)
        {
            return new BodyDiff(BodyDiffKind.TooLarge, leftText == rightText, leftSize, rightSize,
                leftContentType, rightContentType, null);
        }

        var rows = DiffLines(leftFormatted, rightFormatted);
        var isIdentical = rows.All(r => r.Kind == DiffKind.Unchanged);
        return new BodyDiff(BodyDiffKind.Text, isIdentical, leftSize, rightSize,
            leftContentType, rightContentType, rows);
    }

    private static string[] SplitLines(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n").Split('\n');

    private static int CountLines(string text) => text.Count(c => c == '\n') + 1;

    private static long Base64Size(string base64) => (long)base64.Length * 3 / 4;

    private static long ByteCount(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : System.Text.Encoding.UTF8.GetByteCount(text);
}
