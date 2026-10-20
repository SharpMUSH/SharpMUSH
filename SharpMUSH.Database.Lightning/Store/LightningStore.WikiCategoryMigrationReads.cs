using LightningDB;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	private sealed partial class Tx : IWikiCategoryMigrationReadTx
	{
		private const int WikiCategorySourceCeiling = 64 * 1024 * 1024;
		private const int WikiCategoryKeyCeiling = 128 * 1024;

		public Found<NativeReadValue> ReadWikiCategoryPage(ReadOnlySpan<byte> pageKey,
			int maxSourceBytes, NativeReadBudget budget)
		{
			ArgumentNullException.ThrowIfNull(budget);
			if (pageKey.Length != 8) throw new ArgumentException("A wiki page key must contain eight bytes.", nameof(pageKey));
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
			ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSourceBytes, WikiCategorySourceCeiling);
			var db = CheckedTable(Tables.WikiPage, budget);
			budget.ChargeProbe();
			var (code, nativeKey, nativeValue) = tx.Get(db, pageKey);
			if (!FoundNative(code, "wiki category source get")) return new NotFound();
			if (NativeSize(ref nativeKey) != 8) throw new InvalidDataException("Invalid native wiki page key length.");
			budget.ChargeBytes(8);
			var size = NativeSize(ref nativeValue);
			if (size < 0) throw new InvalidDataException("Invalid native wiki page source length.");
			if (size > maxSourceBytes)
				throw new WikiCategoryMigrationCapacityException("source-bytes", (long)size, maxSourceBytes,
					size > WikiCategorySourceCeiling);
			// The configured cap is at most 64 MiB, so narrowing and AsSpan are safe only after this check.
			var length = checked((int)size);
			budget.ChargeBytes(length);
			budget.ChargeRecord();
			var reservation = budget.ReserveWorking(ObjectBytes + ArrayBytes(length));
			try
			{
				var copy = new byte[length];
				nativeValue.AsSpan().CopyTo(copy);
				var owner = new NativeReadValue(copy, reservation);
				budget.CancellationToken.ThrowIfCancellationRequested();
				return owner;
			}
			catch { reservation.Dispose(); throw; }
		}

		public WikiCategoryKeyBatch ReadWikiCategoryKeys(TableDef table, ReadOnlySpan<byte> afterKey,
			int maxKeys, int maxKeyBytes, NativeReadBudget budget)
		{
			ArgumentNullException.ThrowIfNull(budget);
			ArgumentNullException.ThrowIfNull(table);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeys);
			ArgumentOutOfRangeException.ThrowIfGreaterThan(maxKeys, 128);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeyBytes);
			ArgumentOutOfRangeException.ThrowIfGreaterThan(maxKeyBytes, WikiCategoryKeyCeiling);
			ArgumentOutOfRangeException.ThrowIfGreaterThan(afterKey.Length, WikiCategoryKeyCeiling);
			if (table.Name != Tables.WikiPage.Name && table.Name != Tables.WikiByCategory.Name)
				throw new NotSupportedException("Category migration keys require a supported core wiki table.");
			var pageKeys = table.Name == Tables.WikiPage.Name;
			if (pageKeys && !afterKey.IsEmpty && afterKey.Length != 8)
				throw new ArgumentException("A wiki page resume key must contain eight bytes.", nameof(afterKey));
			var db = CheckedTable(table, budget);
			var reservation = budget.ReserveWorking(2 * ObjectBytes + ArrayBytes(8 * maxKeys));
			try
			{
				var keys = new byte[maxKeys][];
				var count = 0;
				var bytes = 0;
				var exhausted = true;
				using (var cursor = tx.CreateCursor(db))
				{
					budget.ChargeProbe();
					var position = afterKey.IsEmpty ? cursor.First().resultCode : cursor.SetRange(afterKey);
					while (FoundNative(position, "wiki category keys seek/advance"))
					{
						budget.ChargeProbe();
						var (code, nativeKey, _) = cursor.GetCurrent();
						if (!FoundNative(code, "wiki category keys current")) break;
						var length = WikiCategoryKeyLength(nativeKey, pageKeys, maxKeyBytes);
						budget.ChargeBytes(length);
						var mappedKey = nativeKey.AsSpan();
						if (afterKey.IsEmpty || mappedKey.SequenceCompareTo(afterKey) > 0)
						{
							if (length > maxKeyBytes - bytes)
							{
								if (count == 0) throw new WikiCategoryMigrationCapacityException("key-bytes", length, maxKeyBytes);
								exhausted = false;
								break;
							}
							budget.ChargeRecord();
							reservation.ReserveAdditional(ArrayBytes(length));
							var copy = new byte[length];
							mappedKey.CopyTo(copy);
							keys[count++] = copy;
							bytes += length;
							if (count == maxKeys)
							{
								exhausted = false;
								break;
							}
						}
						budget.ChargeProbe();
						position = cursor.Next().resultCode;
					}
				}
				// No native cursor remains when the caller receives keys for same-transaction deletion.
				var owner = new WikiCategoryKeyBatch(keys, count, exhausted, reservation);
				budget.CancellationToken.ThrowIfCancellationRequested();
				return owner;
			}
			catch { reservation.Dispose(); throw; }
		}

		private static int WikiCategoryKeyLength(MDBValue nativeKey, bool pageKeys, int maxKeyBytes)
		{
			var size = NativeSize(ref nativeKey);
			if (size <= 0) throw new InvalidDataException("Invalid native wiki category key length.");
			if (pageKeys && size != 8) throw new InvalidDataException("Invalid native wiki page key length.");
			if (size > WikiCategoryKeyCeiling)
				throw new WikiCategoryMigrationCapacityException("key-bytes", (long)size, maxKeyBytes, requiresEnvelopeReview: true);
			return checked((int)size);
		}
	}
}
