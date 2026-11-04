namespace CodebaseReviewTool.Host.Api;

public static class HealthEndpoints
{
    /// <summary>
    /// Maps <c>GET /api/health</c>, which returns <c>200 text/plain "OK"</c> while the host is running.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthApi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/health", () => Results.Text("OK", "text/plain")).WithName("GetHealth");

        return app;
    }
}
