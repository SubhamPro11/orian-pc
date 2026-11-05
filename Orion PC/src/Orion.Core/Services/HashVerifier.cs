using System.Security.Cryptography;

namespace Orion.Core.Services;

public interface IHashVerifier
{
    string ComputeSha256(byte[] data);
    string ComputeSha256(Stream stream);
    Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default);
    bool VerifySha256(byte[] data, string expectedHexHash);
    bool VerifySha256(Stream stream, string expectedHexHash);
    Task<bool> VerifySha256Async(Stream stream, string expectedHexHash, CancellationToken cancellationToken = default);
}

public class HashVerifier : IHashVerifier
{
    public string ComputeSha256(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public string ComputeSha256(Stream stream)
    {
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public bool VerifySha256(byte[] data, string expectedHexHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHexHash)) return false;
        var computed = ComputeSha256(data);
        return string.Equals(computed, expectedHexHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public bool VerifySha256(Stream stream, string expectedHexHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHexHash)) return false;
        var computed = ComputeSha256(stream);
        return string.Equals(computed, expectedHexHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> VerifySha256Async(Stream stream, string expectedHexHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedHexHash)) return false;
        var computed = await ComputeSha256Async(stream, cancellationToken);
        return string.Equals(computed, expectedHexHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

