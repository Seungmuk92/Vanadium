using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Controllers;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Guards the caching policy on the authenticated image download endpoint
/// (<see cref="ImagesController.Get"/>). Because the endpoint requires
/// authentication, its response must be marked <c>Cache-Control: private</c>
/// so that shared (intermediary) proxies never cache a private image, while
/// long-lived client-side caching is preserved. The <c>[ResponseCache]</c>
/// attribute is framework metadata applied by the MVC pipeline, so it is
/// asserted here via reflection (a full HTTP integration host is out of scope
/// for this project).
/// </summary>
public class ImagesControllerCacheTests
{
    private static ResponseCacheAttribute GetAttribute()
    {
        var method = typeof(ImagesController).GetMethod(nameof(ImagesController.Get))!;
        return method.GetCustomAttribute<ResponseCacheAttribute>()!;
    }

    [Fact]
    public void Get_UsesClientLocation_SoResponseIsPrivate()
    {
        var attribute = GetAttribute();

        Assert.NotNull(attribute);
        Assert.Equal(ResponseCacheLocation.Client, attribute.Location);
    }

    [Fact]
    public void Get_RetainsLongLivedClientCaching()
    {
        var attribute = GetAttribute();

        Assert.Equal(31536000, attribute.Duration);
    }
}
