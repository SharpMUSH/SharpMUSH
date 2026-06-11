using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Character Header widget — renders a character's structured, schema-driven
/// profile fields (grouped into sections) with per-viewer visibility decided by the http_handler.
/// </summary>
public sealed class CharacterHeaderWidgetDescriptor : IPortalWidget
{
	public string Name => "CharacterHeader";
	public string DisplayName => "Character Header";
	public string Description => "Structured, schema-driven profile fields for a character.";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent];
	public Type ComponentType => typeof(CharacterHeaderWidget);
	public Type? ConfigType => null;
}
