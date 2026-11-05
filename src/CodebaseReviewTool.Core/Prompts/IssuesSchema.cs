namespace CodebaseReviewTool.Core.Prompts;

/// <summary>
/// The fixed JSON structure the last flow step returns its issues in. The tool owns it: the instruction is appended to
/// the step's prompt and the schema is also handed to the agent so it can enforce the shape.
/// </summary>
public static class IssuesSchema
{
    /// <summary>The JSON schema of the issues object.</summary>
    public const string Schema =
        """
        {
          "type": "object",
          "required": ["issues"],
          "properties": {
            "issues": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["severity", "category", "file", "title", "description"],
                "properties": {
                  "severity":    { "type": "string", "enum": ["Critical", "High", "Medium", "Low", "Info"] },
                  "category":    { "type": "string" },
                  "file":        { "type": "string" },
                  "line":        { "type": ["integer", "null"] },
                  "title":       { "type": "string" },
                  "description": { "type": "string" },
                  "suggestion":  { "type": ["string", "null"] }
                }
              }
            }
          }
        }
        """;

    /// <summary>Appended to the last step's rendered prompt: a blank line, the instruction, then the schema in a fenced block.</summary>
    public static string Instruction { get; } =
        "\n\n"
        + "Return only a JSON object that matches the schema below, with no prose before or after it. Use one entry in "
        + "\"issues\" per problem; severity is one of Critical, High, Medium, Low or Info, and line is null when the issue "
        + "does not apply to a single line. Return {\"issues\": []} when there is nothing to report.\n\n"
        + "```json\n"
        + Schema.ReplaceLineEndings("\n")
        + "\n```";
}
