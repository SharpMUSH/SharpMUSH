using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Wiki Body widget — renders one wiki page inline. Addressed by
/// <see cref="WikiBodyConfig"/>, or, with no config, by the character supplied by the profile page
/// context, which is how it serves as the biography in the default <c>"profile"</c> layout.
/// </summary>
public sealed class WikiBodyWidgetDescriptor : IPortalWidget
{
	public string Name => "WikiBody";
	public string DisplayName => "LayWidgetWikiBody";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent];
	public Type ComponentType => typeof(WikiBodyWidget);
	public Type? ConfigType => typeof(WikiBodyConfig);
}
