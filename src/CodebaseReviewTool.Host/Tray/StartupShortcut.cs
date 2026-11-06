using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace CodebaseReviewTool.Host.Tray;

/// <summary>
/// The "run at login" setting: a shortcut to this exe in the user's Startup folder. No registry and no admin rights;
/// the shortcut's existence is the setting.
/// </summary>
public sealed class StartupShortcut
{
    private const string ShortcutFileName = "Codebase Review Tool.lnk";

    /// <summary>Where the shortcut is (or would be) created.</summary>
    public string ShortcutPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutFileName);

    /// <summary>True when the shortcut exists, i.e. the tool starts at login.</summary>
    public bool IsEnabled => File.Exists(ShortcutPath);

    /// <summary>Creates the shortcut pointing at the running exe (replacing an existing one) or deletes it.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            Create();
        }
        else if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }

    private void Create()
    {
        var (targetPath, arguments) = ResolveTarget();

        // The Windows Script Host shell object writes .lnk files; late binding avoids an interop assembly. It is an
        // apartment-threaded COM object, so it runs on a short-lived STA thread rather than the caller's pool thread.
        RunOnStaThread(() =>
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
            var shell = Activator.CreateInstance(shellType)!;
            try
            {
                var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [ShortcutPath])!;
                try
                {
                    SetProperty(link, "TargetPath", targetPath);
                    SetProperty(link, "Arguments", arguments);
                    SetProperty(link, "WorkingDirectory", Path.GetDirectoryName(targetPath) ?? string.Empty);
                    SetProperty(link, "Description", "Starts Codebase Review Tool in the system tray");
                    link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(link);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        });
    }

    /// <summary>
    /// The exe to start at login. Normally the running process; when it was started through <c>dotnet</c> the app
    /// host exe next to the assembly is used instead, falling back to <c>dotnet &lt;dll&gt;</c>.
    /// </summary>
    private static (string TargetPath, string Arguments) ResolveTarget()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The path of the running executable is unknown.");

        if (!string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, string.Empty);
        }

        var assemblyPath = typeof(StartupShortcut).Assembly.Location;
        var appHost = Path.ChangeExtension(assemblyPath, ".exe");
        return File.Exists(appHost) ? (appHost, string.Empty) : (processPath, $"\"{assemblyPath}\"");
    }

    private static void SetProperty(object target, string name, string value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            ExceptionDispatchInfo.Throw(error is TargetInvocationException { InnerException: { } inner } ? inner : error);
        }
    }
}
