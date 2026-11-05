namespace CodebaseReviewTool.Host.Components.Shared;

/// <summary>
/// The title and description of each flow step card on the Configuration page. A step's output is interpreted by its
/// position in the flow, so the labels are too.
/// </summary>
public static class FlowStepLabels
{
    private static readonly (string Title, string Description)[] _labels =
    [
        ("Summary", "Produces the overview at the top of the report."),
        ("Architecture diagram", "Produces the diagram of the components the change touches."),
        ("Issues", "Produces the table of issues, each with a severity and a suggested fix."),
    ];

    /// <summary>The card title for the step at <paramref name="index"/> (0-based position in the flow).</summary>
    public static string Title(int index) => index < _labels.Length ? _labels[index].Title : $"Step {index + 1}";

    /// <summary>The card description for the step at <paramref name="index"/>; empty for steps beyond the shipped three.</summary>
    public static string Description(int index) => index < _labels.Length ? _labels[index].Description : string.Empty;
}
