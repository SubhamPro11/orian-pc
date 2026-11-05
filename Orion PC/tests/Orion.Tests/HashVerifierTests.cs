using System.Text;
using Orion.Core.Services;
using Xunit;

namespace Orion.Tests;

public class HashVerifierTests
{
    private readonly HashVerifier _verifier = new();

    [Fact]
    public void ComputeSha256_ComputesKnownHash()
    {
        // SHA-256 for "hello world" is b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9
        var bytes = Encoding.UTF8.GetBytes("hello world");
        var hash = _verifier.ComputeSha256(bytes);

        Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", hash);
    }

    [Fact]
    public void VerifySha256_VerifiesCaseInsensitively()
    {
        var bytes = Encoding.UTF8.GetBytes("hello world");
        const string upperHash = "B94D27B9934D3E08A52E52D7DA7DABFAC484EFE37A5380EE9088F7ACE2EFCDE9";

        Assert.True(_verifier.VerifySha256(bytes, upperHash));
        Assert.False(_verifier.VerifySha256(bytes, "0000000000000000000000000000000000000000000000000000000000000000"));
    }

    [Fact]
    public async Task VerifySha256Async_VerifiesStream()
    {
        var bytes = Encoding.UTF8.GetBytes("orion store pc verification test");
        using var stream = new MemoryStream(bytes);

        var expectedHash = _verifier.ComputeSha256(bytes);
        var isValid = await _verifier.VerifySha256Async(stream, expectedHash);

        Assert.True(isValid);
    }
}

