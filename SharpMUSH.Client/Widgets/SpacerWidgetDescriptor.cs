using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Spacer widget — empty reserved space for finer layout control. Width is the
/// placement's column span; height is set per-instance via <see cref="SpacerConfig"/>. Placeable in
/// every zone.
/// </summary>
public sealed class SpacerWidgetDescriptor : IPortalWidget
{
	public string Name => "Spacer";
	public string DisplayName => "Spacer";
	public string Description => "Empty space to push widgets apart or pad a zone (width = column span, height configurable).";
	public WidgetSize DefaultSize => WidgetSize.Small;

	// Vertical/content zones only — a height-based spacer is meaningless in the horizontal top bar.
	public WidgetZone[] AllowedZones =>
	[
		WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar, WidgetZone.Footer
	];

	public Type ComponentType => typeof(SpacerWidget);
	public Type? ConfigType => typeof(SpacerConfig);
}
