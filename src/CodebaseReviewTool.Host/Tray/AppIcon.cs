using System.Drawing;

namespace CodebaseReviewTool.Host.Tray;

/// <summary>
/// The application icon, read from the copy embedded in the assembly. The same file is the exe's icon and the
/// browser tab's favicon, so the tray matches both.
/// </summary>
internal static class AppIcon
{
    private const string ResourceName = "CodebaseReviewTool.Host.app.ico";

    /// <summary>Loads the icon at the frame closest to <paramref name="size"/> pixels. The caller disposes it.</summary>
    public static Icon Load(int size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded icon {ResourceName} is missing.");
        return new Icon(stream, size, size);
    }
}
