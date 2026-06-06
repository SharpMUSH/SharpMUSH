using System.Text.Json;

namespace SharpMUSH.Library.Models.Portal.Widgets;

/// <summary>
/// A single widget instance placed in a zone with an optional JSON config blob.
/// </summary>
/// <param name="WidgetName">Matches <see cref="IPortalWidget.Name"/>.</param>
/// <param name="Order">Ascending sort order within the zone.</param>
/// <param name="Config">Optional JSON config; <c>null</c> means use widget defaults.</param>
public record WidgetPlacement(
	string WidgetName,
	int Order,
	JsonElement? Config);
