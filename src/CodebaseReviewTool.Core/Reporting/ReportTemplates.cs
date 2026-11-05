using System.Reflection;

namespace CodebaseReviewTool.Core.Reporting;

/// <summary>The files under <c>Reporting/Templates</c>, embedded in the assembly and read once on first use.</summary>
internal static class ReportTemplates
{
    private const string ResourcePrefix = "CodebaseReviewTool.Core.Reporting.Templates.";

    private static readonly Lazy<string> _diagramDocument = new(() => Read("diagram-document.html"));
    private static readonly Lazy<string> _reportStyles = new(() => Read("report.css"));
    private static readonly Lazy<string> _reportScript = new(() => Read("report.js"));

    /// <summary>The document a diagram excerpt is shown in, with <c>{{EXCERPT}}</c> where the excerpt goes.</summary>
    public static string DiagramDocument => _diagramDocument.Value;

    /// <summary>The downloaded report's stylesheet.</summary>
    public static string ReportStyles => _reportStyles.Value;

    /// <summary>The downloaded report's script: severity filtering and diagram frame sizing.</summary>
    public static string ReportScript => _reportScript.Value;

    private static string Read(string fileName)
    {
        var assembly = typeof(ReportTemplates).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"Embedded template {fileName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd();
    }
}
