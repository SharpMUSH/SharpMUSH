using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the schema-driven widget (Area 21). Its per-instance config carries
/// <c>{ schemaUrl, dataUrl }</c> pointing at softcode HTTP-handler routes; the widget renders the
/// returned Portal Schema Document with the shared schema renderers. Registered Dynamic Applications
/// of kind Widget surface through this single descriptor.
/// </summary>
public sealed class SchemaWidgetDescriptor : IPortalWidget
{
	public string Name => "SchemaWidget";
	public string DisplayName => "Schema Application";
	public string Description => "Renders a softcode-defined form or view from a schema endpoint.";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(SchemaWidget);
	public Type? ConfigType => null;
}
