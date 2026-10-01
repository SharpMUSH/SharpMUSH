using SharpMUSH.Client.Widgets;
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
		// (e.g. the seeded "character-header") rendering even if the startup catalog snapshot was empty.
		// Every renderer gates it through ApplicationWidgetGate, which resolves the app first to apply its
		// minimum role and leaves out one it cannot find.
		return new UnresolvedApplicationWidget(name);
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
}
