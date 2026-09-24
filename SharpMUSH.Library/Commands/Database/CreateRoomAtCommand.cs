using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// <see cref="CreateRoomCommand"/> at a dbref the caller names — PennMUSH's
/// <c>make_first_free_wrapper</c> (<c>src/destroy.c:928</c>) ahead of <c>do_dig</c>'s
/// <c>new_object()</c> (<c>create.c:480-490</c>). A command of its own rather than an option on
/// <see cref="CreateRoomCommand"/> because it can fail for a reason the caller has to report:
/// <c>@dig name=,,#42</c> either lands on <c>#42</c> or digs nothing.
/// </summary>
/// <param name="Requested">The dbref the new room must have</param>
/// <param name="ModifiedTime">Modification time in Unix milliseconds, or <c>null</c> for now — a clone
/// keeps the original's (<c>create.c:653-655</c>).</param>
public record CreateRoomAtCommand(DBRef Requested, string Name, SharpPlayer Creator, long? ModifiedTime = null)
	: ICommand<Result<DBRef>>, ICacheInvalidating, ICacheInvalidatingByResult<Result<DBRef>>
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Creator.Object.DBRef)];

	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheTags.ObjectList,
		Definitions.CacheTags.RoomList
	];

	/// <summary>
	/// The requested dbref was a hole, so every reader that looked it up while it was one cached a
	/// miss. A refusal wrote nothing and invalidates nothing.
	/// </summary>
	public string[] CacheKeysFor(Result<DBRef> result) => result switch
	{
		DBRef created => [Definitions.CacheKeys.Object(created)],
		Error<string> => []
	};
}
