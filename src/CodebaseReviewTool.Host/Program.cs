using CodebaseReviewTool.Core;
using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Host.Api;
using CodebaseReviewTool.Host.Components;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Machine-specific configuration (PAT, organisation, worktree root, repositories) lives in user-secrets.
// CreateBuilder only adds them in Development and the tray-hosted exe runs as Production, so add them in every
// environment. Environment variables and the command line are re-added after them so they keep precedence.
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

// Serve static web assets (blazor.web.js, scoped CSS) from the build output in every environment, not only
// Development, so the exe works when started from bin as Production. No effect on published output.
builder.WebHost.UseStaticWebAssets();

builder.Services.AddReviewCore(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

try
{
    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (StartupFailures(ex) is { Count: > 0 } failures)
{
    // Configuration problems: print a plain list instead of a stack trace and exit non-zero.
    Console.Error.WriteLine();
    Console.Error.WriteLine("Codebase Review Tool cannot start because its configuration is invalid:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }

    Console.Error.WriteLine("Fix the values in appsettings.json or user-secrets and start again.");
    return 1;
}

static List<string> StartupFailures(Exception exception) => exception switch
{
    OptionsValidationException options => [.. options.Failures],
    StartupValidationException startup => [.. startup.Failures],
    AggregateException aggregate => [.. aggregate.InnerExceptions.SelectMany(StartupFailures)],
    _ => [],
};
