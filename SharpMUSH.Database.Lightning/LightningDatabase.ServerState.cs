using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IServerStateStore"/>: the game-wide server state document. Reads and writes the same
/// fixed <c>state</c> row that <c>Migrate()</c>'s <see cref="Migration.LightningMigration"/> partial
/// (<c>EnsureServerState</c>) guarantees exists after migration.
/// </summary>
public sealed partial class LightningDatabase
{
	private static readonly byte[] ServerStateKey = Keys.Str("state");

	public ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
	{
		var record = Store.Read(tx => tx.TryGet(Tables.State, ServerStateKey, out var v)
			? Codec.Deserialize<ServerStateRecord>(v)
			: new ServerStateRecord());
		return ValueTask.FromResult(new SharpServerState { SetupCompleted = record.SetupCompleted });
	}

	public async ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => tx.Put(Tables.State, ServerStateKey, Codec.Serialize(new ServerStateRecord { SetupCompleted = value })), cancellationToken);
}
