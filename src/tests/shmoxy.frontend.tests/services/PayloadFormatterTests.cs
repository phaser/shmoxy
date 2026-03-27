using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class PayloadFormatterTests
{
    [Fact]
    public void Format_ReturnsEmpty_WhenBodyIsNull()
    {
        var (content, language) = PayloadFormatter.Format(null, null);

        Assert.Equal(string.Empty, content);
        Assert.Equal("plaintext", language);
    }

    [Fact]
    public void Format_ReturnsEmpty_WhenBodyIsEmpty()
    {
        var (content, language) = PayloadFormatter.Format("", "application/json");

        Assert.Equal(string.Empty, content);
        Assert.Equal("plaintext", language);
    }

    [Fact]
    public void Format_DetectsJson_FromContentType()
    {
        var body = "{\"key\":\"value\"}";

        var (content, language) = PayloadFormatter.Format(body, "application/json");

        Assert.Equal("json", language);
        Assert.Contains("\"key\"", content);
        Assert.Contains("\"value\"", content);
    }

    [Fact]
    public void Format_PrettyPrintsJson()
    {
        var body = "{\"key\":\"value\",\"nested\":{\"a\":1}}";

        var (content, _) = PayloadFormatter.Format(body, "application/json");

        Assert.Contains("\n", content);
        Assert.Contains("  ", content);
    }

    [Fact]
    public void Format_DetectsXml_FromContentType()
    {
        var body = "<root><item>test</item></root>";

        var (content, language) = PayloadFormatter.Format(body, "application/xml");

        Assert.Equal("xml", language);
        Assert.Contains("<root>", content);
    }

    [Fact]
    public void Format_DetectsHtml_FromContentType()
    {
        var body = "<html><body>test</body></html>";

        var (_, language) = PayloadFormatter.Format(body, "text/html");

        Assert.Equal("html", language);
    }

    [Fact]
    public void Format_DetectsCss_FromContentType()
    {
        var body = "body { color: red; }";

        var (_, language) = PayloadFormatter.Format(body, "text/css");

        Assert.Equal("css", language);
    }

    [Fact]
    public void Format_DetectsJavaScript_FromContentType()
    {
        var body = "function test() { return 1; }";

        var (_, language) = PayloadFormatter.Format(body, "application/javascript");

        Assert.Equal("javascript", language);
    }

    [Fact]
    public void Format_DetectsJson_FromBodyContent_WhenNoContentType()
    {
        var body = "{\"key\":\"value\"}";

        var (_, language) = PayloadFormatter.Format(body, null);

        Assert.Equal("json", language);
    }

    [Fact]
    public void Format_DetectsXml_FromBodyContent_WhenNoContentType()
    {
        var body = "<root>test</root>";

        var (_, language) = PayloadFormatter.Format(body, null);

        Assert.Equal("xml", language);
    }

    [Fact]
    public void Format_ReturnsPlaintext_ForUnknownContentType()
    {
        var body = "some plain text";

        var (content, language) = PayloadFormatter.Format(body, "text/plain");

        Assert.Equal("plaintext", language);
        Assert.Equal(body, content);
    }

    [Fact]
    public void Format_HandlesInvalidJson_Gracefully()
    {
        var body = "{invalid json}";

        var (content, language) = PayloadFormatter.Format(body, "application/json");

        Assert.Equal("json", language);
        Assert.Equal(body, content);
    }

    [Fact]
    public void Format_HandlesInvalidXml_Gracefully()
    {
        var body = "<unclosed>tag";

        var (content, language) = PayloadFormatter.Format(body, "application/xml");

        Assert.Equal("xml", language);
        Assert.Equal(body, content);
    }

    [Fact]
    public void Format_HandlesContentTypeWithCharset()
    {
        var body = "{\"key\":\"value\"}";

        var (_, language) = PayloadFormatter.Format(body, "application/json; charset=utf-8");

        Assert.Equal("json", language);
    }

    [Fact]
    public void DetectLanguage_DetectsArrayJson()
    {
        var language = PayloadFormatter.DetectLanguage(null, "[1,2,3]");

        Assert.Equal("json", language);
    }
}
