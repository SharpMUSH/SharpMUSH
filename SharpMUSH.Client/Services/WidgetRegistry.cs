using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Services;

/// <inheritdoc/>
public sealed class WidgetRegistry : IWidgetRegistry
{
	private readonly Dictionary<string, IPortalWidget> _widgets = new(StringComparer.Ordinal);

	/// <inheritdoc/>
	public void Register(IPortalWidget widget)
	{
		ArgumentNullException.ThrowIfNull(widget);
		_widgets[widget.Name] = widget;
	}

	/// <inheritdoc/>
	public IPortalWidget? GetWidget(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (_widgets.TryGetValue(name, out var w))
		{
			return w;
		}

		// Unknown name → treat it as an application slug and render through SchemaWidget, which resolves
		// the app's routes by slug (from the catalog or a lazy fetch). This keeps an app-backed placement
		// (e.g. the seeded "character-header") rendering even if the startup catalog snapshot was empty;
		// if the app truly isn't registered server-side, SchemaWidget shows "schema unavailable".
		return new SchemaApplicationWidget(name);
	}

	/// <inheritdoc/>
	public IReadOnlyList<IPortalWidget> GetAllWidgets()
		=> _widgets.Values.ToList().AsReadOnly();

	/// <inheritdoc/>
	public IReadOnlyList<IPortalWidget> GetWidgetsForZone(WidgetZone zone)
		=> _widgets.Values
			.Where(w => w.AllowedZones.Contains(zone))
			.ToList()
			.AsReadOnly();

	/// <summary>
	/// Fallback descriptor for an application-backed placement whose slug isn't a registered widget:
	/// renders through <see cref="SchemaWidget"/> (which resolves the application by slug). Never listed
	/// in the palette — only returned by <see cref="GetWidget"/> so rendering doesn't depend on the
	/// startup application snapshot.
	/// </summary>
	private sealed class SchemaApplicationWidget(string name) : IPortalWidget
	{
		public string Name => name;
		public string DisplayName => name;
		public string Description => "Application widget.";
		public WidgetSize DefaultSize => WidgetSize.Large;
		public WidgetZone[] AllowedZones => Enum.GetValues<WidgetZone>();
		public Type ComponentType => typeof(SchemaWidget);
		public Type? ConfigType => null;
	}
}
