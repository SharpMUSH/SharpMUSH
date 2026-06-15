using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Wiki Body widget — a character's free-form wiki biography, rendered for the
/// character supplied by the profile page context. Part of the default <c>"profile"</c> layout.
/// </summary>
public sealed class WikiBodyWidgetDescriptor : IPortalWidget
{
	public string Name => "WikiBody";
	public string DisplayName => "Wiki Body";
	public string Description => "A character's free-form wiki biography.";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent];
	public Type ComponentType => typeof(WikiBodyWidget);
	public Type? ConfigType => null;
}
