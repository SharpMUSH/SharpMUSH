using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Config schema for the Character Gallery widget: which character it renders for when no
/// <see cref="ProfilePageContext"/> is cascading.
/// </summary>
/// <param name="Character">
/// Character name to render. Ignored on the profile page, where the route-supplied context wins.
/// Omit it and the widget renders nothing outside a profile.
/// </param>
public record CharacterTargetConfig(
	[property: WidgetConfigKey("LayCfgCharacterTarget")]
	string? Character = null);
