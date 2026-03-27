using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class JwtDecoderTests
{
    // A real JWT structure: {"alg":"HS256","typ":"JWT"}.{"sub":"1234567890","name":"John Doe","iat":1516239022}.signature
    private const string ValidJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    [Fact]
    public void IsJwt_ReturnsTrueForValidJwt()
    {
        Assert.True(JwtDecoder.IsJwt(ValidJwt));
    }

    [Fact]
    public void IsJwt_ReturnsFalseForNonJwt()
    {
        Assert.False(JwtDecoder.IsJwt("not-a-jwt"));
    }

    [Fact]
    public void IsJwt_ReturnsFalseForTwoParts()
    {
        Assert.False(JwtDecoder.IsJwt("part1.part2"));
    }

    [Fact]
    public void IsJwt_ReturnsFalseForEmptyString()
    {
        Assert.False(JwtDecoder.IsJwt(""));
    }

    [Fact]
    public void IsJwt_ReturnsFalseForInvalidBase64()
    {
        Assert.False(JwtDecoder.IsJwt("!!!.@@@.###"));
    }

    [Fact]
    public void ExtractBearerToken_ExtractsToken()
    {
        var token = JwtDecoder.ExtractBearerToken($"Bearer {ValidJwt}");
        Assert.Equal(ValidJwt, token);
    }

    [Fact]
    public void ExtractBearerToken_CaseInsensitive()
    {
        var token = JwtDecoder.ExtractBearerToken($"bearer {ValidJwt}");
        Assert.Equal(ValidJwt, token);
    }

    [Fact]
    public void ExtractBearerToken_ReturnsNullForNonBearer()
    {
        Assert.Null(JwtDecoder.ExtractBearerToken("Basic dXNlcjpwYXNz"));
    }

    [Fact]
    public void Decode_ReturnsHeaderAndPayload()
    {
        var result = JwtDecoder.Decode(ValidJwt);

        Assert.NotNull(result);
        Assert.Contains("HS256", result.Header);
        Assert.Contains("JWT", result.Header);
        Assert.Contains("John Doe", result.Payload);
        Assert.Contains("1234567890", result.Payload);
    }

    [Fact]
    public void Decode_PrettyPrintsJson()
    {
        var result = JwtDecoder.Decode(ValidJwt);

        Assert.NotNull(result);
        Assert.Contains("\n", result.Header);
        Assert.Contains("\n", result.Payload);
    }

    [Fact]
    public void Decode_IncludesSignature()
    {
        var result = JwtDecoder.Decode(ValidJwt);

        Assert.NotNull(result);
        Assert.Equal("SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c", result.Signature);
    }

    [Fact]
    public void Decode_ReturnsNullForInvalidToken()
    {
        Assert.Null(JwtDecoder.Decode("not-a-jwt"));
    }

    [Fact]
    public void Decode_ReturnsNullForTwoParts()
    {
        Assert.Null(JwtDecoder.Decode("part1.part2"));
    }
}
