namespace BlazorDX.Interop;

/// <summary>One file hashed in the browser before upload.</summary>
/// <param name="Name">The browser-reported file name (untrusted — see <see cref="DroppedFile"/>).</param>
/// <param name="Size">The browser-reported size in bytes.</param>
/// <param name="Hash">Lowercase hex hash computed client-side with the requested algorithm.</param>
public readonly record struct FileHashResult(string Name, long Size, string Hash);

/// <summary>
/// Browser-side file-integrity hashing (Web Crypto) for the upload "verify before write" path: it
/// computes each selected file's hash in the browser so the receiving side can re-hash the bytes it
/// received and confirm they match. Files are addressed by the <c>&lt;input&gt;</c> element id, so no
/// <c>File</c> crosses the boundary (matching <see cref="IFileDndInterop"/>). Outside WebAssembly it
/// is a no-op (<see cref="NullFileHashInterop"/>); the authoritative re-hash on receipt still runs.
/// </summary>
public interface IFileHashInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>
    /// Hashes every file currently selected on the input with id <paramref name="elementId"/> using
    /// <paramref name="algorithm"/> (a <c>HashAlgorithmName.Name</c> such as "SHA256"; unknown names
    /// fall back to SHA-256). Returns one <see cref="FileHashResult"/> per file, or an empty list if
    /// the element is missing or holds no files.
    /// </summary>
    ValueTask<IReadOnlyList<FileHashResult>> HashInputFilesAsync(string elementId, string algorithm);
}
