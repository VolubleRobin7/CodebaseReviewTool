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

        // Configuration is appsettings.json beside the exe, environment variables and the command line. In the
        // Development environment (Visual Studio, dotnet run) user-secrets are layered in as well, so a development
        // copy can run beside an installed one with its own values.
        var trayOptions = builder.Configuration.GetSection(TrayOptions.SectionName).Get<TrayOptions>() ?? new TrayOptions();
        var useTray = trayOptions.Enabled && !useConsole;
        if (!useTray)
        {
            ConsoleWindow.Ensure();
        }

        var url = AppUrl.FromConfiguration(builder.Configuration);
        using var instance = SingleInstance.TryAcquire(url);
        if (instance is null)
        {
            // Another instance owns this port and its database; show its UI instead of starting a second host.
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
