using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Guards the length caps added in issue #135 so string columns that were previously
/// mapped to unbounded Postgres <c>text</c> stay bounded. If a <c>[MaxLength]</c> is
/// dropped or its value changes, these assertions fail and remind the reviewer that a
/// matching EF Core migration is required.
/// </summary>
public class StringColumnMaxLengthTests
{
    [Theory]
    [InlineData(typeof(FileAttachment), nameof(FileAttachment.OriginalName), 255)]
    [InlineData(typeof(FileAttachment), nameof(FileAttachment.ContentType), 100)]
    [InlineData(typeof(ApiToken), nameof(ApiToken.TokenHash), 64)]
    public void StringColumn_HasExpectedMaxLength(Type entity, string propertyName, int expectedLength)
    {
        var property = entity.GetProperty(propertyName);
        Assert.NotNull(property);

        var maxLength = property!.GetCustomAttribute<MaxLengthAttribute>();
        Assert.NotNull(maxLength);
        Assert.Equal(expectedLength, maxLength!.Length);
    }
}
