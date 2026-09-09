using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IMoveService
{
	/// <summary>
	/// Checks if moving an object to a destination would create a containment loop.
	/// A loop occurs when object A contains B, B contains C, and we try to move A into C,
	/// creating a circular containment chain.
	/// </summary>
	/// <param name="objectToMove">The object being moved</param>
	/// <param name="destination">The destination container</param>
	/// <returns>True if moving would create a loop, false otherwise</returns>
	ValueTask<bool> WouldCreateLoop(AnySharpContent objectToMove, AnySharpContainer destination);

	/// <summary>
	/// The outermost room containing <paramref name="obj"/>, walking out through containers.
	/// PennMUSH <c>absolute_room</c> (<c>src/utils.c:794</c>). Null when the chain exceeds
	/// <c>Limit.MaxDepth</c> or ends somewhere that is not a room — Penn's "too many containers"
	/// and void cases.
	/// </summary>
	ValueTask<AnySharpContainer?> AbsoluteRoom(AnySharpObject obj);

	/// <summary>
	/// Sends an object somewhere and fires every triad the move produces, in PennMUSH's order.
	/// PennMUSH <c>moveit</c> (<c>src/move.c:66</c>).
	/// </summary>
	/// <param name="parser">Parser context the triads evaluate and queue under.</param>
	/// <param name="what">The object being moved.</param>
	/// <param name="where">The destination container.</param>
	/// <param name="noMoveMsgs">Suppresses the <c>MOVE</c>/<c>OMOVE</c>/<c>AMOVE</c> triad only.</param>
	/// <param name="enactor">The object that caused the move.</param>
	/// <param name="cause">What caused the move, for events.</param>
	ValueTask MoveIt(
		IMUSHCodeParser parser,
		AnySharpContent what,
		AnySharpContainer where,
		bool noMoveMsgs,
		DBRef enactor,
		string cause);

	/// <summary>
	/// Checks if a move is permitted based on locks and permissions.
	/// </summary>
	/// <param name="who">The object attempting the move</param>
	/// <param name="objectToMove">The object being moved</param>
	/// <param name="destination">The destination container</param>
	/// <returns>True if the move is permitted</returns>
	ValueTask<bool> CanMoveAsync(AnySharpObject who, AnySharpContent objectToMove, AnySharpContainer destination);

	/// <summary>
	/// Calculates the cost of moving an object.
	/// </summary>
	/// <param name="objectToMove">The object being moved</param>
	/// <param name="destination">The destination container</param>
	/// <returns>The cost in pennies/quota</returns>
	ValueTask<int> CalculateMoveCostAsync(AnySharpContent objectToMove, AnySharpContainer destination);

	/// <summary>
	/// Checks if a player is "in the void" (location has DBRef &lt; 0 or is unresolvable).
	/// If so, moves the player to their home location, or the fallback if home is also invalid.
	/// Mirrors PennMUSH's void detection in process_command() (src/bsd.c).
	/// </summary>
	/// <param name="player">The player object to check</param>
	/// <param name="fallbackHome">A fallback DBRef (e.g., PlayerStart) if the player's home is also invalid</param>
	/// <returns>True if the player was rescued from the void, false if location was valid</returns>
	ValueTask<bool> RescueFromVoidAsync(AnySharpObject player, DBRef fallbackHome);
}