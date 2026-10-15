using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

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
	/// The warnings <paramref name="target"/> earns under <paramref name="warnings"/>, as data rather than
	/// notifications, whatever the checker's or the owner's own <c>@warnings</c>. Nothing for a GOING object
	/// or one whose owner has set NO_WARN on it or on themselves.
	/// </summary>
	/// <param name="checker">Whose view of the attributes the checks read.</param>
	/// <param name="target">The object to check.</param>
	/// <param name="warnings">The checks to run.</param>
	IAsyncEnumerable<ObjectWarning> FindWarningsAsync(AnySharpObject checker, AnySharpObject target, WarningType warnings);

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

/// <summary>One problem a check found: the warning's name, as <c>@warnings</c> spells it, and what is wrong.</summary>
public readonly record struct ObjectWarning(string Name, string Message);
