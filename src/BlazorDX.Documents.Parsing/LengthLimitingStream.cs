using System;
using System.IO;

namespace BlazorDX.Documents;

/// <summary>
/// A forward-only, read-only pass-through that throws once more than <c>limit</c> bytes have been
/// read from the inner stream. This is the real defense against a "lying" zip bomb: a crafted entry
/// can declare a small uncompressed length in the ZIP header but inflate to far more, and
/// <see cref="System.IO.Compression.ZipArchive"/> does not verify the inflated size against the
/// header — so checking <c>ZipArchiveEntry.Length</c> up front is necessary but not sufficient.
/// Wrapping the entry stream bounds the bytes that can ever be materialized (e.g. an image part read
/// with <c>CopyTo</c>, which has no character cap of its own).
/// </summary>
internal sealed class LengthLimitingStream : Stream
{
    private readonly Stream inner;
    private readonly long limit;
    private readonly string partName;
    private long bytesRead;

    public LengthLimitingStream(Stream inner, long limit, string partName)
    {
        this.inner = inner;
        this.limit = limit;
        this.partName = partName;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Track(inner.Read(buffer));

    public override int ReadByte()
    {
        int b = inner.ReadByte();
        if (b >= 0)
        {
            Track(1);
        }

        return b;
    }

    private int Track(int n)
    {
        bytesRead += n;
        if (bytesRead > limit)
        {
            throw new InvalidDataException(
                $"part '{partName}' exceeds the {limit}-byte limit when decompressed and was rejected.");
        }

        return n;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
