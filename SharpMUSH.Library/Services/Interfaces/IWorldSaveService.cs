using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Pushes every change already committed to the world onto the disk. The save behind PennMUSH's dump events
/// (<c>compatibility_events</c>): SharpMUSH commits each change as it is made, so there is nothing to write out,
/// only the provider's own buffers to flush. Under a sync mode that syncs every commit it does nothing.
/// </summary>
public interface IWorldSaveService
{
	/// <summary>Flushes the world to disk, or says why it could not.</summary>
	ValueTask<Result<Success>> SaveAsync(CancellationToken cancellationToken = default);
}
