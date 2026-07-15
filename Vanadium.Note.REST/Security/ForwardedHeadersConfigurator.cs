using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Vanadium.Note.REST.Security;

/// <summary>
/// Configures which upstream proxies/networks are trusted to set <c>X-Forwarded-For</c>, so the
/// login rate limiter partitions on a client IP that cannot be forged by a client reaching the
/// app port directly (issue #197).
///
/// ASP.NET Core trusts EVERY forwarded hop when both
/// <see cref="ForwardedHeadersOptions.KnownProxies"/> and
/// <see cref="ForwardedHeadersOptions.KnownIPNetworks"/> are empty. This helper therefore KEEPS the
/// framework's loopback-only defaults when nothing is configured, and only replaces them when an
/// explicit trust list is supplied via <c>ForwardedHeaders:KnownProxies</c> (individual IPs) and
/// <c>ForwardedHeaders:KnownNetworks</c> (CIDR). Clearing the defaults unconditionally — the previous
/// behavior — is exactly what let a direct client forge its IP and mint a fresh rate-limit bucket.
/// </summary>
public static class ForwardedHeadersConfigurator
{
    /// <summary>Configuration section holding the trusted proxy/network lists.</summary>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Applies the trusted-proxy configuration to <paramref name="options"/>. When no proxies or
    /// networks are configured the framework defaults are left intact (so untrusted origins are never
    /// honored); when a trust list is configured it replaces those defaults.
    /// </summary>
    /// <param name="options">The options instance to populate (already carrying framework defaults).</param>
    /// <param name="configuration">Application configuration to read the trust list from.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a configured proxy is not a valid IP address, or a configured network is not valid CIDR.
    /// </exception>
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        var knownProxies = configuration
            .GetSection($"{SectionName}:KnownProxies").Get<string[]>() ?? [];
        var knownNetworks = configuration
            .GetSection($"{SectionName}:KnownNetworks").Get<string[]>() ?? [];

        // No explicit trust list — keep the framework's loopback-only defaults so untrusted
        // origins can never partition the rate limiter. Clearing here would re-open the hole.
        if (knownProxies.Length == 0 && knownNetworks.Length == 0)
            return;

        // Explicit trust list configured — replace the loopback defaults with it.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in knownProxies)
        {
            if (IPAddress.TryParse(proxy, out var ip))
                options.KnownProxies.Add(ip);
            else
                throw new InvalidOperationException(
                    $"{SectionName}:KnownProxies contains an invalid IP address: '{proxy}'.");
        }

        foreach (var network in knownNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out var net))
                options.KnownIPNetworks.Add(net);
            else
                throw new InvalidOperationException(
                    $"{SectionName}:KnownNetworks contains an invalid CIDR network: '{network}'.");
        }
    }
}
