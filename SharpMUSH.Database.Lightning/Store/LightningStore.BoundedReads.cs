using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using LightningDB;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using LmdbDb = LightningDB.LightningDatabase;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	private sealed partial class Tx : IBoundedReadTx
	{
		// Pinned LightningDB 0.23.1 coupling: its public AsSpan narrows size unchecked, and raw CopyTo
		// ignores destination capacity. Review on every package upgrade. Read ONLY this managed field;
		// no pointer/offset/layout access and no fallback if field binding fails.
		[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "size")]
		private static extern ref nint NativeSize(ref MDBValue value);

		private static int CheckedLength(MDBValue value)
		{
			var size = NativeSize(ref value);
			if (size < 0 || size > int.MaxValue || size > NativeReadBudget.MaxRecordBytes)
				throw new NativeReadLimitException("record-bytes");
			return checked((int)size);
		}

		private LmdbDb CheckedTable(TableDef table, NativeReadBudget budget)
		{
			ArgumentNullException.ThrowIfNull(budget);
			budget.CancellationToken.ThrowIfCancellationRequested();
			// TableDef equality is name-only; requested flags cannot certify an existing native table.
			if (!tables.ByName.TryGetValue(table.Name, out var entry)) throw new KeyNotFoundException($"Unknown native table {table.Name}.");
			// Reverse/integer comparators would also invalidate this API's bytewise interval semantics.
			if (entry.Flags != DatabaseOpenFlags.None) throw new NotSupportedException($"Bounded reads require a plain nonduplicate table: {table.Name}.");
			return entry.Handle;
		}

		// Conservative accounting (not measured CLR layout): 64 bytes for each owner/cursor/builder/entry,
		// 32-byte array header and round payload up to 8-byte alignment. Reference slots cost 8 bytes.
		// Range reserves fixed staging storage and its final immutable-array overlap before allocating;
		// all of that conservative reservation stays owned until result disposal, even after cursor close.
		private const long ObjectBytes = 64;
		private static long ArrayBytes(int length) => 32L + ((length + 7L) & ~7L);

		public Found<NativeReadValue> ReadValue(TableDef table, ReadOnlySpan<byte> key, NativeReadBudget budget)
		{
			var db = CheckedTable(table, budget);
			budget.ChargeProbe();
			var (code, nativeKey, nativeValue) = tx.Get(db, key);
			if (!FoundNative(code, "bounded get")) return new NotFound();
			var keyLength = CheckedLength(nativeKey);
			budget.ChargeBytes(keyLength);
			var valueLength = CheckedLength(nativeValue);
			if ((long)keyLength + valueLength > NativeReadBudget.MaxRecordBytes) throw new NativeReadLimitException("record-bytes");
			budget.ChargeBytes(valueLength);
			var reservation = budget.ReserveWorking(ObjectBytes + ArrayBytes(valueLength));
			try
			{
				var value = new byte[valueLength];
				nativeValue.AsSpan().CopyTo(value);
				return new NativeReadValue(value, reservation);
			}
			catch { reservation.Dispose(); throw; }
		}

		private static NativeReadBatch Finish(NativeReadEntry[] entries, int count, bool exhausted, NativeReadReservation reservation)
		{
			reservation.ReserveAdditional(ArrayBytes(8 * count));
			return new NativeReadBatch(ImmutableArray.Create(entries, 0, count), exhausted, reservation);
		}

		public NativeReadBatch ReadRange(TableDef table, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> inclusiveStart,
			ReadOnlySpan<byte> exclusiveEnd, ReadOnlySpan<byte> afterKey, NativeReadBudget budget)
		{
			var db = CheckedTable(table, budget);
			var reservation = budget.ReserveWorking(3 * ObjectBytes + ArrayBytes(8 * NativeReadBudget.MaxBatchRecords));
			try
			{
				var entries = new NativeReadEntry[NativeReadBudget.MaxBatchRecords];
				using var cursor = tx.CreateCursor(db);
				var start = prefix.SequenceCompareTo(inclusiveStart) > 0 ? prefix : inclusiveStart;
				if (afterKey.SequenceCompareTo(start) > 0) start = afterKey;
				budget.ChargeProbe();
				var position = start.IsEmpty ? cursor.First().resultCode : cursor.SetRange(start);
				var count = 0;
				long bytes = 0;
				while (FoundNative(position, "bounded seek/advance"))
				{
					budget.ChargeProbe();
					var (code, nativeKey, nativeValue) = cursor.GetCurrent();
					if (!FoundNative(code, "bounded current")) return Finish(entries, count, true, reservation);
					var keyLength = CheckedLength(nativeKey);
					budget.ChargeBytes(keyLength);
					var mappedKey = nativeKey.AsSpan();
					if (!mappedKey.StartsWith(prefix) || (!exclusiveEnd.IsEmpty && mappedKey.SequenceCompareTo(exclusiveEnd) >= 0)) return Finish(entries, count, true, reservation);
					if (afterKey.IsEmpty || mappedKey.SequenceCompareTo(afterKey) > 0)
					{
						var valueLength = CheckedLength(nativeValue);
						var recordBytes = (long)keyLength + valueLength;
						if (recordBytes > NativeReadBudget.MaxRecordBytes) throw new NativeReadLimitException("record-bytes");
						if (bytes + recordBytes > NativeReadBudget.MaxRecordBytes) return Finish(entries, count, false, reservation);
						budget.ChargeBytes(valueLength);
						reservation.ReserveAdditional(ObjectBytes + ArrayBytes(keyLength) + ArrayBytes(valueLength));
						var keyCopy = new byte[keyLength];
						var valueCopy = new byte[valueLength];
						mappedKey.CopyTo(keyCopy);
						nativeValue.AsSpan().CopyTo(valueCopy);
						entries[count++] = new NativeReadEntry(keyCopy, valueCopy);
						bytes += recordBytes;
						if (count == NativeReadBudget.MaxBatchRecords) return Finish(entries, count, false, reservation);
					}
					budget.ChargeProbe();
					position = cursor.Next().resultCode;
				}
				return Finish(entries, count, true, reservation);

			}
			catch { reservation.Dispose(); throw; }
		}
	}
}
