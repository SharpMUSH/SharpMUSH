using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// The link PennMUSH keeps as a pseudo-dbref in an exit's destination or a room's drop-to: <c>HOME</c>
/// (<c>-3</c>) and, for an exit, <c>AMBIGUOUS</c> (<c>-2</c>, a variable exit). SharpMUSH writes no relation
/// for them and records the keyword in <see cref="Attribute"/> instead. <c>@link</c>, <c>loc()</c>, moving
/// through an exit, dropping in a room and every importer read and write the same attribute.
/// </summary>
public static class LinkTypes
{
	/// <summary>The attribute holding the keyword.</summary>
	public const string Attribute = "_LINKTYPE";

	/// <summary>A variable exit: it works out where it leads from its <c>DESTINATION</c> attribute.</summary>
	public const string Variable = "variable";

	/// <summary>An exit or drop-to leading to the home of whatever goes through it.</summary>
	public const string Home = "home";

	/// <summary>The pseudo-dbref PennMUSH, TinyMUX and RhostMUSH store for <c>HOME</c>.</summary>
	public const int HomeDbref = -3;

	/// <summary>The pseudo-dbref PennMUSH, TinyMUX and RhostMUSH store for a variable link (<c>AMBIGUOUS</c>).</summary>
	public const int VariableDbref = -2;

	/// <summary>
	/// The keyword recorded on <paramref name="dbref"/>, lower-cased, or null when it has none. Read without
	/// anyone's permissions, as PennMUSH reads <c>Location()</c>.
	/// </summary>
	public static async ValueTask<string?> OfAsync(IMediator mediator, DBRef dbref)
	{
		await foreach (var attribute in mediator.CreateStream(new GetAttributeQuery(dbref, [Attribute])))
		{
			var keyword = attribute.Value.ToPlainText().Trim();
			return string.IsNullOrEmpty(keyword) ? null : keyword.ToLowerInvariant();
		}

		return null;
	}

	/// <summary>
	/// Where <paramref name="room"/>'s drop-to sends <paramref name="item"/>: the item's own home for a drop-to
	/// of <c>HOME</c> (PennMUSH's <c>safe_tel</c> resolving <c>HOME</c>), otherwise the room it names, if any.
	/// </summary>
	public static async ValueTask<AnyOptionalSharpContainer> DropToForAsync(IMediator mediator, SharpRoom room,
		AnySharpContent item)
		=> await OfAsync(mediator, room.Object.DBRef) == Home
			? await item.Home()
			: await room.Location.WithCancellation(CancellationToken.None);

	/// <summary>Whether <paramref name="room"/>'s drop-to is <c>HOME</c>.</summary>
	public static async ValueTask<bool> DropsHomeAsync(IMediator mediator, SharpRoom room)
		=> await OfAsync(mediator, room.Object.DBRef) == Home;
}
