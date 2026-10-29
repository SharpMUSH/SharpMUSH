using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models.Portal.Navigation;

/// <summary>
/// The game's arrangement of the portal's main navigation: which groups the rail and the drawer show, in what
/// order, how each group is drawn, what is hidden and which destinations the phone tab bar carries. It says only
/// where things go; what exists, and who may open it, comes from the destinations themselves, so a layout can
/// hide a destination but never show one its own gate refuses.
/// </summary>
/// <param name="Format">The document's format; a newer one than <see cref="CurrentFormat"/> is read as far as it is understood.</param>
/// <param name="Version">Bumped on every save, so two staff saving at once cannot overwrite each other unseen.</param>
/// <param name="Groups">The groups, top to bottom. Each destination sits in at most one.</param>
/// <param name="Hidden">Destination keys left out of the navigation. Their pages still open for whoever may see them.</param>
/// <param name="MobileTabs">The destinations between Home and Menu on the phone tab bar, at most <see cref="MaxMobileTabs"/>.</param>
/// <param name="NewEntries">
/// <see cref="NavNewEntries.Show"/> slots a destination no group names into its hinted place;
/// <see cref="NavNewEntries.Hide"/> keeps it hidden until staff place it.
/// </param>
public sealed record NavLayout(
	int Format,
	long Version,
	IReadOnlyList<NavLayoutGroup> Groups,
	IReadOnlyList<string> Hidden,
	IReadOnlyList<string> MobileTabs,
	string NewEntries = NavNewEntries.Show)
{
	public const int CurrentFormat = 1;

	/// <summary>Home and Menu are fixed, so the bar keeps to Material's five destinations.</summary>
	public const int MaxMobileTabs = 3;
}

/// <summary>One group of destinations.</summary>
/// <param name="Id">Stable id; a package's <c>nav_placement</c> matches it or the label.</param>
/// <param name="Label">What staff named it, or null for a built-in group's own localized name.</param>
/// <param name="Mode">A <see cref="NavGroupModes"/> value.</param>
/// <param name="Icon">A Material icon name for the group's own button (section and flyout modes).</param>
/// <param name="Items">The destinations, in order.</param>
public sealed record NavLayoutGroup(
	string Id,
	string? Label,
	string Mode,
	string? Icon,
	IReadOnlyList<NavLayoutItem> Items);

/// <summary>
/// One place in a group: a destination's key. A custom link (<see cref="NavKeys.LinkPrefix"/>) also carries
/// its own label, address, icon and the permission that shows it, since nothing else declares it.
/// </summary>
public sealed record NavLayoutItem(
	string Key,
	string? Label = null,
	string? Href = null,
	string? Icon = null,
	string? Permission = null);

/// <summary>How a group is drawn.</summary>
public static class NavGroupModes
{
	/// <summary>Every destination has its own rail button, under the group's name.</summary>
	public const string Inline = "inline";

	/// <summary>One rail button opens the first destination; the section's sidebar lists the rest.</summary>
	public const string Section = "section";

	/// <summary>One rail button opens a list of the destinations; each opens directly.</summary>
	public const string Flyout = "flyout";

	/// <summary>The mode <paramref name="value"/> names, or <see cref="Inline"/> for one this portal does not know.</summary>
	public static string Parse(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		Section => Section,
		Flyout => Flyout,
		_ => Inline,
	};
}

/// <summary>What happens to a destination no group names yet.</summary>
public static class NavNewEntries
{
	public const string Show = "show";
	public const string Hide = "hide";
}

/// <summary>How destinations are named in a layout. Keys compare case-insensitively.</summary>
public static class NavKeys
{
	public const string CorePrefix = "core:";
	public const string AppPrefix = "app:";
	public const string LinkPrefix = "link:";

	public static string Core(string name) => CorePrefix + name;
	public static string App(string slug) => AppPrefix + slug;
	public static string Link(string id) => LinkPrefix + id;

	public static bool IsLink(string key) => key.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Another save landed first: the layout is at <see cref="CurrentVersion"/> now.</summary>
public readonly record struct NavLayoutConflict(long CurrentVersion);

/// <summary>A save's outcome: the layout as stored, a conflict, or why it was refused.</summary>
public union NavLayoutSaveResult(NavLayout, NavLayoutConflict, Error<string>);

/// <summary>A save from the editor: the layout and the version it was read at.</summary>
public sealed record SaveNavLayoutRequest(NavLayout Layout, long ExpectedVersion);

/// <summary>What a layout must be to be saved. The editor checks it as staff type, the server again on save.</summary>
public static class NavLayoutRules
{
	public const int MaxGroups = 50;
	public const int MaxItems = 500;
	public const int MaxIdLength = 64;
	public const int MaxLabelLength = 64;
	public const int MaxIconLength = 64;
	public const int MaxKeyLength = 200;
	public const int MaxHrefLength = 2048;

