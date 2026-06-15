using System.Text.Json;

namespace SharpMUSH.Library.Models.Portal.Widgets;

/// <summary>
/// A single widget instance placed in a zone with an optional JSON config blob.
/// </summary>
/// <param name="WidgetName">Matches <see cref="IPortalWidget.Name"/>.</param>
/// <param name="Order">Ascending sort order within the zone.</param>
/// <param name="Config">Optional JSON config; <c>null</c> means use widget defaults.</param>
/// <param name="Span">
/// Width in columns of a 12-column grid (1–12). 12 = full width. Lets a zone lay widgets out in a
/// grid that flows into rows. Older layouts that omit it (or store 0) are treated as full width.
/// </param>
public record WidgetPlacement(
	string WidgetName,
	int Order,
	JsonElement? Config,
	int Span = 12);
