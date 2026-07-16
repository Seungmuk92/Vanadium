using Vanadium.Note.REST.Controllers;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Guards the WebP magic-byte validation in <see cref="FilesController"/> (issue #217).
/// A WebP file is a RIFF container whose bytes at offset 8 spell <c>WEBP</c>. Checking
/// only the <c>RIFF</c> prefix also accepts AVI/WAV (same container), so the offset-8
/// signature must be verified — matching ImagesController.DetectImageTypeAsync.
/// </summary>
public class WebpMagicByteTests
{
    // "RIFF" (offset 0) + 4 size bytes + "WEBP" (offset 8).
    private static byte[] Riff(params byte[] signatureAt8)
    {
        var buffer = new byte[12];
        buffer[0] = 0x52; buffer[1] = 0x49; buffer[2] = 0x46; buffer[3] = 0x46; // RIFF
        buffer[4] = 0x24; buffer[5] = 0x00; buffer[6] = 0x00; buffer[7] = 0x00; // size (arbitrary)
        signatureAt8.CopyTo(buffer, 8);
        return buffer;
    }

    [Fact]
    public void RealWebp_IsAccepted()
    {
        var buffer = Riff(0x57, 0x45, 0x42, 0x50); // "WEBP"
        Assert.True(FilesController.HasValidMagicBytes(buffer, buffer.Length, "image/webp"));
    }

    [Fact]
    public void RiffAvi_IsRejectedAsWebp()
    {
        // AVI is a RIFF container ("AVI " at offset 8) — must NOT pass as WebP.
        var buffer = Riff(0x41, 0x56, 0x49, 0x20); // "AVI "
        Assert.False(FilesController.HasValidMagicBytes(buffer, buffer.Length, "image/webp"));
    }

    [Fact]
    public void RiffWav_IsRejectedAsWebp()
    {
        // WAV is a RIFF container ("WAVE" at offset 8) — must NOT pass as WebP.
        var buffer = Riff(0x57, 0x41, 0x56, 0x45); // "WAVE"
        Assert.False(FilesController.HasValidMagicBytes(buffer, buffer.Length, "image/webp"));
    }

    [Fact]
    public void RiffOnly_TooShortForOffset8_IsRejected()
    {
        // Only the RIFF prefix present (8 bytes read): offset-8 signature can't be
        // verified, so it must be rejected rather than trusted.
        var buffer = Riff(0x00, 0x00, 0x00, 0x00);
        Assert.False(FilesController.HasValidMagicBytes(buffer, 8, "image/webp"));
    }

    [Fact]
    public void OtherMimeTypes_AreUnaffected()
    {
        // The fix must not change validation for other MIME types (issue #217 범위 밖).
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
        Assert.True(FilesController.HasValidMagicBytes(png, png.Length, "image/png"));

        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        Assert.True(FilesController.HasValidMagicBytes(pdf, pdf.Length, "application/pdf"));
    }
}
