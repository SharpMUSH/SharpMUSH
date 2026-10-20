using LightningDB;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using LmdbDb = LightningDB.LightningDatabase;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	private sealed partial class Tx : IBoundedDuplicateReadTx
	{
		private LmdbDb CheckedDuplicateTable(TableDef table, NativeReadBudget budget)
		{
			RequireReadCallback();
			ArgumentNullException.ThrowIfNull(budget);
			budget.CancellationToken.ThrowIfCancellationRequested();
			NativeTableName.Validate(table.Name);
			if (!tables.ByName.TryGetValue(table.Name, out var entry)) throw new KeyNotFoundException($"Unknown native table {table.Name}.");
			// Requested TableDef flags do not certify the opened native database. Integer/reverse and
			// fixed-width duplicate comparators are deliberately outside this bytewise capability.
			if (entry.Flags != DatabaseOpenFlags.DuplicatesSort)
				throw new NotSupportedException($"Bounded duplicate reads require variable DUPSORT: {table.Name}.");
			return entry.Handle;
		}

		public NativeReadBatch ReadDuplicates(TableDef table, ReadOnlySpan<byte> key, byte[]? afterValue, NativeReadBudget budget)
		{
			var db = CheckedDuplicateTable(table, budget);
			var reservation = budget.ReserveWorking(3 * ObjectBytes + ArrayBytes(8 * NativeReadBudget.MaxBatchRecords));
			try
			{
				var entries = new NativeReadEntry[NativeReadBudget.MaxBatchRecords];
				using var cursor = tx.CreateCursor(db);
				budget.ChargeProbe();
				// Native GET_BOTH_RANGE(empty) depends on the duplicate storage layout. Set handles
				// null/empty consistently; the strict comparison below still governs admission.
				var position = afterValue is { Length: > 0 }
					? cursor.GetBothRange(key, afterValue.AsSpan()) : cursor.Set(key);
				if (!FoundNative(position, "bounded duplicate seek")) return Finish(entries, 0, true, reservation);
				budget.ChargeProbe();
				var current = cursor.GetCurrent();
				var count = 0;
				long bytes = 0;
				while (FoundNative(current.resultCode, "bounded duplicate current/advance"))
				{
					var (_, nativeKey, nativeValue) = current;
					var keyLength = CheckedLength(nativeKey);
					budget.ChargeBytes(keyLength);
					var valueLength = CheckedLength(nativeValue);
					var recordBytes = (long)keyLength + valueLength;
					if (recordBytes > NativeReadBudget.MaxRecordBytes) throw new NativeReadLimitException("record-bytes");
					budget.ChargeBytes(valueLength);
					var mappedKey = nativeKey.AsSpan();
					var mappedValue = nativeValue.AsSpan();
					if (!mappedKey.SequenceEqual(key)) throw new InvalidOperationException("Native duplicate cursor left its exact key.");
					if (afterValue is null || mappedValue.SequenceCompareTo(afterValue) > 0)
					{
						if (bytes + recordBytes > NativeReadBudget.MaxRecordBytes) return Finish(entries, count, false, reservation);
						budget.ChargeRecord();
						reservation.ReserveAdditional(ObjectBytes + ArrayBytes(keyLength) + ArrayBytes(valueLength));
						var keyCopy = new byte[keyLength];
						var valueCopy = new byte[valueLength];
						mappedKey.CopyTo(keyCopy);
						mappedValue.CopyTo(valueCopy);
						entries[count++] = new NativeReadEntry(keyCopy, valueCopy);
						bytes += recordBytes;
						if (count == NativeReadBudget.MaxBatchRecords) return Finish(entries, count, false, reservation);
					}
					budget.ChargeProbe();
					current = cursor.NextDuplicate();
				}
				return Finish(entries, count, true, reservation);
			}
			catch { reservation.Dispose(); throw; }
		}
	}
}
