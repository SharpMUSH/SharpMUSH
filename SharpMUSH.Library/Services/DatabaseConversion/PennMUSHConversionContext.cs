using SharpMUSH.Configuration.Options;
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
	/// <summary>The server that wrote the source (<see cref="PennMUSHDatabase.SourceName"/>), for the import's messages.</summary>
	public string SourceName { get; init; } = DatabaseDialect.PennMUSH.DisplayName();

	/// <summary>Where each source dbref ended up in the SharpMUSH database.</summary>
	public Dictionary<int, DBRef> DbrefMapping { get; } = [];

	/// <summary>
	/// The database options this conversion has already written, so a later write of the whole options
	/// record keeps them rather than restoring what the running configuration read before the reload.
	/// </summary>
	public DatabaseOptions? WrittenDatabaseOptions { get; set; }

	/// <summary>The packages uninstalled so their objects' numbers are free, to be installed again afterwards.</summary>
	public List<string> UninstalledPackages { get; } = [];

	/// <summary>Whether the uninstalled packages have been put back, so a failed import doesn't do it twice.</summary>
	public bool PackagesReinstalled { get; set; }

	/// <summary>Failures that cost the conversion an object, an attribute or a lock.</summary>
	public List<string> Errors { get; } = [];

	/// <summary>Things the conversion carried on past, but that the caller should see.</summary>
	public List<ImportWarning> Warnings { get; } = [];

	public void Warn(ImportWarningKind kind, string text) => Warnings.Add(new ImportWarning(kind, text));

	/// <summary>A warning an administrator has to act on, which the report puts first.</summary>
	public void WarnNeedsAction(ImportWarningKind kind, string text) => Warnings.Add(new ImportWarning(kind, text, NeedsAction: true));

	/// <summary>
	/// What every import of this kind does and the report says only for the record: the seeded objects it
	/// removed, the definitions SharpMUSH already had. Kept apart so they do not bury the warnings.
	/// </summary>
	public List<string> Notes { get; } = [];

	/// <summary>Told how many of the source's objects the running phase has worked through.</summary>
	public Action<int>? Advance { get; set; }

	/// <summary>
	/// Source values the conversion could not carry — a flag, power or warning SharpMUSH has no
	/// counterpart for, or one it does not allow on that type — keyed by what was dropped, with the
	/// source dbrefs that had it. A flag every player carries would otherwise be a warning per player.
	/// </summary>
	private SortedDictionary<string, (ImportWarningKind Kind, List<int> Dbrefs)> Unconverted { get; } = new(StringComparer.OrdinalIgnoreCase);

	public void NoteUnconverted(ImportWarningKind kind, string what, int dbref)
	{
		if (!Unconverted.TryGetValue(what, out var unconverted))
		{
			Unconverted[what] = unconverted = (kind, []);
		}

		unconverted.Dbrefs.Add(dbref);
	}

	/// <summary>Turns <see cref="Unconverted"/> into one warning each.</summary>
	public void ReportUnconverted()
	{
		foreach (var (what, (kind, dbrefs)) in Unconverted)
		{
			var sample = string.Join(" ", dbrefs.Take(10).Select(dbref => $"#{dbref}"));
			Warn(kind, $"{what}: not imported on {dbrefs.Count} object(s) ({sample}{(dbrefs.Count > 10 ? " ..." : "")})");
		}
	}
}
