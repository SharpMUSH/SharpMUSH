using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The free numbers (<see cref="Tables.FreeByTime"/>, <see cref="Tables.FreeByNumber"/>), each stored with the time
/// from which the allocator may hand it out unasked. A number joins them in the write that frees it, which for a
/// destroyed object is the purge of its recover entry, and leaves them in the write that takes it.
/// <see cref="AllocateDbref"/> takes the earliest due that is not protected, so a number comes back as late as the free
/// list allows and every stale reference to it has had the longest time to be noticed.
/// </summary>
public partial class LightningDatabase
{
	private const int FreeNumberPage = 256;

	/// <summary>The stored due time of a number the allocator never hands out unasked.</summary>
	internal const long Never = long.MaxValue;

	/// <summary>
	/// How long the holes a world already had wait when the free numbers are first recorded
	/// (<see cref="MigrateFreeNumbers"/>): <c>recover_keep_objects</c>' default, which the provider cannot read.
	/// </summary>
	internal static readonly TimeSpan HoleQuarantine = TimeSpan.FromDays(30);

	/// <summary>The configuration's numbers, written by the host (<see cref="SetProtectedNumbersAsync"/>).</summary>
	private static readonly byte[] ProtectedNumbersKey = Keys.Str("protected_dbrefs");

	/// <summary>The room every world starts with, and God, protected whatever the configuration says.</summary>
	private static readonly long[] AlwaysProtected = [0, 1];

	internal const string FreeNumberMigrationId = "0010_free_numbers";

	public ValueTask<int> CountFreeNumbersAsync(CancellationToken cancellationToken = default)
		=> ValueTask.FromResult((int)Store.Read(tx => tx.Count(Tables.FreeByNumber)));

	public ValueTask<bool> IsFreeNumberAsync(int number, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(number >= 0 && Store.Read(tx => tx.TryGet(Tables.FreeByNumber, Keys.Dbref(number), out _)));

	public async ValueTask<bool> SetFreeNumberDueAsync(int number, DateTimeOffset reusableAt, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (number < 0 || !tx.TryGet(Tables.FreeByNumber, Keys.Dbref(number), out _)) return false;
			FreeNumberAt(tx, number, reusableAt.ToUnixTimeMilliseconds());
			return true;
		}, cancellationToken);

