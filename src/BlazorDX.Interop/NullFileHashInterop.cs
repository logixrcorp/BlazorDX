namespace BlazorDX.Interop;

/// <summary>
/// Server-side / non-browser implementation of <see cref="IFileHashInterop"/>. There is no Web
/// Crypto and no selected-file element outside WebAssembly, so it reports no client hashes. The
/// authoritative side is the receiver's own re-hash of the bytes it received (the
/// <c>FileHasher</c> streaming verifier), which runs regardless.
/// </summary>
public sealed class NullFileHashInterop : IFileHashInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<FileHashResult>> HashInputFilesAsync(string elementId, string algorithm) =>
        ValueTask.FromResult<IReadOnlyList<FileHashResult>>([]);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
