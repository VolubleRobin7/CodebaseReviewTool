using System.ComponentModel;
using System.Diagnostics;

namespace CodebaseReviewTool.Host.Tray;

/// <summary>Opens addresses in the user's default browser.</summary>
internal static class Browser
{
    /// <summary>Opens <paramref name="url"/>; returns false (and does not throw) when no browser could be started.</summary>
    public static bool TryOpen(string url)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
