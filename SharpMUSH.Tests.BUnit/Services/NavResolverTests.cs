using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services.Navigation;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// <see cref="NavResolver"/>: the game's layout, the destinations that exist and who is looking make the one
/// navigation the rail, the drawer, the tab bar and the palette all draw.
/// </summary>
public class NavResolverTests
{
	private static string Localize(string key) => key;

	private static NavViewer Viewer(
		PortalRole role = PortalRole.Player,
		bool loggedIn = true,
		bool guests = true,
		bool character = true,
		string[]? features = null,
		string[]? holds = null) =>
		new(role,
			scope => holds?.Contains(scope, StringComparer.OrdinalIgnoreCase) == true,
			loggedIn,
			guests,
			character,
			new HashSet<string>(features ?? [GameFeatures.Scenes, GameFeatures.WorldMap], StringComparer.OrdinalIgnoreCase).Contains);

	private static PortalApplication App(string slug, string? nav, int order = 0, PortalRole role = PortalRole.Guest,
		string? permission = null, string kind = "Page") =>
		new(slug, $"App {slug}", null, kind, $"http/{slug}/schema", null, null, role.ToString(), nav, [], order, Permission: permission);

	private static ResolvedNav Resolve(NavLayout? stored, IEnumerable<PortalApplication> apps, NavViewer? viewer = null) =>
		NavResolver.Resolve(stored, NavCatalog.All(apps, Localize), viewer ?? Viewer(), Localize);

	private static string[] Keys(ResolvedNavGroup group) => group.Items.Select(i => i.Key).ToArray();

	private static string Join(IEnumerable<string> values) => string.Join(",", values);

	private static string TabKey(NavTab tab) => tab switch
	{
		NavDestination destination => destination.Key,
		ResolvedNavGroup group => NavKeys.Group(group.Id),
	};

	private static NavLayout Stored(IReadOnlyList<NavLayoutGroup> groups, string[]? hidden = null, string[]? tabs = null,
		string newEntries = NavNewEntries.Show) =>
		new(NavLayout.CurrentFormat, 1, groups, hidden ?? [], tabs ?? [], newEntries);

	[Test]
	public async Task WithNoStoredLayout_TheCoreSectionsAreInPlayAndWorld()
	{
		var nav = Resolve(null, []);

		await Assert.That(Join(nav.Groups.Select(g => g.Id))).IsEqualTo("play,world");
		await Assert.That(Join(Keys(nav.Groups[0]))).IsEqualTo("core:play,core:scenes,core:mail");
		await Assert.That(Join(Keys(nav.Groups[1]))).IsEqualTo("core:wiki,core:map,core:characters,core:help");
		await Assert.That(nav.Groups[0].Label).IsEqualTo("Play");
		await Assert.That(nav.Groups[1].Label).IsEqualTo("NavSectionWorld");
		await Assert.That(Join(nav.Tabs.Select(TabKey))).IsEqualTo("core:play,core:wiki,core:characters");
	}

	/// <summary>A visitor while guest logins are off, with no character and no optional applications.</summary>
	[Test]
	public async Task EachDestinationKeepsItsOwnGate_AndAnEmptyGroupGoes()
	{
		var nav = Resolve(null, [], Viewer(PortalRole.Guest, loggedIn: false, guests: false, character: false, features: []));

		await Assert.That(Join(nav.Groups.Select(g => g.Id))).IsEqualTo("world");
		await Assert.That(Join(Keys(nav.Groups[0]))).IsEqualTo("core:wiki,core:characters,core:help");
		await Assert.That(Join(nav.Tabs.Select(TabKey))).IsEqualTo("core:wiki,core:characters");
	}

	[Test]
	public async Task AnAppJoinsTheGroupItsPlacementNames_ByIdOrLabel()
	{
		var nav = Resolve(null, [App("chargen", "Play", 5), App("atlas", "world", 1)]);

		await Assert.That(Keys(nav.Groups[0]).Last()).IsEqualTo("app:chargen");
		await Assert.That(Keys(nav.Groups[1]).Last()).IsEqualTo("app:atlas");
	}

	/// <summary>
	/// A placement no group answers to is a section of its own, after the others, ordered by its lowest-ordered
	/// app (ties by name). Build and Manage apps belong to Build &amp; manage, and widgets have no page to link.
	/// </summary>
	[Test]
	public async Task OtherPlacements_BecomeSections_InOrder()
	{
		var nav = Resolve(null,
		[
			App("a", "Build", 0),
			App("m", "Manage", 0),
			App("w", "Zeta", 0, kind: "Widget"),
			App("b", "Zeta", 30),
			App("c", "Alpha", 10),
			App("d", "Alpha", 99),
			App("e", "Mid", 10),
			App("none", null),
		]);

		await Assert.That(Join(nav.Groups.Select(g => g.Label))).IsEqualTo("Play,NavSectionWorld,Alpha,Mid,Zeta");
		var alpha = nav.Groups[2];
		await Assert.That(alpha.Mode).IsEqualTo(NavGroupModes.Section);
		await Assert.That(Join(Keys(alpha))).IsEqualTo("app:c,app:d");
		await Assert.That(nav.Shown.Any(d => d.Key is "app:a" or "app:m" or "app:w" or "app:none")).IsFalse();
	}

