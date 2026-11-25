using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Config schema for the Upcoming Scenes widget: how many scheduled scenes it lists, and how far ahead it looks.
/// </summary>
/// <param name="Count">How many scenes to list, 1 to 10.</param>
/// <param name="Days">How many days ahead to look; 0 lists every scheduled scene, however far off.</param>
public record UpcomingScenesConfig(
	[property: WidgetConfigKey("LayCfgUpcomingScenesCount", LabelKey = "LayCfgUpcomingScenesCountLabel")]
	int Count = UpcomingScenesConfig.DefaultCount,
	[property: WidgetConfigKey("LayCfgUpcomingScenesDays", LabelKey = "LayCfgUpcomingScenesDaysLabel")]
	int Days = UpcomingScenesConfig.DefaultDays)
{
	public const int DefaultCount = 3;
	public const int MaxCount = 10;
	public const int DefaultDays = 7;
}
