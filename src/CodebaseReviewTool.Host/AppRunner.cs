using System.Windows.Forms;
using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Host.Tray;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Host;

/// <summary>
/// Runs the built application in one of its two modes. In console mode the calling thread blocks on the web host.
/// In tray mode the web host starts in the background and the calling thread, which must be STA, runs the tray's
/// message loop; when that loop ends the host is stopped and waited for. Both modes report configuration problems as
/// a plain list and return the process exit code.
/// </summary>
internal static class AppRunner
{
    private const string AppName = "Codebase Review Tool";

    public static int RunWithConsole(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        try
        {
            app.Run();
            return 0;
        }
        catch (Exception ex) when (StartupFailures(ex) is { Count: > 0 } failures)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(DescribeFailures(failures));
            return 1;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    public static int RunWithTray(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            // Start off the STA thread so no continuation is tied to it; StartAsync returns once Kestrel listens.
            Task.Run(() => app.StartAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // There is no console to print to, so configuration problems and other start failures go in a message box.
            var failures = StartupFailures(ex);
            var message = failures.Count > 0
                ? DescribeFailures(failures)
                : $"{AppName} could not start.{Environment.NewLine}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}";
            MessageBox.Show(message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            try
            {
                Stop(app);
            }
            catch (Exception)
            {
                // The host never fully started; the message above already says why.
            }

            return 1;
        }

        using (var tray = new TrayApplicationContext(app.Services, AppUrl.FromServer(app)))
        {
            Application.Run(tray);
        }

        Stop(app);
        return 0;
    }

    /// <summary>
    /// Stops the web host and waits for it: the hosted services are cancelled, so the review queue stops taking work
    /// and a running agent or git process is killed with its process tree. The pooled SQLite connections are then
    /// closed so everything written is on disk before the process ends.
    /// </summary>
    private static void Stop(WebApplication app)
    {
        Task.Run(async () =>
        {
            try
            {
                await app.StopAsync();
            }
            finally
            {
                await app.DisposeAsync();
            }
        }).GetAwaiter().GetResult();

        SqliteConnection.ClearAllPools();
    }

    private static string DescribeFailures(List<string> failures) =>
        $"{AppName} cannot start because its configuration is invalid:{Environment.NewLine}"
        + string.Concat(failures.Select(f => $"  - {f}{Environment.NewLine}"))
        + "Fix the values in appsettings.json or user-secrets and start again.";

    private static List<string> StartupFailures(Exception exception) => exception switch
    {
        OptionsValidationException options => [.. options.Failures],
        StartupValidationException startup => [.. startup.Failures],
        AggregateException aggregate => [.. aggregate.InnerExceptions.SelectMany(StartupFailures)],
        _ => [],
    };
}
