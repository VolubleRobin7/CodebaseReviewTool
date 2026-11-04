namespace CodebaseReviewTool.Host.Components.Shared;

/// <summary>Formats a past UTC time as "just now", "N minutes ago", "N hours ago" or "N days ago".</summary>
public static class RelativeTime
{
    public static string Ago(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => Plural((int)elapsed.TotalMinutes, "minute") + " ago",
            { TotalDays: < 1 } => Plural((int)elapsed.TotalHours, "hour") + " ago",
            _ => Plural((int)elapsed.TotalDays, "day") + " ago",
        };
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
