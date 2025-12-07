using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for checking topology and integrity warnings on MUSH objects
/// </summary>
public interface IWarningService
{
	/// <summary>
	/// Check warnings on a specific object
	/// </summary>
	/// <param name="checker">The player performing the check</param>
	/// <param name="target">The object to check</param>
	/// <returns>True if warnings were found</returns>
	Task<bool> CheckObjectAsync(AnySharpObject checker, AnySharpObject target);

	/// <summary>
	/// Check warnings on all objects owned by a player
	/// </summary>
	/// <param name="owner">The player whose objects to check</param>
	/// <returns>Number of warnings found</returns>
	Task<int> CheckOwnedObjectsAsync(AnySharpObject owner);

	/// <summary>
	/// Check warnings on all objects in the database (admin only)
	/// Notifies connected owners of warnings found
	/// </summary>
	/// <returns>Number of objects checked</returns>
	Task<int> CheckAllObjectsAsync();
}
