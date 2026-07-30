using Vanadium.Note.Web.Models;
using Xunit;

namespace Vanadium.Note.Web.Tests.Models;

/// <summary>
/// Guards the client-side <c>pf</c> wire form (issue #343): <see cref="PropertyFilter.ToQueryValue"/>
/// and <see cref="PropertyFilter.Parse"/> must round-trip, including values that contain the ':' /
/// ',' separators (which must be percent-encoded so the server's split stays unambiguous).
/// </summary>
public sealed class PropertyFilterTests
{
    [Fact]
    public void ValueOp_RoundTrips()
    {
        var id = Guid.NewGuid();
        var filter = new PropertyFilter { DefinitionId = id, Op = PropertyFilterOp.Eq, Value = "hello" };

        var parsed = PropertyFilter.Parse(filter.ToQueryValue());

        Assert.NotNull(parsed);
        Assert.Equal(id, parsed!.DefinitionId);
        Assert.Equal(PropertyFilterOp.Eq, parsed.Op);
        Assert.Equal("hello", parsed.Value);
    }

    [Fact]
    public void ValueWithSeparators_IsEncoded_AndRoundTrips()
    {
        var id = Guid.NewGuid();
        var filter = new PropertyFilter { DefinitionId = id, Op = PropertyFilterOp.Eq, Value = "a:b,c" };

        var wire = filter.ToQueryValue();
        // The raw separators must not leak into the wire form's value segment.
        Assert.DoesNotContain("a:b", wire);
        Assert.DoesNotContain("b,c", wire);

        var parsed = PropertyFilter.Parse(wire);
        Assert.Equal("a:b,c", parsed!.Value);
    }

    [Fact]
    public void EmptyOp_HasNoValueSegment()
    {
        var id = Guid.NewGuid();
        var filter = new PropertyFilter { DefinitionId = id, Op = PropertyFilterOp.NotEmpty };

        var wire = filter.ToQueryValue();
        Assert.Equal($"{id}:notempty", wire);

        var parsed = PropertyFilter.Parse(wire);
        Assert.Equal(PropertyFilterOp.NotEmpty, parsed!.Op);
        Assert.Null(parsed.Value);
    }

    [Theory]
    [InlineData("not-a-guid:eq:x")]
    [InlineData("onlyonepart")]
    public void Parse_Malformed_ReturnsNull(string raw) => Assert.Null(PropertyFilter.Parse(raw));

    [Fact]
    public void IsEmpty_TracksClearedValues()
    {
        Assert.True(new NotePropertyValue { Type = PropertyType.Text }.IsEmpty);
        Assert.True(new NotePropertyValue { Type = PropertyType.Checkbox, BoolValue = false }.IsEmpty);
        Assert.False(new NotePropertyValue { Type = PropertyType.Number, NumberValue = 0 }.IsEmpty);
        Assert.False(new NotePropertyValue { Type = PropertyType.Checkbox, BoolValue = true }.IsEmpty);
    }
}
