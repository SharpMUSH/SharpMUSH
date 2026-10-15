namespace SharpMUSH.Library.Models;

/// <summary>
/// <c>@attribute/watch &lt;attribute&gt;=&lt;pattern&gt;</c>: every set, clear or wipe of a copy of a watched attribute,
/// on any object, runs the event handler's attributes whose names match the pattern. The pattern is one or more
/// attribute wildcards (<c>*</c> within a level, <c>**</c> across levels, as <c>lattr()</c> takes them), separated by
/// spaces; an attribute matching any of them runs once.
/// </summary>
public static class AttributeWatch
{
	/// <summary>
	/// The pattern <c>@attribute/watch &lt;attribute&gt;</c> gives when none is named: <c>WATCH`&lt;attribute&gt;</c> and every
	/// attribute one level below it, so each package can keep its own handler (<c>WATCH`HP`GAUGE</c>).
	/// </summary>
	public static string DefaultPattern(string attribute)
	{
		var name = attribute.ToUpperInvariant();
		return $"WATCH`{name} WATCH`{name}`*";
	}

	/// <summary>The wildcards in <paramref name="pattern"/>, upper case, each once.</summary>
	public static IReadOnlyList<string> Wildcards(string pattern)
		=> [.. pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(wildcard => wildcard.ToUpperInvariant())
			.Distinct(StringComparer.Ordinal)];

	/// <summary>
	/// <paramref name="pattern"/> as stored: its wildcards upper case, separated by single spaces, or null when it names
	/// none (watching is off).
	/// </summary>
	public static string? Normalize(string pattern)
		=> Wildcards(pattern) is { Count: > 0 } wildcards ? string.Join(' ', wildcards) : null;

	/// <summary>
	/// The names %0-%4 are also given under, in the handler: the object's objid, the attribute's full name, its old
	/// value, its new value and the enactor's objid.
	/// </summary>
	public static IReadOnlyList<string> ArgumentNames { get; } = ["objid", "attribute", "old", "new", "enactor"];
}

/// <summary>
/// The watched attribute entries, by full name (upper case), each with its pattern: what a write checks its names
/// against, read once per write from the cache rather than from the table.
/// </summary>
public sealed class WatchedAttributes
{
	/// <summary>No attribute is watched.</summary>
	public static WatchedAttributes None { get; } = new(new Dictionary<string, string>());

	public WatchedAttributes(IReadOnlyDictionary<string, string> patterns)
	{
		Patterns = patterns.ToDictionary(p => p.Key.ToUpperInvariant(), p => p.Value, StringComparer.Ordinal);
		Names = Patterns.Keys.ToHashSet(StringComparer.Ordinal);
	}

	/// <summary>Each watched attribute's pattern, by its full name in upper case.</summary>
	public IReadOnlyDictionary<string, string> Patterns { get; }

	/// <summary>The watched attributes' full names, upper case.</summary>
	public IReadOnlySet<string> Names { get; }
}
