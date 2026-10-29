using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Navigation;

namespace SharpMUSH.Library;

/// <summary>
/// The game's portal navigation layout: one document, saved with optimistic concurrency so two staff editing at once
/// cannot overwrite each other unseen. System data, never visible to softcode, travels with backups.
/// </summary>
public interface IPortalNavigationStore
{
	/// <summary>The stored layout, or <see cref="NotFound"/> while staff have never saved one.</summary>
	ValueTask<Found<NavLayout>> GetNavLayoutAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stores <paramref name="layout"/> as the next version when the stored one is still at
	/// <paramref name="expectedVersion"/> (0 for none yet); a conflict otherwise.
	/// </summary>
	ValueTask<NavLayoutSaveResult> SaveNavLayoutAsync(NavLayout layout, long expectedVersion,
		CancellationToken cancellationToken = default);

	/// <summary>Removes the stored layout, so the game is back on the default. Does nothing when there is none.</summary>
	ValueTask ResetNavLayoutAsync(CancellationToken cancellationToken = default);
}
