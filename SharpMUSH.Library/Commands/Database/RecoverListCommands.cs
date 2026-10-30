using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Frees an object onto the recover list — the storage half of a destroy, when the object can come back. See
/// <see cref="IRecoverListStore.RetireObjectAsync"/>. It invalidates what <see cref="DeleteObjectCommand"/> does,
/// for the same reason: the teardown severs edges on objects the caller never named.
/// </summary>
public record RetireObjectCommand(DBRef Target, DBRef? DestroyedBy) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => new DeleteObjectCommand(Target).CacheKeys;

	public string[] CacheTags => new DeleteObjectCommand(Target).CacheTags;
}

/// <summary>
/// Puts a recover entry back under its own objid. The object reappears in every list, in its location's contents,
/// and in its owner's possessions, so this drops what a delete drops.
/// </summary>
public record RestoreRecoverEntryCommand(RecoverRestore Restore) : ICommand<Result<DBRef>>, ICacheInvalidating
{
	public string[] CacheKeys => new DeleteObjectCommand(Restore.Objid).CacheKeys;

	public string[] CacheTags => new DeleteObjectCommand(Restore.Objid).CacheTags;
}

/// <summary>
/// Removes a recover entry for good, freeing its number with no due time; a kept one only when
/// <paramref name="EvenIfKept"/>. The number can be asked for at once; <see cref="SetFreeNumberDueCommand"/> lets the
/// allocator hand it out unasked.
/// </summary>
public record PurgeRecoverEntryCommand(DBRef Objid, bool EvenIfKept) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [];

	public string[] CacheTags => [Definitions.CacheKeys.AttributesTag(Objid.Number)];
}

/// <summary>Marks a recover entry kept, or clears the mark when <paramref name="Keep"/> is null.</summary>
public record SetRecoverKeepCommand(DBRef Objid, RecoverKeep? Keep) : ICommand<bool>;

/// <summary>
/// Records the holes below the counter that are not free yet, due at <paramref name="ReusableAt"/> (never, when null).
/// See <see cref="IFreeNumberStore.RecordUntrackedHolesAsync"/>.
/// </summary>
public record RecordUntrackedHolesCommand(DateTimeOffset? ReusableAt) : ICommand<int>;

/// <summary>Gives a free number the time from which the allocator hands it out unasked. See <see cref="IFreeNumberStore.SetFreeNumberDueAsync"/>.</summary>
public record SetFreeNumberDueCommand(int Number, DateTimeOffset ReusableAt) : ICommand<bool>;

/// <summary>Replaces the configuration's protected numbers. See <see cref="IFreeNumberStore.SetProtectedNumbersAsync"/>.</summary>
public record SetProtectedNumbersCommand(IReadOnlyCollection<int> Numbers) : ICommand;
