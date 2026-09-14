using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// The state one run of <see cref="PennMUSHDatabaseConverter"/> accumulates as it walks a source
/// database: where each PennMUSH dbref landed, and what went wrong on the way.
/// </summary>
/// <remarks>
/// It is a parameter rather than a field on the converter because the converter is a singleton and
/// this is per-conversion state. Held on the instance, a second import starting while a first is
/// still running clears the mapping the first is midway through — and both then write the same
/// <see cref="Dictionary{TKey,TValue}"/> without synchronisation, which either throws or silently
/// resolves one import's attributes, parents and exit links through the other's objects.
/// </remarks>
internal sealed class PennMUSHConversionContext
{
	/// <summary>Where each source PennMUSH dbref ended up in the SharpMUSH database.</summary>
	public Dictionary<int, DBRef> DbrefMapping { get; } = [];

	/// <summary>Failures that cost the conversion an object, an attribute or a lock.</summary>
	public List<string> Errors { get; } = [];

	/// <summary>Things the conversion carried on past, but that the caller should see.</summary>
	public List<string> Warnings { get; } = [];

	/// <summary>
	/// Source values the conversion could not carry — a flag, power or warning SharpMUSH has no
	/// counterpart for, or one it does not allow on that type — keyed by what was dropped, with the
	/// source dbrefs that had it. A flag every player carries would otherwise be a warning per player.
	/// </summary>
	public SortedDictionary<string, List<int>> Unconverted { get; } = new(StringComparer.OrdinalIgnoreCase);

	public void NoteUnconverted(string what, int dbref)
	{
		if (!Unconverted.TryGetValue(what, out var dbrefs))
		{
			Unconverted[what] = dbrefs = [];
		}

		dbrefs.Add(dbref);
	}

	/// <summary>Turns <see cref="Unconverted"/> into one warning each.</summary>
	public void ReportUnconverted()
	{
		foreach (var (what, dbrefs) in Unconverted)
		{
			var sample = string.Join(" ", dbrefs.Take(10).Select(dbref => $"#{dbref}"));
			Warnings.Add($"{what}: not imported on {dbrefs.Count} object(s) ({sample}{(dbrefs.Count > 10 ? " ..." : "")})");
		}
	}
}
