using System.Runtime.InteropServices;

namespace CodebaseReviewTool.Host.Tray;

/// <summary>
/// The exe is a Windows (not console) application, so it starts without a console. When it runs without the tray
/// this gives it one: the console it was started from when there is one (<c>dotnet run</c>, a terminal), otherwise a
/// new window.
/// </summary>
internal static class ConsoleWindow
{
    private const int AttachParentProcess = -1;
    private const int StandardOutputHandle = -11;
    private const uint FileTypeDisk = 0x0001;
    private const uint FileTypePipe = 0x0003;

    /// <summary>Makes console output visible. Call before anything writes to <see cref="Console"/>.</summary>
    public static void Ensure()
    {
        // Output redirected to a file or pipe already reaches whoever started the process.
        if (GetFileType(GetStdHandle(StandardOutputHandle)) is FileTypeDisk or FileTypePipe)
        {
            return;
        }

        if (!AttachConsole(AttachParentProcess))
        {
            AllocConsole();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr handle);
}
