using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>Game-at-a-glance stat tiles (players, scenes, recent changes, characters).</summary>
public sealed class StatsWidgetDescriptor : IPortalWidget
{
	public string Name => "Stats";
	public string DisplayName => "LayWidgetGameStats";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent];
	public Type ComponentType => typeof(StatsWidget);
	public Type? ConfigType => null;
}

/// <summary>The most recent active scene, with a join link.</summary>
public sealed class ActiveSceneWidgetDescriptor : IPortalWidget
{
	public string Name => "ActiveScene";
	public string DisplayName => "LayWidgetActiveScene";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(ActiveSceneWidget);
	public Type? ConfigType => null;
}

/// <summary>The most recently edited wiki pages.</summary>
public sealed class RecentWikiActivityWidgetDescriptor : IPortalWidget
{
	public string Name => "RecentWikiActivity";
	public string DisplayName => "LayWidgetRecentWikiActivity";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(RecentWikiActivityWidget);
	public Type? ConfigType => null;
}

/// <summary>Characters currently connected (lwho()), linking to profiles.</summary>
public sealed class OnlineCharactersWidgetDescriptor : IPortalWidget
{
	public string Name => "OnlineCharacters";
	public string DisplayName => "LayWidgetOnlineCharacters";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(OnlineCharactersWidget);
	public Type? ConfigType => null;
}

/// <summary>Static "new here?" quickstart links.</summary>
public sealed class QuickstartWidgetDescriptor : IPortalWidget
{
	public string Name => "Quickstart";
	public string DisplayName => "LayWidgetQuickstart";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(QuickstartWidget);
	public Type? ConfigType => null;
}
