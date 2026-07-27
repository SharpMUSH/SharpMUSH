using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Resources;

public static class WidgetZoneLabels
{
	/// <summary>
	/// The <see cref="SharedResource"/> key holding the zone's human-readable label. The enum name is a
	/// machine identifier and must never reach the UI — "MainContent" is not a thing anyone types or
	/// says — so every display site looks the label up through <c>Loc[zone.ResourceKey()]</c> rather
	/// than rendering the zone directly.
	/// </summary>
	public static string ResourceKey(this WidgetZone zone) => $"WidgetZone{zone}";
}
