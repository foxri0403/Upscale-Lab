using System.Text.Json;

namespace UpscaleLab.Application.Projects;

// Reuse the existing JSON metadata column for web editing sessions.
// Projects created by older/mobile clients keep their existing saved behavior.
public static class ProjectDraftState
{
    public const string Pending = "{\"isDraft\":true}";
    public const string Discarded = "{\"isDraft\":true,\"discarded\":true}";
    public static bool IsDraft(string? json) => Flag(json, "isDraft");
    public static bool IsDiscarded(string? json) => IsDraft(json) && Flag(json, "discarded");
    private static bool Flag(string? json, string name)
    {
        if (json is null) return false;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}
