using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Registry of all known <see cref="IPortalWidget"/> implementations.
/// Populated at startup from DI; widgets register themselves.
/// </summary>
public interface IWidgetRegistry
{
	/// <summary>Registers a widget by its <see cref="IPortalWidget.Name"/>.</summary>
	void Register(IPortalWidget widget);

	/// <summary>Returns the widget with the given name, or <c>null</c> if not registered.</summary>
	IPortalWidget? GetWidget(string name);

	/// <summary>Returns all registered widgets.</summary>
	IReadOnlyList<IPortalWidget> GetAllWidgets();

	/// <summary>Returns only the widgets whose <see cref="IPortalWidget.AllowedZones"/> contains <paramref name="zone"/>.</summary>
	IReadOnlyList<IPortalWidget> GetWidgetsForZone(WidgetZone zone);
}
