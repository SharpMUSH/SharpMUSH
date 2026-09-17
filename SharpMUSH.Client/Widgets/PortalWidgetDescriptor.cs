using System.Diagnostics.CodeAnalysis;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// A built-in widget's entry in the catalogue. <see cref="IPortalWidget"/> is six values and no
/// behaviour, so a record says it once instead of a sealed class per widget saying it thirteen times.
/// </summary>
/// <param name="Name">
/// The machine name a <c>WidgetPlacement</c> stores. Changing one orphans every placement that names
/// it — unknown names fall through to the application-slug resolution, which will not find it either.
/// </param>
/// <param name="DisplayName">
/// A <c>SharedResource</c> key, not text. The palette resolves it through its localizer; see
/// <see cref="IPortalWidget.DisplayName"/> for why a Dynamic Application's descriptor differs.
/// </param>
/// <param name="ConfigType">
/// The config model, or <see langword="null"/> for a widget that takes none. Every property on it
/// should carry <c>[WidgetConfigKey]</c> — <c>BuiltInWidgetCatalogueTests</c> fails a config model
/// that documents no keys, and the localization tests fail a key with no resx description.
/// </param>
public sealed record PortalWidgetDescriptor(
	string Name,
	string DisplayName,
	WidgetSize DefaultSize,
	WidgetZone[] AllowedZones,
	Type ComponentType,
	[property: DynamicallyAccessedMembers(
		DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
	Type? ConfigType = null) : IPortalWidget;
