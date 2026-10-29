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

/// <summary>Removes a recover entry for good, freeing its number.</summary>
public record PurgeRecoverEntryCommand(DBRef Objid) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [];

	public string[] CacheTags => [Definitions.CacheKeys.AttributesTag(Objid.Number)];
}

/// <summary>Marks a recover entry kept, or clears the mark when <paramref name="Keep"/> is null.</summary>
public record SetRecoverKeepCommand(DBRef Objid, RecoverKeep? Keep) : ICommand<bool>;
