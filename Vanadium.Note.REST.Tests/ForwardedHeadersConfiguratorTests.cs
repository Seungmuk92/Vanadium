using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Vanadium.Note.REST.Security;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression coverage for the proxy trust configuration (issue #197). The security-critical
/// invariant is that when NO trust list is configured, the framework's loopback-only defaults
/// are preserved — never cleared — so ASP.NET Core does not fall back to trusting every
/// <c>X-Forwarded-For</c> hop (which would let a direct client forge its IP and defeat the
/// login rate limiter). The full middleware pipeline needs an integration host and is verified
/// manually; this exercises the pure configuration seam.
/// </summary>
public class ForwardedHeadersConfiguratorTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Configure_NoTrustListConfigured_PreservesFrameworkDefaults()
    {
        var options = new ForwardedHeadersOptions();
        var proxiesBefore = options.KnownProxies.Count;
        var networksBefore = options.KnownIPNetworks.Count;
        // Guard the guard: the framework must ship non-empty defaults, otherwise "preserve" is meaningless.
        Assert.True(proxiesBefore > 0 || networksBefore > 0,
            "Framework defaults should be non-empty before configuration.");

        ForwardedHeadersConfigurator.Configure(options, BuildConfig(new()));

        // Untouched — an untrusted origin's XFF can never partition the rate limiter.
        Assert.Equal(proxiesBefore, options.KnownProxies.Count);
        Assert.Equal(networksBefore, options.KnownIPNetworks.Count);
    }

    [Fact]
    public void Configure_KnownNetworksConfigured_ReplacesDefaults()
    {
        var options = new ForwardedHeadersOptions();
        var config = BuildConfig(new() { ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8" });

        ForwardedHeadersConfigurator.Configure(options, config);

        Assert.Empty(options.KnownProxies);
        var net = Assert.Single(options.KnownIPNetworks);
        Assert.Equal(System.Net.IPNetwork.Parse("10.0.0.0/8"), net);
    }

    [Fact]
    public void Configure_KnownProxiesConfigured_ReplacesDefaults()
    {
        var options = new ForwardedHeadersOptions();
        var config = BuildConfig(new() { ["ForwardedHeaders:KnownProxies:0"] = "203.0.113.5" });

        ForwardedHeadersConfigurator.Configure(options, config);

        Assert.Empty(options.KnownIPNetworks);
        var proxy = Assert.Single(options.KnownProxies);
        Assert.Equal(IPAddress.Parse("203.0.113.5"), proxy);
    }

    [Fact]
    public void Configure_ProxiesAndNetworksConfigured_BothApplied()
    {
        var options = new ForwardedHeadersOptions();
        var config = BuildConfig(new()
        {
            ["ForwardedHeaders:KnownProxies:0"] = "203.0.113.5",
            ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12",
        });

        ForwardedHeadersConfigurator.Configure(options, config);

        Assert.Equal(IPAddress.Parse("203.0.113.5"), Assert.Single(options.KnownProxies));
        Assert.Equal(System.Net.IPNetwork.Parse("172.16.0.0/12"), Assert.Single(options.KnownIPNetworks));
    }

    [Fact]
    public void Configure_InvalidProxy_Throws()
    {
        var options = new ForwardedHeadersOptions();
        var config = BuildConfig(new() { ["ForwardedHeaders:KnownProxies:0"] = "not-an-ip" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersConfigurator.Configure(options, config));
        Assert.Contains("KnownProxies", ex.Message);
    }

    [Fact]
    public void Configure_InvalidNetwork_Throws()
    {
        var options = new ForwardedHeadersOptions();
        var config = BuildConfig(new() { ["ForwardedHeaders:KnownNetworks:0"] = "not-a-cidr" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersConfigurator.Configure(options, config));
        Assert.Contains("KnownNetworks", ex.Message);
    }
}
