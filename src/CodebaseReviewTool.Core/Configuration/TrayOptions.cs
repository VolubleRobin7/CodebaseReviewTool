namespace CodebaseReviewTool.Core.Configuration;

public sealed class TrayOptions
{
    public const string SectionName = "Tray";

    public bool Enabled { get; set; } = true;

    public bool OpenBrowserOnStart { get; set; }
}
