using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Wiki Index widget — the wiki landing page (hero + client-side search + a
/// category grid of pages). Drives the default <c>"wiki-index"</c> layout scope and is placeable in
/// any main-content area.
/// </summary>
public sealed class WikiIndexWidgetDescriptor : IPortalWidget
{
	public string Name => "WikiIndex";
	public string DisplayName => "Wiki Index";
	public string Description => "Searchable category grid of all wiki pages.";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent];
	public Type ComponentType => typeof(WikiIndexWidget);
	public Type? ConfigType => null;
}
