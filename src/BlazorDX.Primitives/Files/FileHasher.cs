using System.Buffers;
using System.Security.Cryptography;

namespace BlazorDX.Primitives.Files;

/// <summary>The outcome of verifying a stream against an expected hash.</summary>
/// <param name="Verified">True when the stream's hash matched the expected value.</param>
/// <param name="ActualHashHex">The lowercase hex hash actually computed from the stream.</param>
public readonly record struct FileHashVerification(bool Verified, string ActualHashHex);

/// <summary>
/// Streaming file-integrity hashing for the upload "verify before write" path: the receiving side
/// re-hashes the bytes it actually got and confirms they match the hash the browser computed before
/// the upload, so corruption in transit is caught before the file is committed.
/// </summary>
/// <remarks>
/// Built on <see cref="IncrementalHash"/>, so a file of any size is hashed in fixed-size chunks and
/// is never fully buffered in memory. The default algorithm is <see cref="HashAlgorithmName.SHA256"/>:
/// SHA-1 is deliberately not the default because it is a broken primitive, though any
/// <see cref="HashAlgorithmName"/> may be passed to match an external system. Comparison is
/// constant-time (<see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>)
/// so a verifier never leaks, via timing, how many leading bytes of a hash matched.
/// </remarks>
public static class FileHasher
{
    private const int DefaultBufferSize = 81920;

    private static HashAlgorithmName Resolve(HashAlgorithmName algorithm) =>
        algorithm == default ? HashAlgorithmName.SHA256 : algorithm;

    /// <summary>
    /// Computes the lowercase hex hash of <paramref name="source"/>, reading it in fixed-size chunks
    /// (never buffering the whole stream). Defaults to SHA-256.
    /// </summary>
    public static async Task<string> ComputeHexAsync(
        Stream source,
        HashAlgorithmName algorithm = default,
        int bufferSize = DefaultBufferSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using IncrementalHash hash = IncrementalHash.CreateHash(Resolve(algorithm));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1024, bufferSize));
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Streams <paramref name="source"/>, hashes it, and reports whether it matches
    /// <paramref name="expectedHashHex"/> (case-insensitive, constant-time), along with the hash
    /// actually computed. A null/empty/malformed expected value verifies as false rather than throwing.
    /// </summary>
    public static async Task<FileHashVerification> VerifyAsync(
        Stream source,
        string? expectedHashHex,
        HashAlgorithmName algorithm = default,
        int bufferSize = DefaultBufferSize,
        CancellationToken cancellationToken = default)
    {
        string actual = await ComputeHexAsync(source, algorithm, bufferSize, cancellationToken).ConfigureAwait(false);
        return new FileHashVerification(HexEqualsConstantTime(actual, expectedHashHex), actual);
    }

    // Constant-time compare of two hex strings. Both are parsed to bytes first so differing
    // lengths or non-hex input fail closed without leaking position information via timing.
    private static bool HexEqualsConstantTime(string actualHex, string? expectedHex)
    {
        if (string.IsNullOrEmpty(expectedHex))
        {
            return false;
        }

        byte[]? actual = TryParseHex(actualHex);
        byte[]? expected = TryParseHex(expectedHex);
        return actual is not null && expected is not null && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[]? TryParseHex(string hex)
    {
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
