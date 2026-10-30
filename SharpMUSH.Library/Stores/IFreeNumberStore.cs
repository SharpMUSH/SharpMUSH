using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// The free numbers: dbrefs below the allocation counter that hold no object and no recover entry, each with the
/// time from which a creation may take it unasked. A creation that names no number takes the earliest such number
/// that has come due and is not protected, and grows the counter only when there is none. A number becomes free when
/// its recover entry is purged, so the time it spent on the recover list is its quarantine; an entry purged early
/// comes due when it would have expired.
/// </summary>
public interface IFreeNumberStore
{
	/// <summary>How many numbers are free: PennMUSH's garbage count.</summary>
	ValueTask<int> CountFreeNumbersAsync(CancellationToken cancellationToken = default);

	/// <summary>Whether <paramref name="number"/> is free.</summary>
	ValueTask<bool> IsFreeNumberAsync(int number, CancellationToken cancellationToken = default);

	/// <summary>Every free number, the earliest due first, the order the allocator takes them in.</summary>
	IAsyncEnumerable<FreeNumber> GetFreeNumbersAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// The numbers the configuration names, which the allocator never hands out unasked even when they are free:
	/// replaces the set it had. #0 and #1 are always protected, and so is every number a package record names.
	/// </summary>
	ValueTask SetProtectedNumbersAsync(IReadOnlyCollection<int> numbers, CancellationToken cancellationToken = default);

	/// <summary>
	/// Every attribute that names one of <paramref name="numbers"/> as a bare <c>#N</c>, rather than as an objid, so a
	/// game can fix it before the number comes back. Read a page at a time, never in one long transaction.
	/// </summary>
	IAsyncEnumerable<NumberReference> FindNumberReferencesAsync(IReadOnlySet<int> numbers, CancellationToken cancellationToken = default);

	/// <summary>
	/// Records every number below the counter that holds nothing and is not free yet, due at
	/// <paramref name="reusableAt"/> (never, when null), so it waits out a quarantine like any other: the holes an import
	/// leaves where the source had garbage. Returns how many.
	/// </summary>
	ValueTask<int> RecordUntrackedHolesAsync(DateTimeOffset? reusableAt, CancellationToken cancellationToken = default);
}
