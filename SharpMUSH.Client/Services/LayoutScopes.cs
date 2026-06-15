using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Services;

/// <summary>
/// A customizable page layout: its scope key, a human label, and the zones an admin may edit for it.
/// </summary>
/// <param name="Scope">Machine key passed to <see cref="ILayoutService"/> and the layout REST API.</param>
/// <param name="DisplayName">Human-readable label shown in the admin layout index.</param>
/// <param name="Description">Short explanation of what the scope drives.</param>
/// <param name="Zones">Zones this scope renders, and therefore the zones the editor exposes.</param>
public record LayoutScope(string Scope, string DisplayName, string Description, WidgetZone[] Zones);

/// <summary>
/// The known, editable layout scopes. <c>"global"</c> drives the shell chrome (top bar, sidebars,
/// footer) rendered by <c>MainLayout</c>; the page scopes drive an individual page's own zones.
/// </summary>
public static class LayoutScopes
{
	public const string Global = "global";
	public const string Home = "home";
	public const string WikiIndex = "wiki-index";
	public const string Profile = "profile";

	public static readonly IReadOnlyList<LayoutScope> All =
	[
		new(Global, "Site Chrome", "Top bar, sidebars, and footer shared by every page.",
			[WidgetZone.TopBar, WidgetZone.LeftSidebar, WidgetZone.RightSidebar, WidgetZone.Footer]),
		new(Home, "Home Page", "The main content of the landing page.",
			[WidgetZone.MainContent]),
		new(WikiIndex, "Wiki Index", "The /wiki landing page.",
			[WidgetZone.MainContent]),
		new(Profile, "Character Profile", "The /character/{name} page body and right rail.",
			[WidgetZone.MainContent, WidgetZone.RightSidebar]),
	];

	public static LayoutScope? Find(string scope)
		=> All.FirstOrDefault(s => string.Equals(s.Scope, scope, StringComparison.OrdinalIgnoreCase));
}
