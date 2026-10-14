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
/// <param name="Order">Its place, lowest first.</param>
public sealed record SceneDetailInfo(string Key, string Label, string Hint, IReadOnlyList<string> Choices, bool Many, bool Listed, int Order)
{
	/// <summary>What splits the values of a detail that takes several.</summary>
	public const char Separator = '|';

	/// <summary>
	/// The scene fields kept in <c>Meta</c> that are not details: the title, the pitch and the picture have places
	/// of their own on the page.
	/// </summary>
	public static readonly IReadOnlySet<string> NotDetails = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "title", "summary", "image" };

	/// <summary>A key a player set that the catalogue does not list: named by its key.</summary>
	public static SceneDetailInfo Unlisted(string key) =>
		new(key, key.Length > 0 ? char.ToUpperInvariant(key[0]) + key[1..] : key, "", [], false, false, int.MaxValue);

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

	/// <summary>
	/// The details <paramref name="scene"/> has a value for, each with its values: the catalogue's in its order, then
	/// any other key a player set, by name.
	/// </summary>
	public IReadOnlyList<(SceneDetailInfo Detail, IReadOnlyList<string> Values)> Of(SceneSummary scene)
	{
		var catalogued = Details
			.Where(d => scene.Meta.ContainsKey(d.Key))
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
