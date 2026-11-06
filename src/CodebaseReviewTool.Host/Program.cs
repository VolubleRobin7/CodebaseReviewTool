using CodebaseReviewTool.Core;
using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Host.Api;
using CodebaseReviewTool.Host.Components;
using CodebaseReviewTool.Host.Tray;

namespace CodebaseReviewTool.Host;

/// <summary>
/// The entry point: reads configuration, makes sure only one instance runs, builds the application and hands it to
/// <see cref="AppRunner"/> in tray mode (the default) or console mode (<c>Tray:Enabled=false</c> or
/// <c>--console</c>, which is how it runs during development).
/// </summary>
public sealed class Program
{
    private const string ConsoleSwitch = "--console";

    private Program()
    {
    }

    [STAThread]
    public static int Main(string[] args)
    {
        var useConsole = args.Any(a => string.Equals(a, ConsoleSwitch, StringComparison.OrdinalIgnoreCase));
        var hostArgs = args.Where(a => !string.Equals(a, ConsoleSwitch, StringComparison.OrdinalIgnoreCase)).ToArray();

        // The content root is the exe's folder, not the current directory, so appsettings.json is found however the
        // exe is started (from the Startup folder, Explorer or another directory).
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = hostArgs,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Machine-specific configuration (PAT, organisation, worktree root, repositories) lives in user-secrets.
        // CreateBuilder only adds them in Development and the tray-hosted exe runs as Production, so add them in every
        // environment. Environment variables and the command line are re-added after them so they keep precedence.
        builder.Configuration.AddUserSecrets<Program>(optional: true);
        builder.Configuration.AddEnvironmentVariables();
        builder.Configuration.AddCommandLine(hostArgs);

        var trayOptions = builder.Configuration.GetSection(TrayOptions.SectionName).Get<TrayOptions>() ?? new TrayOptions();
        var useTray = trayOptions.Enabled && !useConsole;
        if (!useTray)
        {
            ConsoleWindow.Ensure();
        }

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            // Another instance owns the port and the database; show its UI instead of starting a second host.
            var url = AppUrl.FromConfiguration(builder.Configuration);
            Console.WriteLine($"Codebase Review Tool is already running; opening {url}");
            Browser.TryOpen(url);
            return 0;
        }

        var app = Build(builder);
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (trayOptions.OpenBrowserOnStart)
            {
                Browser.TryOpen(AppUrl.FromServer(app));
            }
        });

        return useTray ? AppRunner.RunWithTray(app) : AppRunner.RunWithConsole(app);
    }

    private static WebApplication Build(WebApplicationBuilder builder)
    {
        // Serve static web assets (blazor.web.js, scoped CSS) from the build output in every environment, not only
        // Development, so the exe works when started from bin as Production. No effect on published output.
        builder.WebHost.UseStaticWebAssets();

        builder.Services.AddReviewCore(builder.Configuration);
        builder.Services.AddSingleton<StartupShortcut>();

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
        app.MapReportApi();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
