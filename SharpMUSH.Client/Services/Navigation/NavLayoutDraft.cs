using SharpMUSH.Library.Models.Portal.Navigation;

namespace SharpMUSH.Client.Services.Navigation;

/// <summary>
/// The navigation editor's working copy: the game's layout with every destination in it, changed one step at a time
/// and saved as a whole. It starts from <see cref="NavResolver.Arrange"/>, so what staff see is what players get, and
/// it remembers which destinations nobody had placed yet.
/// </summary>
public sealed class NavLayoutDraft
{
	/// <summary>The hidden destinations, kept apart from the groups.</summary>
	public const string HiddenId = "";

	private static readonly StringComparer Keys = StringComparer.OrdinalIgnoreCase;

	/// <summary>One group as staff edit it.</summary>
	public sealed class Group(string id, string? label, string mode, string? icon, IEnumerable<NavLayoutItem> items)
	{
		public string Id { get; } = id;
		public string? Label { get; set; } = label;
		public string Mode { get; set; } = mode;
		public string? Icon { get; set; } = icon;
		public List<NavLayoutItem> Items { get; } = [.. items];
	}

	private NavLayoutDraft(NavLayout arranged, long version, IReadOnlySet<string> arrivals)
	{
		Version = version;
		NewEntries = arranged.NewEntries;
		Groups = [.. arranged.Groups.Select(g => new Group(g.Id, g.Label, NavGroupModes.Parse(g.Mode), g.Icon, g.Items))];
		Hidden = [.. arranged.Hidden];
		Tabs = [.. arranged.MobileTabs];
		Arrivals = arrivals;
	}

	/// <summary>The version the layout was read at, which a save must still find.</summary>
	public long Version { get; private set; }

	public List<Group> Groups { get; }

	public List<string> Hidden { get; }

	/// <summary>The phone tab bar's destinations between Home and Menu.</summary>
	public List<string> Tabs { get; }

	public string NewEntries { get; set; }

	/// <summary>The destinations no saved layout had placed: they are where the game would put them until staff save.</summary>
	public IReadOnlySet<string> Arrivals { get; }

	/// <param name="stored">The game's layout, or null while staff never saved one.</param>
	/// <param name="catalog">Every destination that exists.</param>
	public static NavLayoutDraft From(NavLayout? stored, IReadOnlyList<NavDestination> catalog)
	{
		var arranged = NavResolver.Arrange(stored, catalog);
		var known = stored is null
			? new HashSet<string>(catalog.Select(d => d.Key), Keys)
			: new HashSet<string>(stored.Groups.SelectMany(g => g.Items).Select(i => i.Key).Concat(stored.Hidden), Keys);
		var arrivals = arranged.Groups.SelectMany(g => g.Items).Select(i => i.Key).Concat(arranged.Hidden)
			.Where(key => !known.Contains(key))
			.ToHashSet(Keys);
		return new NavLayoutDraft(arranged, stored?.Version ?? 0, arrivals);
	}

	/// <summary>The layout to save.</summary>
	public NavLayout ToLayout() => new(
		NavLayout.CurrentFormat,
		Version,
		[.. Groups.Select(g => new NavLayoutGroup(g.Id, string.IsNullOrWhiteSpace(g.Label) ? null : g.Label.Trim(), g.Mode,
			string.IsNullOrWhiteSpace(g.Icon) ? null : g.Icon.Trim(), [.. g.Items]))],
		[.. Hidden],
		[.. Tabs.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(Keys).Take(NavLayout.MaxMobileTabs)],
		NewEntries);

	/// <summary>After a save: the draft carries on from the version the server stored.</summary>
	public void Saved(NavLayout saved) => Version = saved.Version;

	/// <summary>The group holding <paramref name="key"/>, or <see cref="HiddenId"/> when it is hidden.</summary>
	public string? Where(string key) =>
		Hidden.Contains(key, Keys) ? HiddenId
		: Groups.FirstOrDefault(g => g.Items.Any(i => Keys.Equals(i.Key, key)))?.Id;

	/// <summary>
	/// Moves <paramref name="key"/> into <paramref name="groupId"/> (or <see cref="HiddenId"/>) at
	/// <paramref name="index"/>, or at the end when it is null or past it.
	/// </summary>
	/// <remarks>A link has nothing to hide it from but itself, so it is removed instead of hidden.</remarks>
	public void Move(string key, string groupId, int? index = null)
	{
		if (groupId == HiddenId && NavKeys.IsLink(key)) return;
		if (groupId != HiddenId && !Groups.Any(g => Keys.Equals(g.Id, groupId))) return;
		var item = Take(key) ?? new NavLayoutItem(key);
		if (groupId == HiddenId)
		{
			Hidden.Insert(Clamp(index, Hidden.Count), key);
			return;
		}
		var group = Groups.First(g => Keys.Equals(g.Id, groupId));
		group.Items.Insert(Clamp(index, group.Items.Count), item);
	}

