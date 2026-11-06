using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace CodebaseReviewTool.Host.Tray;

/// <summary>Works out the browser address of the UI.</summary>
internal static class AppUrl
{
    private const string FallbackUrl = "http://localhost:5210";

    /// <summary>The address Kestrel actually listens on; call after the host has started.</summary>
    public static string FromServer(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? addresses.FirstOrDefault();
        return address is null ? FromConfiguration(app.Configuration) : Normalize(address);
    }

    /// <summary>The configured address, for when no server has been started (a second instance).</summary>
    public static string FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration["Kestrel:Endpoints:Http:Url"]
            ?? configuration["urls"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? FallbackUrl;
        return Normalize(configured);
    }

    /// <summary>The address of one review's report page.</summary>
    public static string Report(string baseUrl, int reviewId) => $"{baseUrl}/reviews/{reviewId}";

    // A wildcard listen address is not something a browser can open; the UI is loopback-only anyway.
    private static string Normalize(string address)
    {
        var wildcard = address.Replace("://+", "://localhost").Replace("://*", "://localhost");
        if (!Uri.TryCreate(wildcard, UriKind.Absolute, out var uri))
        {
            return FallbackUrl;
        }

        var builder = new UriBuilder(uri);
        if (builder.Host is "0.0.0.0" or "[::]" or "::")
        {
            builder.Host = "localhost";
        }

        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }
}
