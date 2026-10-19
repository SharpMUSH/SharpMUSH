using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using System.Buffers.Binary;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Plugins.Storage.ILightningStorageAccessor"/>: the host-shared seam a storage
/// plugin (the Scene plugin's Lightning backend) reads and writes the store through. Every
/// ordinary-host member forwards to <see cref="Store"/>. Issued atomic fixtures additionally guard
/// raw mutations and table creation inside their owning writer jobs.
/// </summary>
public partial class LightningDatabase
{
	public T Read<T>(Func<ITx, T> read) => Store.Read(read);

	public async ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default)
	{
		using var admission = EnterFixtureMutation(SemanticFixtureMutation.Raw);
		return await WriteFixtureAsync(admission, tx =>
		{
			if (admission?.Phase != SemanticFixturePhase.Active) return job(tx);
			var guarded = new GuardedSemanticFixtureTx(tx);
			var result = job(guarded);
			guarded.RequireNotPoisoned();
			if (guarded.Touched) AdvanceFixtureRawEpoch(tx);
			return result;
		}, ct);
	}

	public IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default)
		=> Store.RangeAsync(table, prefix, pageSize, ct);

	public TableDef OpenTable(string name, bool duplicates)
	{
		if (_fixtureLease is null) return Store.OpenTable(name, duplicates);
		using var admission = EnterFixtureMutation(SemanticFixtureMutation.Raw);
		return Store.OpenTableOwned(name, duplicates, (tx, creating) =>
		{
			RequireFixtureMutation(tx, admission);
			if (admission?.Phase != SemanticFixturePhase.Active) return;
			if (GuardedSemanticFixtureTx.Protected(name)) throw new WikiFixtureMutationException(WikiFixtureMutationFailure.UnsupportedOperation);
			if (creating) AdvanceFixtureRawEpoch(tx);
		});
	}

	private static void AdvanceFixtureRawEpoch(ITx tx)
	{
		var budget = new NativeReadBudget(2, 1024, 4096, CancellationToken.None);
		var epoch = 0UL;
		if (((IBoundedReadTx)tx).ReadValue(Tables.Meta, AtomicWikiLayout.RawEpochKey, budget) is NativeReadValue value)
		{
			using (value)
			{
				if (value.Value.Length != 8) throw SemanticFixtureCapability.Unavailable();
				epoch = BinaryPrimitives.ReadUInt64BigEndian(value.Value);
			}
		}
		if (epoch == ulong.MaxValue) throw SemanticFixtureCapability.Unavailable();
		Span<byte> bytes = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, epoch + 1);
		tx.Put(Tables.Meta, AtomicWikiLayout.RawEpochKey, bytes);
	}
}
