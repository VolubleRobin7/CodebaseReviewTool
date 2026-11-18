namespace CodebaseReviewTool.Application.Configuration;

/// <summary>
/// The "run at login" setting, shown on the Configuration page. Host implements it with a shortcut in the user's
/// Startup folder, since only Host knows which exe is running.
/// </summary>
public interface IStartupShortcut
{
    /// <summary>Where the shortcut is (or would be) created.</summary>
    string ShortcutPath { get; }

    /// <summary>True when the tool starts at login.</summary>
    bool IsEnabled { get; }

    /// <summary>Turns starting at login on or off.</summary>
    void SetEnabled(bool enabled);
}