	/// <summary>Moves <paramref name="key"/> to just before <paramref name="before"/>, in whichever group that is: a drop.</summary>
	public void MoveBefore(string key, string before)
	{
		if (Keys.Equals(key, before) || Where(before) is not { } groupId) return;
		if (groupId == HiddenId && NavKeys.IsLink(key)) return;
		var item = Take(key) ?? new NavLayoutItem(key);
		if (groupId == HiddenId)
		{
			Hidden.Insert(Hidden.FindIndex(h => Keys.Equals(h, before)), key);
			return;
		}
		var group = Groups.First(g => Keys.Equals(g.Id, groupId));
		group.Items.Insert(group.Items.FindIndex(i => Keys.Equals(i.Key, before)), item);
	}

	/// <summary>Moves <paramref name="key"/> one place up (-1) or down (+1) in its group.</summary>
	public void Nudge(string key, int by)
	{
		if (Groups.FirstOrDefault(g => g.Items.Any(i => Keys.Equals(i.Key, key))) is not { } group) return;
		var at = group.Items.FindIndex(i => Keys.Equals(i.Key, key));
		var to = at + by;
		if (to < 0 || to >= group.Items.Count) return;
		var item = group.Items[at];
		group.Items.RemoveAt(at);
		group.Items.Insert(to, item);
	}

	/// <summary>Moves a group one place up (-1) or down (+1).</summary>
	public void NudgeGroup(string id, int by)
	{
		var at = Groups.FindIndex(g => Keys.Equals(g.Id, id));
		var to = at + by;
		if (at < 0 || to < 0 || to >= Groups.Count) return;
		var group = Groups[at];
		Groups.RemoveAt(at);
		Groups.Insert(to, group);
	}

	/// <summary>Adds an empty group named <paramref name="label"/> at the end, with an id made from it; returns the id.</summary>
	public string AddGroup(string label)
	{
		var stem = new string(label.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
		if (stem.Length == 0) stem = "group";
		if (stem.Length > NavLayoutRules.MaxIdLength - 4) stem = stem[..(NavLayoutRules.MaxIdLength - 4)];
		var id = stem;
		for (var n = 2; Groups.Any(g => Keys.Equals(g.Id, id)); n++) id = $"{stem}-{n}";
		Groups.Add(new Group(id, label.Trim(), NavGroupModes.Inline, null, []));
		return id;
	}

	/// <summary>Removes a group. Its destinations move to the end of the first group left, or are hidden when it was the last.</summary>
	public void RemoveGroup(string id)
	{
		if (Groups.FirstOrDefault(g => Keys.Equals(g.Id, id)) is not { } group) return;
		Groups.Remove(group);
		foreach (var item in group.Items)
		{
			if (Groups.Count > 0) Groups[0].Items.Add(item);
			else if (!NavKeys.IsLink(item.Key)) Hidden.Add(item.Key);
		}
	}

	/// <summary>Adds a link at the end of <paramref name="groupId"/>; returns its key.</summary>
	public string AddLink(string groupId, string label, string href, string? icon, string? permission)
	{
		var stem = new string(label.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
		if (stem.Length == 0) stem = "link";
		var key = NavKeys.Link(stem);
		for (var n = 2; Where(key) is not null; n++) key = NavKeys.Link($"{stem}-{n}");
		Groups.First(g => Keys.Equals(g.Id, groupId)).Items.Add(new NavLayoutItem(key, label.Trim(), href.Trim(),
			string.IsNullOrWhiteSpace(icon) ? null : icon.Trim(), string.IsNullOrWhiteSpace(permission) ? null : permission.Trim()));
		Tabs.RemoveAll(t => Keys.Equals(t, key));
		return key;
	}

	/// <summary>Removes a link, or a key for something that no longer exists.</summary>
	public void Remove(string key)
	{
		Take(key);
		Tabs.RemoveAll(t => Keys.Equals(t, key));
	}

	private NavLayoutItem? Take(string key)
	{
		Hidden.RemoveAll(h => Keys.Equals(h, key));
		foreach (var group in Groups)
		{
			var at = group.Items.FindIndex(i => Keys.Equals(i.Key, key));
			if (at < 0) continue;
			var item = group.Items[at];
			group.Items.RemoveAt(at);
			return item;
		}
		return null;
	}

	private static int Clamp(int? index, int count) => index is { } i && i >= 0 && i <= count ? i : count;
}
