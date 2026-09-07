using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Library.Plugins.Storage;

/// <summary>
/// Host-shared seam that exposes the Lightning provider's store to a storage plugin (the Scene plugin's
/// Lightning backend, arriving in Task 19). Carries no subsystem concept: a plugin reads and writes
/// through the same <see cref="ITx"/>/<see cref="TableDef"/> types the provider uses on itself, and
/// opens its own tables on first use rather than the provider's catalogue knowing about them.
/// </summary>
/// <remarks>
/// This interface lives in <c>SharpMUSH.Library</c> (host-shared) so host and plugin unify on the same
/// <see cref="System.Type"/> across the plugin <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// </remarks>
public interface ILightningStorageAccessor
{
	/// <summary>Runs <paramref name="read"/> inside a read-only transaction on the calling thread.</summary>
	T Read<T>(Func<ITx, T> read);

	/// <summary>
	/// Enqueues <paramref name="job"/> to run on the dedicated writer thread, inside one committed write
	/// transaction. Blocks the caller until the writer thread runs the job (via the returned task), so
	/// <b>must not be called from inside another write job</b> — a job already running on the writer
	/// thread that calls this would wait forever on itself.
	/// </summary>
	ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default);

	/// <summary>Streams every entry under <paramref name="prefix"/> in <paramref name="table"/>, paging so no read transaction is held across the consumer's awaits.</summary>
	IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default);

	/// <summary>
	/// Opens (creating if needed) a plugin-owned table not in the provider's own catalogue, and appends
	/// it to the store's handle map so subsequent reads and writes can use the returned <see cref="TableDef"/>.
	/// Safe to call more than once for the same name; the existing definition is returned. The open itself
	/// runs on the dedicated writer thread and blocks the caller until it completes, so — like
	/// <see cref="WriteAsync{T}"/> — this <b>must not be called from inside a write job</b>.
	/// </summary>
	TableDef OpenTable(string name, bool duplicates);
}