	public async IAsyncEnumerable<FreeNumber> GetFreeNumbersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var (key, _) in Store.RangeAsync(Tables.FreeByTime, [], FreeNumberPage, cancellationToken))
		{
			var due = BinaryPrimitives.ReadInt64BigEndian(key);
			yield return new FreeNumber((int)Keys.ReadDbref(key.AsSpan(8)), due == Never ? null : DateTimeOffset.FromUnixTimeMilliseconds(due));
		}
	}

	public async IAsyncEnumerable<NumberReference> FindNumberReferencesAsync(IReadOnlySet<int> numbers,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		if (numbers.Count == 0) yield break;

		await foreach (var (key, value) in Store.RangeAsync(Tables.AttrVal, [], FreeNumberPage, cancellationToken))
		{
			var (holder, attribute) = Keys.ParseAttr(key);
			foreach (var number in BareNumbers(Keys.ReadStr(value)).Where(numbers.Contains).Distinct())
			{
				yield return new NumberReference((int)holder, attribute, number);
			}
		}
	}

	/// <summary>Every <c>#N</c> in <paramref name="text"/> that is not an objid's <c>#N:ms</c>.</summary>
	private static IEnumerable<int> BareNumbers(string text)
		=> BareNumberPattern().Matches(text).Select(match => int.TryParse(match.Groups[1].ValueSpan, out var number) ? number : -1)
			.Where(number => number >= 0);

	[GeneratedRegex(@"(?<![\w#])#(\d{1,9})(?![\d:])")]
	private static partial Regex BareNumberPattern();

	public async ValueTask SetProtectedNumbersAsync(IReadOnlyCollection<int> numbers, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			var value = new byte[numbers.Count * 8];
			var offset = 0;
			foreach (var number in numbers)
			{
				BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(offset), number);
				offset += 8;
			}

			tx.Put(Tables.Meta, ProtectedNumbersKey, value);
		}, cancellationToken);

	public async ValueTask<int> RecordUntrackedHolesAsync(DateTimeOffset? reusableAt, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => RecordUntrackedHoles(tx, reusableAt?.ToUnixTimeMilliseconds() ?? Never), cancellationToken);

	/// <summary>
	/// Records each number below the counter that holds no object, no recover entry and is not free yet, due at
	/// <paramref name="reusableAt"/>.
	/// </summary>
	private static int RecordUntrackedHoles(ITx tx, long reusableAt)
	{
		var next = ReadNextDbref(tx);
		var held = tx.Range(Tables.Obj, []).Select(entry => Keys.ReadDbref(entry.Key))
			.Concat(tx.Range(Tables.Recover, []).Select(entry => Keys.ReadDbref(entry.Key)))
			.ToHashSet();

		var recorded = 0;
		for (var number = 0L; number < next; number++)
		{
			if (held.Contains(number) || tx.TryGet(Tables.FreeByNumber, Keys.Dbref(number), out _)) continue;
			FreeNumberAt(tx, number, reusableAt);
			recorded++;
		}

		return recorded;
	}

	/// <summary>The first world migration that knew free numbers: the holes already there start a quarantine now.</summary>
	private static void MigrateFreeNumbers(ITx tx)
	{
		var marker = Keys.Str("mig:" + FreeNumberMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;

		var now = DateTimeOffset.UtcNow;
		RecordUntrackedHoles(tx, (now + HoleQuarantine).ToUnixTimeMilliseconds());
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = FreeNumberMigrationId, AppliedUnixMs = now.ToUnixTimeMilliseconds()
		}));
	}

	/// <summary>Adds <paramref name="number"/> to the free numbers, due at <paramref name="reusableAt"/> (unix ms, or <see cref="Never"/>).</summary>
	internal static void FreeNumberAt(ITx tx, long number, long reusableAt)
	{
		ForgetFreeNumber(tx, number);
		tx.Put(Tables.FreeByTime, FreeTimeKey(reusableAt, number), [0]);
		tx.Put(Tables.FreeByNumber, Keys.Dbref(number), Keys.Dbref(reusableAt));
	}

	/// <summary>Takes <paramref name="number"/> off the free numbers, if it is there.</summary>
	internal static void ForgetFreeNumber(ITx tx, long number)
	{
		if (!tx.TryGet(Tables.FreeByNumber, Keys.Dbref(number), out var reusableAt)) return;
		tx.Delete(Tables.FreeByTime, FreeTimeKey(Keys.ReadDbref(reusableAt), number));
		tx.Delete(Tables.FreeByNumber, Keys.Dbref(number));
	}

	/// <summary>
	/// The earliest free number due by now that the allocator may hand out unasked: not protected, and, should the
	/// tables ever disagree, holding nothing. Null when there is none.
	/// </summary>
	internal static long? DueFreeNumber(ITx tx)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		HashSet<long>? protectedNumbers = null;
		foreach (var (key, _) in tx.Range(Tables.FreeByTime, []))
		{
			if (BinaryPrimitives.ReadInt64BigEndian(key) > now) return null;

			var number = Keys.ReadDbref(key.AsSpan(8));
			protectedNumbers ??= ProtectedNumbers(tx);
			if (protectedNumbers.Contains(number)
				|| tx.TryGet(Tables.Obj, Keys.Dbref(number), out _)
				|| IsHeldByRecoverEntry(tx, number))
			{
				continue;
			}

			return number;
		}

		return null;
	}

	/// <summary>#0, #1, the configuration's numbers, and every number a package record names.</summary>
	private static HashSet<long> ProtectedNumbers(ITx tx)
	{
		HashSet<long> numbers = [.. AlwaysProtected];
		if (tx.TryGet(Tables.Meta, ProtectedNumbersKey, out var configured))
		{
			for (var offset = 0; offset + 8 <= configured.Length; offset += 8)
			{
				numbers.Add(BinaryPrimitives.ReadInt64BigEndian(configured.AsSpan(offset)));
			}
		}

		foreach (var (_, value) in tx.Range(Tables.PkgObj, []))
		{
			if (DBRef.TryParse(Codec.Deserialize<PackageObjectRecord>(value).Objid, out var objid) && objid is { } named)
			{
				numbers.Add(named.Number);
			}
		}

		return numbers;
	}

	/// <summary>Drops the free numbers at or above <paramref name="next"/>, after the counter came down to it.</summary>
	private static void ForgetFreeNumbersFrom(ITx tx, long next)
	{
		foreach (var (key, _) in tx.RangeFromKey(Tables.FreeByNumber, Keys.Dbref(next)).ToList())
		{
			ForgetFreeNumber(tx, Keys.ReadDbref(key));
		}
	}

	private static byte[] FreeTimeKey(long reusableAt, long number)
	{
		var key = new byte[16];
		BinaryPrimitives.WriteInt64BigEndian(key, reusableAt);
		Keys.Dbref(number).CopyTo(key, 8);
		return key;
	}
}
