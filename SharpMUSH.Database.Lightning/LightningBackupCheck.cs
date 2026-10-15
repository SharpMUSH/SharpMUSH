using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Reads a backup of the world through, read-only: every entry of every core table, so a page LMDB cannot read
/// fails the check now rather than on the day the copy is restored. The copy is never written to.
/// </summary>
public static class LightningBackupCheck
{
	public static Result<WorldBackupCheck> Check(WorldBackup backup, CancellationToken cancellationToken = default)
	{
		try
		{
			using var env = new LightningEnvironment(backup.Path, new EnvironmentConfiguration { MaxDatabases = 256 });
			env.Open(EnvironmentOpenFlags.ReadOnly | EnvironmentOpenFlags.NoLock);
			using var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly);

			var entries = 0L;
			foreach (var table in Tables.All)
			{
				using var db = tx.OpenDatabase(table.Name, new DatabaseConfiguration { Flags = LightningStore.FlagsFor(table) });
				entries += ReadThrough(tx, db, table.Name, cancellationToken);
			}

			return new WorldBackupCheck(backup, Tables.All.Count, entries);
		}
		catch (LightningException ex)
		{
			return new Error<string>($"backup {backup.Name} failed the check: {ex.Message}");
		}
		catch (LightningStoreException ex)
		{
			return new Error<string>($"backup {backup.Name} failed the check: {ex.Message}");
		}
	}

	/// <summary>Walks every entry of one table, which reads every page it is stored on.</summary>
	private static long ReadThrough(LightningTransaction tx, LightningDB.LightningDatabase db, string table,
		CancellationToken cancellationToken)
	{
		using var cursor = tx.CreateCursor(db);
		var read = 0L;
		var code = cursor.First().resultCode;
		while (code == MDBResultCode.Success)
		{
			cancellationToken.ThrowIfCancellationRequested();
			read++;
			code = cursor.Next().resultCode;
		}

		return code == MDBResultCode.NotFound
			? read
			: throw new LightningStoreException(code, LightningStoreException.Describe(code, $"read {table}"));
	}
}
