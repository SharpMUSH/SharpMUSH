using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Reality;

/// <param name="Aliases">
/// Other names for layers, each to the layer it names: an imported game's own name for a level the import made a
/// differently named layer, such as RhostMUSH's <c>Real</c> for <c>normal</c>. Null when there are none.
/// </param>
public sealed record RealityConfiguration(int Version, bool Enabled, string[] Layers, Dictionary<string, string>? Aliases = null)
{
	public static RealityConfiguration Default => new(1, false, ["normal"]);

	/// <summary>The layer <paramref name="name"/> names, itself or through an alias, or null when it names none.</summary>
	public string? Layer(string name)
	{
		var lower = name.Trim().ToLowerInvariant();
		return Layers.Contains(lower) ? lower
			: Aliases?.GetValueOrDefault(lower) is { } aliased && Layers.Contains(aliased) ? aliased
			: null;
	}
}

public sealed record ObjectReality(int Version, DBRef Object, string[] Receive, string[] Transmit,
	Dictionary<string, string> Descriptions)
{
	public static ObjectReality Default(DBRef obj) => new(1, obj, ["normal"], ["normal"], []);
}

public interface IRealityPolicy
{
	ValueTask<bool> CanPerceiveAsync(DBRef receiver, DBRef target, CancellationToken ct = default);
	ValueTask<bool> IsEnabledAsync(CancellationToken ct = default);
	ValueTask<string?> DescriptionAttributeAsync(DBRef receiver, DBRef target, CancellationToken ct = default);
}

/// <summary>Optional request-scoped observation support; implementations keep receiver state only for one scan.</summary>
public interface IRealityObservationProvider
{
	ValueTask<Func<DBRef, CancellationToken, ValueTask<bool>>> ObserveAsync(DBRef receiver, CancellationToken ct = default);
}
