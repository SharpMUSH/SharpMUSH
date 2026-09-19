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
}