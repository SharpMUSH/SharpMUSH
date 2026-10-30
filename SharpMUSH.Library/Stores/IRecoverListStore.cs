using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// The recover list: objects a purge freed, held whole under their numbers until a recovery puts them back or a
/// purge removes them for good. A number with an entry is never handed out by the allocator, so an entry always
/// finds its own number free.
/// </summary>
public interface IRecoverListStore
{
	/// <summary>
	/// Frees <paramref name="dbref"/> onto the recover list: everything <see cref="IObjectStore.DeleteObjectAsync"/>
	/// removes is removed, except its attributes and expanded data, which stay under its number; its row and what it
	/// pointed at move to the entry. Fails, writing nothing, when the object is gone or its creation time differs.
	/// </summary>
	ValueTask<bool> RetireObjectAsync(DBRef dbref, DBRef? destroyedBy, CancellationToken cancellationToken = default);

	/// <summary>The entry holding <paramref name="number"/>.</summary>
	ValueTask<Found<RecoverEntry>> GetRecoverEntryAsync(int number, CancellationToken cancellationToken = default);

	/// <summary>Every entry, by number, each read in a short transaction of its own.</summary>
	IAsyncEnumerable<RecoverEntry> GetRecoverEntriesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Puts an entry back under its own number and creation time, with the relations <paramref name="restore"/>
	/// names, and rebuilds the indexes the retire dropped. Refused when the entry is gone, its number holds an object,
	/// or a player of that name exists.
	/// </summary>
	ValueTask<Result<DBRef>> RestoreRecoverEntryAsync(RecoverRestore restore, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes an entry for good, with its attributes and expanded data, and adds its number to the free numbers
	/// (<see cref="IFreeNumberStore"/>) with no due time, so nothing takes it unasked until
	/// <see cref="IFreeNumberStore.SetFreeNumberDueAsync"/> gives it one. A kept entry is refused unless
	/// <paramref name="evenIfKept"/>, judged in the same write as the removal.
	/// </summary>
	ValueTask<bool> PurgeRecoverEntryAsync(DBRef objid, bool evenIfKept, CancellationToken cancellationToken = default);

	/// <summary>Marks an entry kept, or clears the mark when <paramref name="keep"/> is null.</summary>
	ValueTask<bool> SetRecoverKeepAsync(DBRef objid, RecoverKeep? keep, CancellationToken cancellationToken = default);

	/// <summary>The list's size, without reading what the entries hold.</summary>
	ValueTask<RecoverListUsage> MeasureRecoverListAsync(CancellationToken cancellationToken = default);
}
