using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IServerStateStore"/>: the game-wide server state document. Not ported yet — every member
/// throws <see cref="NotImplementedException"/> until a later task. (<c>Migrate()</c> already ensures the
/// row exists; reading and updating it through this interface is what remains.)
/// </summary>
public sealed partial class LightningDatabase
{
	public ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
