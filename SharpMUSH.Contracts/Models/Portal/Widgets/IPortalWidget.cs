using System.Diagnostics.CodeAnalysis;

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

	/// <summary>
	/// Label for the admin palette, as a resource key rather than text: built-in descriptors return a
	/// <c>SharedResource</c> key (<c>"LayWidgetCharacterDirectory"</c>) which the palette resolves
	/// through its localizer. Descriptors built from a Dynamic Application instead carry the admin's
	/// own display name, which no lookup can translate; the palette detects the miss
	/// (<c>ResourceNotFound</c>) and renders that text unchanged.
	/// </summary>
	string DisplayName { get; }

	/// <summary>Preferred size hint used by the layout engine.</summary>
	WidgetSize DefaultSize { get; }

	/// <summary>Zones where this widget may be placed.</summary>
	WidgetZone[] AllowedZones { get; }

	/// <summary>The <see cref="Type"/> of the Razor component that renders this widget.</summary>
	Type ComponentType { get; }

	/// <summary>
	/// Optional deserialized config type.
	/// <c>null</c> means the widget has no configuration.
	/// <para>
	/// Its properties should carry <see cref="WidgetConfigKeyAttribute"/>: the layout editor builds
	/// the admin-facing key reference from them through <see cref="WidgetConfigSchema"/>, so a
	/// config model without them is configurable but undocumented.
	/// </para>
	/// </summary>
	[DynamicallyAccessedMembers(
		DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
	Type? ConfigType { get; }
}
