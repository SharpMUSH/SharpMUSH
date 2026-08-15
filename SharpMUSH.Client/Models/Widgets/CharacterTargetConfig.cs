namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Config schema shared by the character-scoped widgets (Wiki Body, Character Gallery): which
/// character they render for when no <see cref="ProfilePageContext"/> is cascading.
/// </summary>
/// <param name="Character">
/// Character name to render. Ignored on the profile page, where the route-supplied context wins.
/// Omit it and the widget renders nothing outside a profile.
/// </param>
public record CharacterTargetConfig(string? Character = null);
