using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Character Directory widget — lists every character with search and links
/// to their profiles. Powers the <c>/characters</c> page and is placeable in any content zone.
/// </summary>
public sealed class CharacterDirectoryWidgetDescriptor : IPortalWidget
{
	public string Name => "CharacterDirectory";
	public string DisplayName => "Character Directory";
	public string Description => "Searchable listing of all characters.";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones =>
	[
		WidgetZone.MainContent,
		WidgetZone.LeftSidebar,
		WidgetZone.RightSidebar
	];
	public Type ComponentType => typeof(CharacterDirectoryWidget);
	public Type? ConfigType => null;
}
