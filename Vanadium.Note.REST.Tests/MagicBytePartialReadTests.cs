using Vanadium.Note.REST.Controllers;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Guards against the partial-read bug in upload magic-byte validation (issue #300).
/// A single <see cref="Stream.ReadAsync(Memory{byte}, System.Threading.CancellationToken)"/>
/// is not guaranteed to fill the buffer — on a chunk boundary it may return fewer bytes than
/// requested, which would wrongly reject valid files (WebP needs all 12 bytes). The validation
/// must fill the buffer (or reach EOF) before inspecting the magic bytes.
/// </summary>
public class MagicBytePartialReadTests
{
    // A stream that hands out at most <paramref name="chunkSize"/> bytes per ReadAsync call,
    // reproducing the chunk boundary that made valid uploads fail intermittently.
    private sealed class ChunkedStream(byte[] data, int chunkSize) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = data.Length - _position;
            if (remaining <= 0) return 0;
            var toCopy = Math.Min(Math.Min(count, chunkSize), remaining);
            Array.Copy(data, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte[] WebpBytes()
    {
        var buffer = new byte[12];
        buffer[0] = 0x52; buffer[1] = 0x49; buffer[2] = 0x46; buffer[3] = 0x46; // RIFF
        buffer[4] = 0x24; buffer[5] = 0x00; buffer[6] = 0x00; buffer[7] = 0x00; // size (arbitrary)
        buffer[8] = 0x57; buffer[9] = 0x45; buffer[10] = 0x42; buffer[11] = 0x50; // WEBP
        return buffer;
    }

    [Theory]
    [InlineData(1)]  // one byte per read — the worst chunk boundary
    [InlineData(4)]  // first read returns only 4 bytes (< the 12 WebP needs)
    [InlineData(11)] // one byte short of the full signature
    [InlineData(12)] // whole buffer in a single read (the already-working case)
    public async Task Files_Webp_IsAccepted_RegardlessOfChunkBoundary(int chunkSize)
    {
        await using var stream = new ChunkedStream(WebpBytes(), chunkSize);
        Assert.True(await FilesController.HasValidMagicBytesAsync(stream, "image/webp"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(11)]
    [InlineData(12)]
    public async Task Images_Webp_IsDetected_RegardlessOfChunkBoundary(int chunkSize)
    {
        await using var stream = new ChunkedStream(WebpBytes(), chunkSize);
        Assert.Equal("image/webp", await ImagesController.DetectImageTypeAsync(stream));
    }

    [Fact]
    public async Task Files_TruncatedStream_IsRejected()
    {
        // A stream shorter than the 12-byte WebP signature must still be rejected (EOF reached).
        await using var stream = new ChunkedStream(WebpBytes()[..8], chunkSize: 1);
        Assert.False(await FilesController.HasValidMagicBytesAsync(stream, "image/webp"));
    }

    [Fact]
    public async Task Images_TruncatedStream_IsRejected()
    {
        await using var stream = new ChunkedStream(WebpBytes()[..8], chunkSize: 1);
        Assert.Null(await ImagesController.DetectImageTypeAsync(stream));
    }
}