	/// <summary>Why <paramref name="layout"/> can't be saved, or null when it can.</summary>
	public static string? Problem(NavLayout? layout)
	{
		if (layout is null) return "A layout is required.";
		if (layout.Format != NavLayout.CurrentFormat) return $"This server saves layout format {NavLayout.CurrentFormat}.";
		if (layout.Groups is null || layout.Hidden is null || layout.MobileTabs is null) return "A layout needs groups, hidden and phone tabs, even if empty.";
		if (layout.Groups.Count > MaxGroups) return $"A layout holds at most {MaxGroups} groups.";
		if (layout.NewEntries is not (NavNewEntries.Show or NavNewEntries.Hide)) return "New entries are either shown or hidden.";
		if (layout.MobileTabs.Count > NavLayout.MaxMobileTabs) return $"The phone tab bar holds at most {NavLayout.MaxMobileTabs} tabs.";

		var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var group in layout.Groups)
		{
			if (group is null || group.Items is null) return "Every group needs a list of items.";
			if (!IsId(group.Id)) return $"A group id is 1 to {MaxIdLength} letters, digits, '-' or '_'.";
			if (!ids.Add(group.Id)) return $"Two groups have the id '{group.Id}'.";
			if (group.Label is { Length: > MaxLabelLength }) return $"A group's name is at most {MaxLabelLength} characters.";
			if (group.Icon is { Length: > MaxIconLength }) return $"An icon name is at most {MaxIconLength} characters.";
			if (group.Mode is not (NavGroupModes.Inline or NavGroupModes.Section or NavGroupModes.Flyout))
				return $"Group '{group.Id}' is drawn inline, as a section or as a pop-out.";
			foreach (var item in group.Items)
			{
				if (item is null || !IsKey(item.Key)) return $"'{item?.Key}' is not a destination key.";
				if (!keys.Add(item.Key)) return $"'{item.Key}' is in two places.";
				if (NavKeys.IsLink(item.Key) && LinkProblem(item) is { } link) return link;
			}
		}
		if (keys.Count > MaxItems) return $"A layout holds at most {MaxItems} destinations.";
		if (layout.Hidden.Count > MaxItems || layout.Hidden.Any(key => !IsKey(key))) return "A hidden entry is not a destination key.";
		if (layout.MobileTabs.Any(key => !IsKey(key))) return "A phone tab is not a destination key.";
		return null;
	}

	private static string? LinkProblem(NavLayoutItem item)
	{
		if (string.IsNullOrWhiteSpace(item.Label) || item.Label.Length > MaxLabelLength)
			return $"A link's name is 1 to {MaxLabelLength} characters.";
		if (item.Icon is { Length: > MaxIconLength }) return $"An icon name is at most {MaxIconLength} characters.";
		if (item.Permission is { Length: > MaxKeyLength }) return "A permission scope is at most 200 characters.";
		var href = item.Href?.Trim() ?? string.Empty;
		var portal = href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal);
		var web = Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
		return href.Length is 0 or > MaxHrefLength || !(portal || web)
			? $"Link '{item.Label}' goes to a portal page (starting with /) or an http or https address."
			: null;
	}

	private static bool IsId(string? id) =>
		id is { Length: > 0 and <= MaxIdLength } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

	private static bool IsKey(string? key)
	{
		if (key is not { Length: > 0 and <= MaxKeyLength }) return false;
		var colon = key.IndexOf(':');
		if (colon <= 0 || colon == key.Length - 1) return false;
		var prefix = key[..(colon + 1)];
		return (prefix.Equals(NavKeys.CorePrefix, StringComparison.OrdinalIgnoreCase)
				|| prefix.Equals(NavKeys.AppPrefix, StringComparison.OrdinalIgnoreCase)
				|| prefix.Equals(NavKeys.LinkPrefix, StringComparison.OrdinalIgnoreCase))
			&& !key[(colon + 1)..].Any(char.IsWhiteSpace);
	}
}
