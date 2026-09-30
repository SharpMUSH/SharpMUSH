using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// What <see cref="Services.IWidgetRegistry.GetWidget"/> returns for a placement whose name is not a
/// registered widget: an application slug whose application is not known yet, because the startup catalog
/// is still loading or failed. It renders through <see cref="SchemaWidget"/>, which resolves the
/// application by slug. Never listed in the palette.
/// </summary>
/// <remarks>
/// It carries no minimum role, since the application behind it is unknown. A renderer has to resolve the
/// application and check its role before placing it; see <c>ZoneRenderer</c>.
/// </remarks>
public sealed class UnresolvedApplicationWidget(string name) : IPortalWidget
{
	public string Name => name;
	public string DisplayName => name;
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => Enum.GetValues<WidgetZone>();
	public Type ComponentType => typeof(SchemaWidget);
	public Type? ConfigType => null;
}
