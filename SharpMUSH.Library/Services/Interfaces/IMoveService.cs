using SharpMUSH.Library.DiscriminatedUnions;

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
}