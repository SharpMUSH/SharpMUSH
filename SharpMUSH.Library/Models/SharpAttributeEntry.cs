using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpAttributeEntry
{
	[JsonIgnore]
	public string? Id { get; set; }

	public required string Name { get; set; }

	public required string[] DefaultFlags { get; set; }

	public string? Limit { get; set; }

	public string[]? Enum { get; set; }

	/// <summary>
	/// The character <see cref="Enum"/>'s choices were given with, <c>@attribute/enum &lt;delim&gt; name=…</c>.
	/// A value holding it names no choice, so a choice may contain spaces only under another delimiter.
	/// </summary>
	public char EnumDelimiter { get; set; } = ' ';

	/// <summary>
	/// The index on this attribute (<c>@attribute/index</c>), or null for none: its options, and whether it is
	/// ready or still being built.
	/// </summary>
	public AttributeIndexDefinition? Index { get; set; }

	/// <summary>
	/// What <c>@attribute/watch</c> made of the attribute, or null when it is not watched: one or more wildcard
	/// patterns, separated by spaces, naming the event handler's attributes that run whenever a copy of this attribute
	/// on any object is set, cleared or wiped (see <see cref="AttributeWatch"/>).
	/// </summary>
	public string? Watch { get; set; }
}

/// <summary>An attribute entry's index, as the entry stores it.</summary>
/// <param name="Options">How values are ordered and which copies are kept.</param>
/// <param name="Ready">
/// Whether every existing copy has been indexed. Until then the index answers nothing (queries say
/// <c>#-1 INDEX BUILDING</c>), though every write already keeps it.
/// </param>
/// <param name="ResumeAt">While building, the dbref the next batch of the build starts at.</param>
public sealed record AttributeIndexDefinition(AttributeIndexOptions Options, bool Ready, long ResumeAt);