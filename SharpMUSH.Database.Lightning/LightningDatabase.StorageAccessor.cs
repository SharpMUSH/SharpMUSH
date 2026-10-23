using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Database.Lightning.Store;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Plugins.Storage.ILightningStorageAccessor"/>: the host-shared seam a storage
/// plugin (the Scene plugin's Lightning backend) reads and writes the store through. Every
/// member forwards to <see cref="Store"/>.
/// </summary>
public partial class LightningDatabase
{
	public T Read<T>(Func<ITx, T> read) => Store.Read(read);

	public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) => Store.WriteAsync(job, ct);

	public IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default)
		=> Store.RangeAsync(table, prefix, pageSize, ct);

	public TableDef OpenTable(string name, bool duplicates) => Store.OpenTable(name, duplicates);
}
