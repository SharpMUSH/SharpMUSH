using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class MoveService : IMoveService
{
	/// <summary>
	/// Checks if moving an object to a destination would create a containment loop.
	/// This prevents scenarios like: A contains B, B contains C, then moving A into C would create a loop.
	/// </summary>
	public async ValueTask<bool> WouldCreateLoop(AnySharpContent objectToMove, AnySharpContainer destination)
	{
		// If we're not moving into a container, no loop is possible
		if (!destination.IsThing && !destination.IsPlayer)
		{
			return false;
		}

		// Get the object's DBRef for comparison
		var objectDBRef = objectToMove.Object().DBRef;
		
		// Traverse up the containment chain from the destination
		// If we find the object we're trying to move, it would create a loop
		var current = destination;
		var visited = new HashSet<string> { current.Object().DBRef.ToString() };
		
		while (true)
		{
			// Check if the current container is the object we're trying to move
			if (current.Object().DBRef.Equals(objectDBRef))
			{
				return true; // Found a loop
			}
			
			// Get the location of the current container
			var location = await current.Match<ValueTask<AnySharpContainer>>(
				async player => await player.Location.WithCancellation(CancellationToken.None),
				room => ValueTask.FromResult<AnySharpContainer>(room),
				async thing => await thing.Location.WithCancellation(CancellationToken.None)
			);
			
			// If we've reached a room (rooms don't have locations other than themselves)
			// or if we've already visited this location (another way to detect loops),
			// we're done and no loop exists
			if (location.IsRoom || visited.Contains(location.Object().DBRef.ToString()))
			{
				return false;
			}
			
			// Continue traversing up the chain
			visited.Add(location.Object().DBRef.ToString());
			current = location;
		}
	}
}