	[Test]
	public async Task AnAppShows_OnlyToWhoeverItsRoleAndPermissionAdmit()
	{
		var apps = new[]
		{
			App("staff", "Support", 0, PortalRole.Wizard),
			App("review", "Support", 1, permission: "chargen.review"),
			App("open", "Support", 2),
		};

		var player = Resolve(null, apps, Viewer(PortalRole.Player));
		var reviewer = Resolve(null, apps, Viewer(PortalRole.Player, holds: ["chargen.review"]));
		var wizard = Resolve(null, apps, Viewer(PortalRole.Wizard, holds: ["chargen.review"]));

		await Assert.That(Join(Keys(player.Groups.Single(g => g.Id == "Support")))).IsEqualTo("app:open");
		await Assert.That(Join(Keys(reviewer.Groups.Single(g => g.Id == "Support")))).IsEqualTo("app:review,app:open");
		await Assert.That(Join(Keys(wizard.Groups.Single(g => g.Id == "Support")))).IsEqualTo("app:staff,app:review,app:open");
	}

	[Test]
	public async Task AStoredLayout_PlacesHidesAndDrawsAsStaffSaid()
	{
		var stored = Stored(
		[
			new("community", "Community", NavGroupModes.Flyout, "forum", [new("app:boards"), new("app:highlights"), new("core:wiki")]),
			new("play", null, NavGroupModes.Inline, null, [new("core:play")]),
		],
		hidden: ["app:highlights", "core:help"]);

		var nav = Resolve(stored, [App("boards", "Community"), App("highlights", "Community")]);

		await Assert.That(nav.Groups[0].Id).IsEqualTo("community");
		await Assert.That(nav.Groups[0].Mode).IsEqualTo(NavGroupModes.Flyout);
		await Assert.That(Join(Keys(nav.Groups[0]))).IsEqualTo("app:boards,core:wiki");
		await Assert.That(Keys(nav.Groups[1])).Contains("core:scenes").Because("a core section no group names goes back to its own group");
		await Assert.That(nav.Shown.Any(d => d.Key == "core:help")).IsFalse();
		await Assert.That(Join(nav.Hidden.Select(d => d.Key))).IsEqualTo("app:highlights,core:help");
		await Assert.That(nav.Groups.Any(g => g.Id == "world")).IsTrue().Because("world's sections still exist and nothing placed them");
	}

	[Test]
	public async Task APopOutWithOneDestinationLeft_IsDrawnInline()
	{
		var stored = Stored([new("staff", "Staff", NavGroupModes.Flyout, null, [new("app:jobs"), new("app:secret")])]);

		var nav = Resolve(stored, [App("jobs", "Support"), App("secret", "Support", role: PortalRole.Wizard)]);

		var staff = nav.Groups.Single(g => g.Id == "staff");
		await Assert.That(staff.Mode).IsEqualTo(NavGroupModes.Inline);
		await Assert.That(Join(Keys(staff))).IsEqualTo("app:jobs");
	}

	[Test]
	public async Task HiddenThingsTheViewerCannotOpen_AreNotOfferedEither()
	{
		var stored = Stored([], hidden: ["app:secret"]);

		var nav = Resolve(stored, [App("secret", "Staff", role: PortalRole.Wizard)]);

		await Assert.That(nav.Hidden).IsEmpty();
	}

	[Test]
	public async Task WhenStaffChoose_NewArrivalsWaitHidden()
	{
		var stored = Stored([new("play", null, NavGroupModes.Inline, null, [new("core:play")])], newEntries: NavNewEntries.Hide);

		var nav = Resolve(stored, [App("boards", "Community")]);

		await Assert.That(nav.Shown.Any(d => d.Key == "app:boards")).IsFalse();
		await Assert.That(nav.Hidden.Select(d => d.Key)).Contains("app:boards");
	}

