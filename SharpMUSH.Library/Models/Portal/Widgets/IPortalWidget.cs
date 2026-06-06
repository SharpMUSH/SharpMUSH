namespace SharpMUSH.Library.Models.Portal.Widgets;

/// <summary>
/// Metadata contract for a portal widget.
/// Each widget is a Blazor component that also implements this interface on a
/// companion descriptor class so the registry can discover it at startup.
/// </summary>
public interface IPortalWidget
{
	/// <summary>Unique machine name used in <see cref="WidgetPlacement.WidgetName"/>.</summary>
	string Name { get; }

	/// <summary>Human-readable display name shown in the admin palette.</summary>
	string DisplayName { get; }

	/// <summary>Short description of what the widget shows.</summary>
	string Description { get; }

	/// <summary>Preferred size hint used by the layout engine.</summary>
	WidgetSize DefaultSize { get; }

	/// <summary>Zones where this widget may be placed.</summary>
	WidgetZone[] AllowedZones { get; }

	/// <summary>The <see cref="Type"/> of the Razor component that renders this widget.</summary>
	Type ComponentType { get; }

	/// <summary>
	/// Optional deserialized config type.
	/// <c>null</c> means the widget has no configuration.
	/// </summary>
	Type? ConfigType { get; }
}
