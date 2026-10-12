using System.Collections.ObjectModel;

namespace SharpMUSH.CodeAnalysis.Catalog;

/// <summary>Transport-neutral metadata. No evaluation delegates or mutable runtime attributes escape.</summary>
public sealed class AnalysisCatalogEntry(
	string name, string kind, string origin, int minArgs, int? maxArgs,
	IEnumerable<string> aliases, IEnumerable<string> parameterNames,
	IEnumerable<string> switches, string flags, IEnumerable<string> restrictions)
{
	public string Name { get; } = name;
	public string Kind { get; } = kind;
	public string Origin { get; } = origin;
	public int MinArgs { get; } = minArgs;
	/// <summary>Null denotes unbounded arity, without an enormous parameter array.</summary>
	public int? MaxArgs { get; } = maxArgs;
	public IReadOnlyList<string> Aliases { get; } = Array.AsReadOnly(aliases.ToArray());
	public IReadOnlyList<string> ParameterNames { get; } = Array.AsReadOnly(parameterNames.ToArray());
	public IReadOnlyList<string> Switches { get; } = Array.AsReadOnly(switches.ToArray());
	public string Flags { get; } = flags;
	public IReadOnlyList<string> Restrictions { get; } = Array.AsReadOnly(restrictions.ToArray());
}

/// <summary>A detached immutable catalog. A live provider must filter before constructing this payload.</summary>
public sealed class AnalysisCatalogSnapshot(
	string epoch, string revision, string source, IEnumerable<AnalysisCatalogEntry> entries)
{
	public int SchemaVersion => 1;
	public string Epoch { get; } = epoch;
	public string Revision { get; } = revision;
	public string Source { get; } = source;
	public ReadOnlyCollection<AnalysisCatalogEntry> Entries { get; } = Array.AsReadOnly(entries.ToArray());
}
