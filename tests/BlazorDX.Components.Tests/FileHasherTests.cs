using System.Security.Cryptography;
using System.Text;
using BlazorDX.Primitives.Files;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Streaming hash + constant-time verify for the upload integrity path.</summary>
public sealed class FileHasherTests
{
    // NIST/FIPS known-answer vectors.
    private const string Sha256Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string Sha256Empty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string Sha1Abc = "a9993e364706816aba3e25717850c26c9cd0d89d";

    private static MemoryStream Stream(string text) => new(Encoding.ASCII.GetBytes(text));

    [Fact]
    public async Task Computes_the_known_sha256_vector()
    {
        Assert.Equal(Sha256Abc, await FileHasher.ComputeHexAsync(Stream("abc")));
    }

    [Fact]
    public async Task Computes_the_empty_input_vector()
    {
        Assert.Equal(Sha256Empty, await FileHasher.ComputeHexAsync(Stream(string.Empty)));
    }

    [Fact]
    public async Task Default_algorithm_is_sha256()
    {
        // No algorithm argument resolves to SHA-256 (not, say, SHA-1).
        Assert.Equal(Sha256Abc, await FileHasher.ComputeHexAsync(Stream("abc"), default));
    }

    [Fact]
    public async Task Verify_accepts_the_matching_hash_case_insensitively()
    {
        FileHashVerification result = await FileHasher.VerifyAsync(Stream("abc"), Sha256Abc.ToUpperInvariant());

        Assert.True(result.Verified);
        Assert.Equal(Sha256Abc, result.ActualHashHex);
    }

    [Fact]
    public async Task Verify_rejects_a_wrong_hash_but_still_reports_the_actual()
    {
        FileHashVerification result = await FileHasher.VerifyAsync(Stream("abc"), Sha256Empty);

        Assert.False(result.Verified);
        Assert.Equal(Sha256Abc, result.ActualHashHex);   // the real hash is still surfaced
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-hex-zz")]
    [InlineData("ba7816bf")]   // right prefix, wrong length
    public async Task Verify_fails_closed_on_a_missing_or_malformed_expected_hash(string? expected)
    {
        FileHashVerification result = await FileHasher.VerifyAsync(Stream("abc"), expected);

        Assert.False(result.Verified);
    }

    [Fact]
    public async Task Hashes_a_multi_buffer_stream_identically_to_one_shot()
    {
        // 200 KB spans several read chunks; the streamed hash must equal the one-shot hash.
        byte[] data = new byte[200_000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 31 % 256);
        }

        string streamed = await FileHasher.ComputeHexAsync(new MemoryStream(data), bufferSize: 4096);
        string oneShot = Convert.ToHexStringLower(SHA256.HashData(data));

        Assert.Equal(oneShot, streamed);
    }

    [Fact]
    public async Task Honours_an_alternate_algorithm()
    {
        Assert.Equal(Sha1Abc, await FileHasher.ComputeHexAsync(Stream("abc"), HashAlgorithmName.SHA1));
    }
}
