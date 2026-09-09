using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Library.Models.Portal.Applications;

/// <summary>
/// Shared persistence helpers for the application registry, used by every database provider so the
/// stored shape stays identical across backends. Zones are stored as a comma-joined list of enum
/// names; an empty string means "no zones".
/// </summary>
public static class ApplicationRegistryMapping
{
	/// <summary>Serializes the allowed zones to a comma-joined string of enum names (empty when none).</summary>
	public static string ZonesToString(IReadOnlyList<WidgetZone>? zones)
		=> zones is null || zones.Count == 0 ? string.Empty : string.Join(",", zones.Select(z => z.ToString()));

	/// <summary>Parses a comma-joined zone list back to enum values; unknown/blank tokens are skipped.</summary>
	public static IReadOnlyList<WidgetZone>? ZonesFromString(string? zones)
	{
		if (string.IsNullOrWhiteSpace(zones))
		{
			return null;
		}

		var parsed = new List<WidgetZone>();
		foreach (var range in zones.AsSpan().Split(','))
		{
			if (Enum.TryParse<WidgetZone>(zones.AsSpan()[range].Trim(), ignoreCase: true, out var zone))
			{
				parsed.Add(zone);
			}
		}

		return parsed.Count == 0 ? null : parsed;
	}
}
