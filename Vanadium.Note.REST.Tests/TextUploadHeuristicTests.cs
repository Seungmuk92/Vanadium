using System.Text;
using Vanadium.Note.REST.Controllers;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Guards the text/plain·text/markdown upload heuristic (issue #216). These types
/// have no reliable magic bytes, so <see cref="FilesController.IsLikelyText"/>
/// sniffs a sampled prefix and rejects obvious binary payloads (NUL bytes or a
/// high ratio of non-whitespace control characters) while letting real text and
/// markdown through unchanged.
/// </summary>
public class TextUploadHeuristicTests
{
    [Fact]
    public void PlainText_IsAccepted()
    {
        var bytes = Encoding.UTF8.GetBytes("Hello, world!\r\nThis is a note.\tTabbed.\n");
        Assert.True(FilesController.IsLikelyText(bytes));
    }

    [Fact]
    public void Markdown_IsAccepted()
    {
        var bytes = Encoding.UTF8.GetBytes("# Title\n\n- item one\n- item two\n\n```csharp\nvar x = 1;\n```\n");
        Assert.True(FilesController.IsLikelyText(bytes));
    }

    [Fact]
    public void MultiByteUtf8Text_IsAccepted()
    {
        // High bytes (0x80-0xFF) from multi-byte UTF-8 must not be treated as binary.
        var bytes = Encoding.UTF8.GetBytes("한국어 텍스트 émojis é ü ñ — dashes.");
        Assert.True(FilesController.IsLikelyText(bytes));
    }

    [Fact]
    public void Utf8Bom_IsAccepted()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("plain text")).ToArray();
        Assert.True(FilesController.IsLikelyText(bytes));
    }

    [Fact]
    public void BytesWithNul_AreRejected()
    {
        // A single NUL byte is a strong binary signal (e.g. a PNG mislabeled as text/plain).
        var bytes = new byte[] { 0x48, 0x65, 0x00, 0x6C, 0x6C, 0x6F };
        Assert.False(FilesController.IsLikelyText(bytes));
    }

    [Fact]
    public void BinaryBlob_IsRejected()
    {
        // A run of control bytes exceeds the allowed control-character ratio.
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x1B, 0x1C, 0x1D };
        Assert.False(FilesController.IsLikelyText(bytes));
    }

    [Fact]
    public void EmptySample_IsRejected()
    {
        Assert.False(FilesController.IsLikelyText(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void MostlyTextWithFewControlChars_IsAccepted()
    {
        // A couple of stray control chars in otherwise-clean text stays under the threshold.
        var text = new string('a', 200);
        var bytes = Encoding.UTF8.GetBytes(text).Append((byte)0x01).ToArray();
        Assert.True(FilesController.IsLikelyText(bytes));
    }
}
