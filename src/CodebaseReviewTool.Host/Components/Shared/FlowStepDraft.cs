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
        _promptText = Normalise(step.PromptText);
        _model = step.Model;
        TimeoutMinutes = step.TimeoutMinutes;
        MarkSaved();
    }

    public int Step { get; }

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

    public string SavedPromptText { get; private set; } = string.Empty;

    public string? SavedModel { get; private set; }

    public int? SavedTimeoutMinutes { get; private set; }

    /// <summary>The prompt the tool ships for this step, or null when it ships none.</summary>
    public string? DefaultPromptText => DefaultPrompts.For(Step);

    /// <summary>True when the card differs from what is saved.</summary>
    public bool IsEdited =>
        PromptText != SavedPromptText || Model != SavedModel || TimeoutMinutes != SavedTimeoutMinutes;

    /// <summary>True when the card holds the shipped prompt with no model or timeout override.</summary>
    public bool IsDefault => DefaultPromptText is { } prompt && PromptText == prompt && Model is null && TimeoutMinutes is null;

    /// <summary>True when the timeout is empty or a whole number of minutes in the accepted range.</summary>
    public bool IsValid => TimeoutMinutes is null or (>= 1 and <= 1440);

    /// <summary>Restores the shipped prompt and clears the model and timeout overrides.</summary>
    public void ResetToDefault()
    {
        if (DefaultPromptText is { } prompt)
        {
            PromptText = prompt;
            Model = null;
            TimeoutMinutes = null;
        }
    }

    /// <summary>Copies the edited values onto <paramref name="step"/>.</summary>
    public void ApplyTo(FlowStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        step.PromptText = PromptText;
        step.Model = Model;
        step.TimeoutMinutes = TimeoutMinutes;
    }

    /// <summary>Records the current values as the saved ones.</summary>
    public void MarkSaved()
    {
        SavedPromptText = PromptText;
        SavedModel = Model;
        SavedTimeoutMinutes = TimeoutMinutes;
    }

    private static string Normalise(string? text) => (text ?? string.Empty).ReplaceLineEndings("\n");
}
