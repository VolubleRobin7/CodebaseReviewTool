using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Prompts;

namespace CodebaseReviewTool.Host.Components.Shared;

/// <summary>
/// The Configuration page's editable copy of one <see cref="FlowStep"/>, remembering the saved values so the page can
/// tell what changed and the shipped default so the card can be reset.
/// </summary>
public sealed class FlowStepDraft
{
    private string _promptText;
    private string? _model;

    public FlowStepDraft(FlowStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        Step = step.Step;
        Name = step.Name;
        _promptText = Normalise(step.PromptText);
        _model = step.Model;
        TimeoutMinutes = step.TimeoutMinutes;
        MarkSaved();
    }

    public int Step { get; }

    /// <summary>The step's name, as its card and its report section are headed.</summary>
    public string Name { get; set; }

    /// <summary>The prompt as edited; line endings are kept as LF, as stored.</summary>
    public string PromptText
    {
        get => _promptText;
        set => _promptText = Normalise(value);
    }

    /// <summary>The model override; an empty choice means none.</summary>
    public string? Model
    {
        get => _model;
        set => _model = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public int? TimeoutMinutes { get; set; }

    public string SavedName { get; private set; } = string.Empty;

    public string SavedPromptText { get; private set; } = string.Empty;

    public string? SavedModel { get; private set; }

    public int? SavedTimeoutMinutes { get; private set; }

    /// <summary>The name and prompt the tool ships for this step, or null when it ships none.</summary>
    public DefaultPrompt? Default => DefaultPrompts.For(Step);

    /// <summary>True when the card differs from what is saved.</summary>
    public bool IsEdited =>
        Name != SavedName || PromptText != SavedPromptText || Model != SavedModel || TimeoutMinutes != SavedTimeoutMinutes;

    /// <summary>True when the card holds the shipped name and prompt with no model or timeout override.</summary>
    public bool IsDefault =>
        Default is { } shipped && Name == shipped.Name && PromptText == shipped.Text && Model is null && TimeoutMinutes is null;

    /// <summary>True when the name is present and the timeout is empty or a whole number of minutes in the accepted range.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && TimeoutMinutes is null or (>= 1 and <= 1440);

    /// <summary>Restores the shipped name and prompt and clears the model and timeout overrides.</summary>
    public void ResetToDefault()
    {
        if (Default is { } shipped)
        {
            Name = shipped.Name;
            PromptText = shipped.Text;
            Model = null;
            TimeoutMinutes = null;
        }
    }

    /// <summary>Copies the edited values onto <paramref name="step"/>.</summary>
    public void ApplyTo(FlowStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        step.Name = Name.Trim();
        step.PromptText = PromptText;
        step.Model = Model;
        step.TimeoutMinutes = TimeoutMinutes;
    }

    /// <summary>Records the current values as the saved ones.</summary>
    public void MarkSaved()
    {
        SavedName = Name;
        SavedPromptText = PromptText;
        SavedModel = Model;
        SavedTimeoutMinutes = TimeoutMinutes;
    }

    private static string Normalise(string? text) => (text ?? string.Empty).ReplaceLineEndings("\n");
}
