using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Character Gallery widget — image gallery for a character's profile.
/// </summary>
public sealed class CharacterGalleryWidgetDescriptor : IPortalWidget
{
	public string Name => "CharacterGallery";
	public string DisplayName => "Character Gallery";
	public string Description => "Image gallery for a character's profile.";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(CharacterGalleryWidget);
	public Type? ConfigType => null;
}
