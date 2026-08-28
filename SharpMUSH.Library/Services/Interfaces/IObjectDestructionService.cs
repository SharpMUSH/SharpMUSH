using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The irrevocable half of object destruction — PennMUSH's <c>free_object()</c> and the
/// <c>clear_thing</c> / <c>clear_player</c> / <c>clear_room</c> / <c>clear_exit</c> helpers it
/// dispatches to (<c>src/destroy.c</c>).
/// </summary>
/// <remarks>
/// The reversible half — marking an object <c>GOING</c>, running its <c>@adestroy</c>, telling the
/// owner it is scheduled — is PennMUSH's <c>pre_destroy()</c> and stays in <c>@destroy</c> itself.
/// This service is what <c>@destroy</c> reaches for when the target is <i>already</i> <c>GOING</c>,
/// and what <c>@purge</c> reaches for on the second pass.
/// </remarks>
public interface IObjectDestructionService
{
	/// <summary>
	/// Objects that must never be destroyed, however they got marked — PennMUSH
	/// <c>special_object()</c>: <c>player_start</c>, <c>master_room</c>, <c>base_room</c>,
	/// <c>default_home</c>, God, and the <c>probate_judge</c>.
	/// </summary>
	bool IsSpecialObject(DBRef dbref);

	/// <summary>
	/// Tear an object down and remove it from the database for good.
	/// </summary>
	/// <remarks>
	/// Performs no permission checking — the caller has already decided this object dies. Runs the
	/// type-specific teardown (contents sent home, held exits destroyed, exits leading here relinked
	/// to their own source, possessions and channels chowned to probate), rehomes anything that
	/// called this object home, halts the object's queue, and then deletes it.
	/// <para>
	/// A special object (see <see cref="IsSpecialObject"/>) is refused and left intact, matching
	/// PennMUSH <c>purge()</c>, which undestroys one rather than freeing it.
	/// </para>
	/// </remarks>
	/// <returns><c>true</c> if the object was destroyed.</returns>
	ValueTask<bool> FreeObjectAsync(IMUSHCodeParser parser, AnySharpObject target,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// One purge pass over the whole database — PennMUSH <c>purge()</c> (<c>src/destroy.c</c>).
	/// </summary>
	/// <remarks>
	/// Objects marked <c>GOING</c> advance to <c>GOING_TWICE</c>; objects that already reached
	/// <c>GOING_TWICE</c> are freed. Everything is therefore destroyed on the <i>second</i> purge
	/// after <c>@destroy</c>, which is what keeps an accidental destroy recoverable by
	/// <c>@undestroy</c> for a full purge interval. Special objects are spared.
	/// </remarks>
	/// <returns>How many objects were freed.</returns>
	ValueTask<int> PurgeAsync(IMUSHCodeParser parser, CancellationToken cancellationToken = default);
}