	/// <summary>An uninstalled app keeps its place in the layout, and comes back to it when it is installed again.</summary>
	[Test]
	public async Task AKeyForSomethingGone_IsSkipped_AndAnUnknownModeIsInline()
	{
		var stored = Stored([new("community", "Community", "carousel", null, [new("app:gone"), new("app:boards")])]);

		var without = Resolve(stored, []);
		var with = Resolve(stored, [App("gone", "Elsewhere"), App("boards", "Community")]);

		await Assert.That(without.Groups.Any(g => g.Id == "community")).IsFalse();
		var community = with.Groups.Single(g => g.Id == "community");
		await Assert.That(community.Mode).IsEqualTo(NavGroupModes.Inline);
		await Assert.That(Join(Keys(community))).IsEqualTo("app:gone,app:boards");
	}

	[Test]
	public async Task ACustomLink_ShowsToWhoeverHoldsItsPermission()
	{
		var stored = Stored(
		[
			new("links", "Links", NavGroupModes.Inline, null,
			[
				new("link:discord", "Our Discord", "https://discord.gg/example", "chat"),
				new("link:handbook", "Staff handbook", "/wiki/staff-handbook", null, "players.moderate"),
			]),
		]);

		var player = Resolve(stored, []);
		var staff = Resolve(stored, [], Viewer(holds: ["players.moderate"]));

		var discord = player.Groups.Single(g => g.Id == "links").Items.Single();
		await Assert.That(discord.Label).IsEqualTo("Our Discord");
		await Assert.That(discord.External).IsTrue();
		await Assert.That(discord.IsCurrent("/")).IsFalse();
		var handbook = staff.Groups.Single(g => g.Id == "links").Items.Last();
		await Assert.That(handbook.IsCurrent("/wiki/staff-handbook")).IsTrue();
	}

	[Test]
	public async Task TheTabBar_TakesAtMostThree_ThatTheViewerMayOpen()
	{
		var stored = Stored([], tabs: ["core:mail", "core:play", "core:wiki", "app:boards", "core:help"]);

		var nav = Resolve(stored, [App("boards", "Community")], Viewer(character: false));

		await Assert.That(Join(nav.Tabs.Select(TabKey))).IsEqualTo("core:play,core:wiki,app:boards");
	}

	/// <summary>
	/// A phone tab can be a whole group, as the viewer sees it. A group left with one destination for them is that
	/// destination's tab, and a group they see nothing of is no tab.
	/// </summary>
	[Test]
	public async Task AGroupTab_IsTheGroupAsTheViewerSeesIt()
	{
		var stored = Stored(
		[
			new("play", null, NavGroupModes.Inline, null, [new("core:play"), new("core:mail")]),
			new("community", "Community", NavGroupModes.Flyout, null, [new("app:boards"), new("app:staffnotes")]),
			new("world", null, NavGroupModes.Inline, null, [new("core:wiki"), new("core:help")]),
		], tabs: ["group:community", "group:play", "group:world"]);
		var apps = new[] { App("boards", "Community"), App("staffnotes", "Community", role: PortalRole.Wizard) };

		var staff = Resolve(stored, apps, Viewer(PortalRole.Wizard));
		var player = Resolve(stored, apps, Viewer(character: false, features: []));

		await Assert.That(Join(staff.Tabs.Select(TabKey))).IsEqualTo("group:community,group:play,group:world");
		await Assert.That(staff.Tabs[0] is ResolvedNavGroup { Items.Count: 2 }).IsTrue();
		await Assert.That(Join(player.Tabs.Select(TabKey))).IsEqualTo("app:boards,core:play,group:world")
			.Because("the player sees one of Community's two, and Play holds no Mail without a character and no Scenes without the feature");
	}

	/// <summary>A layout's phone tab names a destination or one of the layout's own groups.</summary>
	[Test]
	public async Task AGroupTab_MustNameAGroupOfTheLayout()
	{
		var groups = new NavLayoutGroup[] { new("play", null, NavGroupModes.Inline, null, [new("core:play")]) };

		await Assert.That(NavLayoutRules.Problem(Stored(groups, tabs: ["group:play"]))).IsNull();
		await Assert.That(NavLayoutRules.Problem(Stored(groups, tabs: ["group:world"]))).IsNotNull();
	}

	/// <summary>A pop-out's button shows its destinations' icons unless staff gave the group an icon of its own.</summary>
	[Test]
	public async Task AGroup_KnowsWhetherItHasAnIconOfItsOwn()
	{
		var nav = Resolve(Stored(
		[
			new("plain", "Plain", NavGroupModes.Flyout, null, [new("core:play"), new("core:scenes")]),
			new("forum", "Forum", NavGroupModes.Flyout, "forum", [new("core:wiki"), new("core:help")]),
		]), []);

		await Assert.That(nav.Groups.Single(g => g.Id == "plain").OwnIcon).IsFalse();
		await Assert.That(nav.Groups.Single(g => g.Id == "forum").OwnIcon).IsTrue();
	}
}
