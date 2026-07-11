namespace SharpMUSH.Library.Models.Portal.Widgets;

/// <summary>
/// The full persisted layout: which widgets are in each zone, plus sidebar settings.
/// </summary>
/// <param name="Zones">Per-zone ordered list of widget placements.</param>
/// <param name="Settings">Global sidebar / layout flags.</param>
public record LayoutConfiguration(
	Dictionary<WidgetZone, List<WidgetPlacement>> Zones,
	LayoutSettings Settings);
