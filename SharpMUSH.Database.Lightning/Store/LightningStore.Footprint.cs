using LightningDB;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	/// <summary>One table's pages (branch, leaf and overflow) and entries, and the environment's page size.</summary>
	public readonly record struct Footprint(long Pages, long Entries, long PageSize);

	/// <summary>
	/// <paramref name="table"/>'s pages and entries from LMDB's own statistics: no data is read, only the table's
	/// B-tree header. A read-side operation, so writes continue.
	/// </summary>
	public Footprint TableFootprint(TableDef table)
	{
		EnterNative();
		try
		{
			using var tx = _env.BeginTransaction(TransactionBeginFlags.ReadOnly);
			var stats = tx.GetStats(_tables[table]);
			return new Footprint(stats.BranchPages + stats.LeafPages + stats.OverflowPages, stats.Entries, _env.EnvironmentStats.PageSize);
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	/// <summary>
	/// How many entries of <paramref name="table"/> have a key starting with <paramref name="prefix"/>, and the bytes
	/// their keys take: one cursor walk over the mapped pages, copying nothing out.
	/// </summary>
	public (long Entries, long KeyBytes) PrefixFootprint(TableDef table, byte[] prefix)
	{
		EnterNative();
		try
		{
			using var tx = _env.BeginTransaction(TransactionBeginFlags.ReadOnly);
			using var cursor = tx.CreateCursor(_tables[table]);
			var positioned = prefix.Length == 0 ? cursor.First().resultCode : cursor.SetRange(prefix);
			if (!FoundNative(positioned, "prefix footprint seek"))
			{
				return (0, 0);
			}

			long entries = 0, bytes = 0;
			do
			{
				var (code, key, _) = cursor.GetCurrent();
				if (!FoundNative(code, "prefix footprint current") || !Keys.StartsWith(key.AsSpan(), prefix)) break;
				entries++;
				bytes += key.AsSpan().Length;
			} while (FoundNative(cursor.Next().resultCode, "prefix footprint next"));

			return (entries, bytes);
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}
}
