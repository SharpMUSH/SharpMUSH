namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Config schema for the Character Directory widget. Categories named in
/// <paramref name="HiddenCategories"/> (matched case-insensitively against FN`CHARCAT labels)
/// are filtered out of the listing entirely — e.g. ["Guest"] hides guest characters.
/// </summary>
public record CharacterDirectoryConfig(IReadOnlyList<string> HiddenCategories);
