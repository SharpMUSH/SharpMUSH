using MudBlazor;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Services.Navigation;

/// <summary>
/// Every destination the main navigation can show: the portal's own sections and the page applications that name a
/// <c>nav_placement</c>. Applications placed in Build or Manage are not here; they belong to the Build &amp; manage
/// section (<c>BuildNavCatalog</c> and its sidebar).
/// </summary>
public static class NavCatalog
{
	public const string PlayGroup = "play";
	public const string WorldGroup = "world";

	/// <summary>The <c>SharedResource</c> name of a built-in group, which a layout leaves unnamed.</summary>
	public static string? GroupLabelKey(string id) => id.ToLowerInvariant() switch
	{
		PlayGroup => "Play",
		WorldGroup => "NavSectionWorld",
		_ => null,
	};

	/// <summary>The portal's own sections, in the order their groups list them.</summary>
	public static IReadOnlyList<NavDestination> Core(Func<string, string> localize) =>
	[
		Section("play", "/play", Icons.Material.Outlined.SportsEsports, Icons.Material.Filled.SportsEsports, localize("Play"), PlayGroup, 0,
			viewer => viewer.LoggedIn || viewer.GuestsEnabled),
		Section("scenes", "/scenes", Icons.Material.Outlined.Theaters, Icons.Material.Filled.Theaters, localize("SceneArchive"), PlayGroup, 1,
			viewer => viewer.HasFeature(GameFeatures.Scenes)),
		Section("mail", "/mail", Icons.Material.Outlined.MailOutline, Icons.Material.Filled.Mail, localize("Mail"), PlayGroup, 2,
			viewer => viewer.CharacterActive),
		Section("wiki", "/wiki", Icons.Material.Outlined.MenuBook, Icons.Material.Filled.MenuBook, localize("Wiki"), WorldGroup, 0,
			_ => true),
		Section("map", "/map", Icons.Material.Outlined.Map, Icons.Material.Filled.Map, localize("NavWorldMap"), WorldGroup, 1,
			viewer => viewer.HasFeature(GameFeatures.WorldMap)),
		Section("characters", "/characters", Icons.Material.Outlined.Groups, Icons.Material.Filled.Groups, localize("Characters"), WorldGroup, 2,
			_ => true, "/character"),
		Section("help", "/help", Icons.Material.Outlined.HelpOutline, Icons.Material.Filled.Help, localize("Help"), WorldGroup, 3,
			_ => true),
	];

	/// <summary>The page applications that ask for a place in the main navigation.</summary>
	public static IReadOnlyList<NavDestination> Applications(IEnumerable<PortalApplication> apps) =>
		apps.Where(a => a.KindEnum == ApplicationKind.Page
				&& !string.IsNullOrWhiteSpace(a.NavPlacement)
				&& !IsBuildSection(a.NavPlacement))
			.Select(a => new NavDestination(
				NavKeys.App(a.Slug),
				$"/apps/{a.Slug}",
				AppIcons.Outline(a.Icon, Icons.Material.Outlined.Apps),
				AppIcons.Fill(a.Icon, Icons.Material.Filled.Apps),
				a.DisplayName,
				[$"/apps/{a.Slug}"],
				a.NavPlacement!.Trim(),
				a.Order,
				viewer => a.Admits(viewer.Role, viewer.Holds)))
			.ToList();

	/// <summary>Everything: the core sections, then the applications.</summary>
	public static IReadOnlyList<NavDestination> All(IEnumerable<PortalApplication> apps, Func<string, string> localize) =>
		[.. Core(localize), .. Applications(apps)];

	/// <summary>A link staff added to the layout. One to another site opens in a new tab and is never current.</summary>
	public static NavDestination Link(NavLayoutItem item)
	{
		var href = item.Href?.Trim() ?? "/";
		var external = href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| href.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
		var permission = item.Permission?.Trim();
		var path = href.Split('?', '#')[0].TrimEnd('/');
		return new NavDestination(
			item.Key,
			href,
			AppIcons.Outline(item.Icon, Icons.Material.Outlined.Link),
			AppIcons.Fill(item.Icon, Icons.Material.Filled.Link),
			string.IsNullOrWhiteSpace(item.Label) ? href : item.Label.Trim(),
			external ? [] : [path.Length == 0 ? "/" : path],
			null,
			0,
			viewer => string.IsNullOrEmpty(permission) || viewer.Holds(permission),
			external);
	}

	private static bool IsBuildSection(string placement) =>
		placement.Trim().Equals("Build", StringComparison.OrdinalIgnoreCase)
		|| placement.Trim().Equals("Manage", StringComparison.OrdinalIgnoreCase);

	private static NavDestination Section(string name, string href, string outline, string fill, string label, string group, int order,
		Func<NavViewer, bool> admits, params string[] alsoUnder) =>
		new(NavKeys.Core(name), href, outline, fill, label, [href, .. alsoUnder], group, order, admits);
}
