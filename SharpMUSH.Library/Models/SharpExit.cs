using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpExit
{
	[JsonIgnore]
	public string? Id { get; set; }

	public string[]? Aliases { get; set; }

	public required SharpObject Object { get; set; }

	/// <summary>
	/// The container the exit sits in — PennMUSH's <c>Source()</c>. Usually a room, but exits can also
	/// live on a player or a thing. Backed by the AtLocation edge, which is also what makes the exit show
	/// up in that container's contents.
	/// </summary>
	[JsonIgnore]
	public required AsyncRelation<AnySharpContainer> Location { get; set; }

	/// <summary>
	/// Where the exit leads — PennMUSH's <c>Destination()</c>. Backed by the HasHome edge, which
	/// <c>@link</c> writes and <c>@unlink</c> removes, so a freshly <c>@open</c>ed or <c>@unlink</c>ed
	/// exit has none.
	/// </summary>
	[JsonIgnore]
	public required AsyncRelation<AnyOptionalSharpContainer> Home { get; set; }
}