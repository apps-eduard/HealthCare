namespace HealthCare.Web.Design;

/// <summary>
/// Visual required marker for form labels (validation stays in app code).
/// </summary>
public static class FieldLabel
{
    public static string Mark(string label, bool required = true) =>
        required && !string.IsNullOrWhiteSpace(label)
            ? $"{label.TrimEnd()} *"
            : label;
}
