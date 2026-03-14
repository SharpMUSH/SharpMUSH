using OneOf;
using OneOf.Types;
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
	/// Executes a complete move operation including permission checks, cost calculation,
	/// hook triggering, and notifications.
	/// </summary>
	/// <param name="parser">Parser context for executing hooks</param>
	/// <param name="objectToMove">The object being moved</param>
	/// <param name="destination">The destination container</param>
	/// <param name="enactor">The object initiating the move (may be null for system moves)</param>
	/// <param name="cause">The cause of the move (e.g., "teleport", "get", "drop")</param>
	/// <param name="silent">If true, suppress notifications and some hooks</param>
	/// <returns>Success if move completed, Error with message if failed</returns>
	ValueTask<OneOf<Success, Error<string>>> ExecuteMoveAsync(
		IMUSHCodeParser parser,
		AnySharpContent objectToMove,
		AnySharpContainer destination,
		DBRef? enactor = null,
		string cause = "move",
		bool silent = false);

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
}