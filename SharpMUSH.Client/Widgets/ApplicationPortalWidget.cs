using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// A layout-palette widget backed by a Widget-kind Dynamic Application (Area 21). Its machine name is
/// the application slug, its allowed zones come from the application, and it renders through the shared
/// <see cref="SchemaWidget"/>, which resolves the schema/data routes from the application catalog by
/// slug (and fills page-context tokens like <c>{objid}</c>).
/// </summary>
public sealed class ApplicationPortalWidget(PortalApplication app) : IPortalWidget
{
	public string Name => app.Slug;
	public string DisplayName => app.DisplayName;
	public string Description => $"Application · {app.SchemaUrl}";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => app.ZoneEnums.ToArray();
	public Type ComponentType => typeof(SchemaWidget);
	public Type? ConfigType => null;
}
