using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Quick Links widget.
/// </summary>
public sealed class QuickLinksWidgetDescriptor : IPortalWidget
{
	public string Name => "QuickLinks";
	public string DisplayName => "Quick Links";
	public string Description => "Shows a configurable list of links.";
	public WidgetSize DefaultSize => WidgetSize.Small;
	public WidgetZone[] AllowedZones =>
	[
		WidgetZone.TopBar,
		WidgetZone.LeftSidebar,
		WidgetZone.RightSidebar,
		WidgetZone.Footer
	];
	public Type ComponentType => typeof(QuickLinksWidget);
	public Type? ConfigType => typeof(QuickLinksConfig);
}
