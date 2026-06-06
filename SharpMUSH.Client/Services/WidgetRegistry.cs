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
		return _widgets.TryGetValue(name, out var w) ? w : null;
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
