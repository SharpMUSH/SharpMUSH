using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>Game-at-a-glance stat tiles (players, scenes, recent changes, characters).</summary>
public sealed class StatsWidgetDescriptor : IPortalWidget
{
	public string Name => "Stats";
	public string DisplayName => "Game Stats";
	public string Description => "At-a-glance tiles: players online, active scenes, recent changes, characters.";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent];
	public Type ComponentType => typeof(StatsWidget);
	public Type? ConfigType => null;
}

/// <summary>The most recent active scene, with a join link.</summary>
public sealed class ActiveSceneWidgetDescriptor : IPortalWidget
{
	public string Name => "ActiveScene";
	public string DisplayName => "Active Scene";
	public string Description => "Highlights the most recent in-progress scene.";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(ActiveSceneWidget);
	public Type? ConfigType => null;
}

/// <summary>The most recently edited wiki pages.</summary>
public sealed class RecentWikiActivityWidgetDescriptor : IPortalWidget
{
	public string Name => "RecentWikiActivity";
	public string DisplayName => "Recent Wiki Activity";
	public string Description => "A feed of the most recently edited wiki pages.";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(RecentWikiActivityWidget);
	public Type? ConfigType => null;
}

/// <summary>The character directory list, linking to profiles.</summary>
public sealed class OnlineCharactersWidgetDescriptor : IPortalWidget
{
	public string Name => "OnlineCharacters";
	public string DisplayName => "Characters";
	public string Description => "Lists characters and links to their profiles.";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(OnlineCharactersWidget);
	public Type? ConfigType => null;
}

/// <summary>Static "new here?" quickstart links.</summary>
public sealed class QuickstartWidgetDescriptor : IPortalWidget
{
	public string Name => "Quickstart";
	public string DisplayName => "Quickstart";
	public string Description => "Getting-started links for new visitors.";
	public WidgetSize DefaultSize => WidgetSize.Medium;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent, WidgetZone.LeftSidebar, WidgetZone.RightSidebar];
	public Type ComponentType => typeof(QuickstartWidget);
	public Type? ConfigType => null;
}
