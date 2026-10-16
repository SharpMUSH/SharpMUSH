namespace SharpMUSH.Client.Models;

/// <summary>
/// One scene detail as <c>GET api/scenes/details</c> sends it (the Scene plugin's <c>SceneDetail</c>): a key of a
/// scene's <c>Meta</c> that the game names and explains, such as the verse or canon it is set in.
/// </summary>
/// <param name="Key">The detail's key, lower case: the <c>Meta</c> key its value is kept under.</param>
/// <param name="Label">Its name for people.</param>
/// <param name="Hint">One line saying what goes in it, or empty.</param>
/// <param name="Choices">The values it allows; empty when any text will do.</param>
/// <param name="Many">Whether a scene may give it several values, split by <see cref="Separator"/>.</param>
/// <param name="Listed">Whether it shows on the scene's card in lists.</param>
/// <param name="Tone">The tone its values show in (<c>warning</c>), or empty for the text's own colour.</param>
/// <param name="Order">Its place, lowest first.</param>
/// <param name="Hidden">Whether it is kept for code only and never shown.</param>
/// <param name="Sort">The key of the detail that orders scenes by this one, or empty when its own value does.</param>
public sealed record SceneDetailInfo(string Key, string Label, string Hint, IReadOnlyList<string> Choices, bool Many, bool Listed,
	string Tone, int Order, bool Hidden = false, string Sort = "")
{
	/// <summary>What splits the values of a detail that takes several.</summary>
	public const char Separator = '|';

	/// <summary>
	/// The scene fields kept in <c>Meta</c> that are not details: the title, pitch, picture, joining policy and player cap
	/// have places of their own on the page.
	/// </summary>
	public static readonly IReadOnlySet<string> NotDetails = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "title", "summary", "image", "joinpolicy", "playercap" };

	/// <summary>A key a player set that the catalogue does not list: named by its key.</summary>
	public static SceneDetailInfo Unlisted(string key) =>
		new(key, key.Length > 0 ? char.ToUpperInvariant(key[0]) + key[1..] : key, "", [], false, false, "", int.MaxValue);

	/// <summary>The values <paramref name="value"/> holds, split when the detail takes several.</summary>
	public IReadOnlyList<string> Values(string value) =>
		Many
			? [.. value.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
			: value.Trim() is { Length: > 0 } one ? [one] : [];
}

/// <summary>A <c>DETAIL`</c> attribute the plugin could not read as a detail, and why.</summary>
public sealed record SceneDetailProblem(string Key, string Reason);

/// <summary><c>GET api/scenes/details</c>: the details in display order, and the attributes that did not read as one.</summary>
public sealed record SceneDetailCatalogue(IReadOnlyList<SceneDetailInfo> Details, IReadOnlyList<SceneDetailProblem> Problems)
{
	public static readonly SceneDetailCatalogue Empty = new([], []);

	/// <summary>The first detail that orders scenes by a key of its own, such as the IC date, or null when none does.</summary>
	public SceneDetailInfo? Chronology => Details.FirstOrDefault(d => !d.Hidden && d.Sort.Length > 0);

	/// <summary>
	/// The details <paramref name="scene"/> has a value for, each with its values: the catalogue's in its order, then
	/// any other key a player set, by name. A hidden detail is never among them.
	/// </summary>
	public IReadOnlyList<(SceneDetailInfo Detail, IReadOnlyList<string> Values)> Of(SceneSummary scene)
	{
		var catalogued = Details
			.Where(d => !d.Hidden && scene.Meta.ContainsKey(d.Key))
			.Select(d => (Detail: d, Values: d.Values(scene.Meta[d.Key])))
			.Where(d => d.Values.Count > 0);

		var listed = Details.Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var unlisted = scene.Meta
			.Where(m => !listed.Contains(m.Key) && !SceneDetailInfo.NotDetails.Contains(m.Key) && !string.IsNullOrWhiteSpace(m.Value))
			.OrderBy(m => m.Key, StringComparer.Ordinal)
			.Select(m => (Detail: SceneDetailInfo.Unlisted(m.Key), m.Value))
			.Select(d => (d.Detail, Values: d.Detail.Values(d.Value)));

		return [.. catalogued, .. unlisted];
	}
}

/// <summary>A scene list narrowed to the scenes giving detail <paramref name="Key"/> the value <paramref name="Value"/>.</summary>
public sealed record SceneDetailMatch(string Key, string Value)
{
	/// <summary>The query <c>api/scenes</c> and the <c>/scenes</c> page read it from.</summary>
	public string Query => $"detail={Uri.EscapeDataString(Key)}&value={Uri.EscapeDataString(Value)}";
}

/// <summary>One value a listed detail has, and how many scenes the viewer may see give it.</summary>
public sealed record SceneDetailValue(string Value, int Scenes);

/// <summary><c>GET api/scenes/details/values</c>: one listed detail's values, most scenes first.</summary>
public sealed record SceneDetailValues(string Key, IReadOnlyList<SceneDetailValue> Values);
