namespace CodebaseReviewTool.Core.Reporting;

/// <summary>
/// Wraps the diagram step's HTML excerpt in the document a sandboxed iframe shows through <c>srcdoc</c>. The frame is
/// sandboxed with scripts allowed but not same-origin, so the excerpt may load its own scripts and draw, but cannot
/// reach the page around it. Because the parent cannot read the frame's size, the document (the embedded
/// <c>Templates/diagram-document.html</c>) reports its own height with <c>postMessage</c>
/// (<c>{ type: "diagram-height", height }</c>) after it loads and whenever it resizes; the parent sizes the frame from
/// that and keeps a fixed height until the first message arrives.
/// </summary>
public static class DiagramDocument
{
    private const string ExcerptMarker = "{{EXCERPT}}";

    /// <summary>Returns the complete document for the iframe's <c>srcdoc</c>, with the excerpt as its body.</summary>
    public static string Wrap(string excerpt) =>
        ReportTemplates.DiagramDocument.Replace(ExcerptMarker, excerpt, StringComparison.Ordinal);
}
