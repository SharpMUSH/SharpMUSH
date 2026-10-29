using SharpMUSH.Library.Models.Portal.Navigation;

namespace SharpMUSH.Client.Services.Navigation;

/// <summary>A group as one viewer sees it: only what they may open, at least one destination.</summary>
/// <param name="Id">The group's id.</param>
/// <param name="Label">Its name, localized for a built-in group.</param>
/// <param name="Mode">A <see cref="NavGroupModes"/> value. A pop-out with one destination is drawn inline.</param>
/// <param name="OutlineIcon">The rail button's icon in section and flyout modes: the group's own, else its first destination's.</param>
/// <param name="FillIcon">The same, filled.</param>
/// <param name="Items">The destinations, in order.</param>
public sealed record ResolvedNavGroup(
	string Id,
	string Label,
	string Mode,
	string OutlineIcon,
	string FillIcon,
	IReadOnlyList<NavDestination> Items)
{
	/// <summary>Whether the page at <paramref name="path"/> belongs to this group.</summary>
	public bool IsCurrent(string path) => Items.Any(item => item.IsCurrent(path));
}

/// <summary>The navigation one viewer sees, for the rail, the drawer, the tab bar and the command palette alike.</summary>
/// <param name="Groups">The groups, top to bottom.</param>
/// <param name="Hidden">What staff hid that the viewer may open: the command palette still finds these.</param>
/// <param name="Tabs">The phone tab bar's destinations between Home and Menu.</param>
public sealed record ResolvedNav(
	IReadOnlyList<ResolvedNavGroup> Groups,
	IReadOnlyList<NavDestination> Hidden,
	IReadOnlyList<NavDestination> Tabs)
{
	public static readonly ResolvedNav Empty = new([], [], []);

	/// <summary>Every destination the navigation shows, in order.</summary>
	public IEnumerable<NavDestination> Shown => Groups.SelectMany(group => group.Items);

	/// <summary>The group holding the page at <paramref name="path"/>, if any.</summary>
	public ResolvedNavGroup? GroupAt(string path) => Groups.FirstOrDefault(group => group.IsCurrent(path));
}

/// <summary>
/// Puts the destinations where the game's layout says, for one viewer. A destination no group names goes to the
/// group its hint names (a group's id or label), or to a new section group named after the hint, the way a package's
/// <c>nav_placement</c> always made a section of its own. A key that names nothing that exists any more is skipped
/// and kept, so an application that is reinstalled comes back to its place.
/// </summary>
public static class NavResolver
{
	/// <summary>The arrangement a game has before staff change anything: today's Play and World groups.</summary>
	public static readonly NavLayout Default = new(
		NavLayout.CurrentFormat,
		0,
		[
			new NavLayoutGroup(NavCatalog.PlayGroup, null, NavGroupModes.Inline, null,
				[new(NavKeys.Core("play")), new(NavKeys.Core("scenes")), new(NavKeys.Core("mail"))]),
			new NavLayoutGroup(NavCatalog.WorldGroup, null, NavGroupModes.Inline, null,
				[new(NavKeys.Core("wiki")), new(NavKeys.Core("map")), new(NavKeys.Core("characters")), new(NavKeys.Core("help"))]),
		],
		[],
		[NavKeys.Core("play"), NavKeys.Core("wiki"), NavKeys.Core("characters")]);

	private static readonly StringComparer Keys = StringComparer.OrdinalIgnoreCase;

	private sealed record Slot(string Id, string? Label, string Mode, string? Icon, List<NavLayoutItem> Items);

	/// <summary>
	/// The layout with every destination no group names put where it goes: into the group its hint names, or into
	/// <see cref="NavLayout.Hidden"/> when staff keep new arrivals hidden. What staff see in the editor and what
	/// <see cref="Resolve"/> filters for each viewer. Keys for things that no longer exist are kept.
	/// </summary>
	/// <param name="stored">The game's layout, or null when staff never changed it.</param>
	/// <param name="catalog">Every destination that exists (<see cref="NavCatalog.All"/>).</param>
	public static NavLayout Arrange(NavLayout? stored, IReadOnlyList<NavDestination> catalog)
	{
		var layout = stored ?? Default;
		var hidden = new List<string>(layout.Hidden.Distinct(Keys));
		var placed = new HashSet<string>(hidden, Keys);
		var slots = layout.Groups
			.Where(group => !string.IsNullOrWhiteSpace(group.Id))
			.DistinctBy(group => group.Id, Keys)
			.Select(group => new Slot(group.Id, group.Label, NavGroupModes.Parse(group.Mode), group.Icon,
				group.Items.Where(item => placed.Add(item.Key)).ToList()))
			.ToList();

		var arrivals = catalog
			.Where(destination => !placed.Contains(destination.Key) && destination.Hint is not null)
			.OrderBy(destination => destination.Order)
			.ThenBy(destination => destination.Label, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (stored is not null && stored.NewEntries == NavNewEntries.Hide)
		{
			hidden.AddRange(arrivals.Select(destination => destination.Key));
		}
		else
		{
			Place(arrivals, slots);
		}

		return layout with
		{
			Groups = [.. slots.Select(slot => new NavLayoutGroup(slot.Id, slot.Label, slot.Mode, slot.Icon, slot.Items))],
			Hidden = hidden,
		};
	}

	/// <param name="stored">The game's layout, or null when staff never changed it.</param>
	/// <param name="catalog">Every destination that exists (<see cref="NavCatalog.All"/>).</param>
	/// <param name="viewer">Who is looking.</param>
	/// <param name="localize">Looks up a <c>SharedResource</c> name.</param>
	public static ResolvedNav Resolve(NavLayout? stored, IReadOnlyList<NavDestination> catalog, NavViewer viewer, Func<string, string> localize)
	{
		var layout = Arrange(stored, catalog);
		var destinations = Destinations(layout, catalog);
		var hidden = new HashSet<string>(layout.Hidden, Keys);

		var groups = layout.Groups.Select(group => Resolve(group, destinations, hidden, viewer, localize)).OfType<ResolvedNavGroup>().ToList();
		var shown = new HashSet<string>(groups.SelectMany(group => group.Items).Select(item => item.Key), Keys);
		return new ResolvedNav(
			groups,
			Visible(layout.Hidden, destinations, viewer).Where(destination => !shown.Contains(destination.Key)).ToList(),
			Visible(layout.MobileTabs, destinations, viewer).Take(NavLayout.MaxMobileTabs).ToList());
	}

	/// <summary>Every destination by key: the catalog's, and the links staff added to <paramref name="layout"/>.</summary>
	public static IReadOnlyDictionary<string, NavDestination> Destinations(NavLayout layout, IReadOnlyList<NavDestination> catalog)
	{
		var destinations = new Dictionary<string, NavDestination>(Keys);
		foreach (var destination in catalog) destinations.TryAdd(destination.Key, destination);
		foreach (var link in layout.Groups.SelectMany(group => group.Items).Where(item => NavKeys.IsLink(item.Key)))
			destinations.TryAdd(link.Key, NavCatalog.Link(link));
		return destinations;
	}

	/// <summary>A group's name as the viewer reads it: what staff called it, else a built-in group's own, else its id.</summary>
	public static string Label(string id, string? label, Func<string, string> localize) =>
		!string.IsNullOrWhiteSpace(label) ? label.Trim()
		: NavCatalog.GroupLabelKey(id) is { } key ? localize(key)
		: id;

	/// <summary>
	/// Each arrival joins the group whose id or name its hint is. Hints no group answers to become groups after the rest,
	/// ordered by their lowest-ordered destination, ties by name: a section, or for a built-in group staff removed, that
	/// group again as it first was.
	/// </summary>
	private static void Place(IReadOnlyList<NavDestination> arrivals, List<Slot> slots)
	{
		var created = new List<(Slot Slot, int Order)>();
		foreach (var arrival in arrivals)
		{
			var hint = arrival.Hint!;
			var slot = slots.FirstOrDefault(s => Keys.Equals(s.Id, hint) || Keys.Equals(s.Label?.Trim(), hint))
				?? created.Select(c => c.Slot).FirstOrDefault(s => Keys.Equals(s.Id, hint));
			if (slot is null)
			{
				var builtIn = NavCatalog.GroupLabelKey(hint) is not null;
				slot = new Slot(builtIn ? hint.ToLowerInvariant() : hint, builtIn ? null : hint,
					builtIn ? NavGroupModes.Inline : NavGroupModes.Section, null, []);
				created.Add((slot, arrival.Order));
			}
			slot.Items.Add(new NavLayoutItem(arrival.Key));
		}
		slots.AddRange(created
			.OrderBy(c => c.Order)
			.ThenBy(c => c.Slot.Label ?? c.Slot.Id, StringComparer.OrdinalIgnoreCase)
			.Select(c => c.Slot));
	}

	private static ResolvedNavGroup? Resolve(NavLayoutGroup group, IReadOnlyDictionary<string, NavDestination> destinations,
		HashSet<string> hidden, NavViewer viewer, Func<string, string> localize)
	{
		var items = group.Items
			.Where(item => !hidden.Contains(item.Key))
			.Select(item => destinations.GetValueOrDefault(item.Key))
			.OfType<NavDestination>()
			.Where(destination => destination.Admits(viewer))
			.ToList();
		if (items.Count == 0) return null;

		var mode = group.Mode == NavGroupModes.Flyout && items.Count == 1 ? NavGroupModes.Inline : group.Mode;
		return new ResolvedNavGroup(group.Id, Label(group.Id, group.Label, localize), mode,
			AppIcons.Outline(group.Icon, items[0].OutlineIcon), AppIcons.Fill(group.Icon, items[0].FillIcon), items);
	}

	private static IEnumerable<NavDestination> Visible(IEnumerable<string> keys, IReadOnlyDictionary<string, NavDestination> destinations, NavViewer viewer) =>
		keys.Distinct(Keys)
			.Select(key => destinations.GetValueOrDefault(key))
			.OfType<NavDestination>()
			.Where(destination => destination.Admits(viewer));

}
