namespace SharpMUSH.Library.Models;

/// <summary>
/// The standard locks, named as PennMUSH names them (<c>src/lock.c:56-88</c>).
/// </summary>
/// <remarks>
/// Every member's name must match its <c>ILockService.SystemLocks</c> key up to case, because that
/// key is what <c>@lock/&lt;switch&gt;</c> stores and what <c>LockService.GetIfSet</c> looks up by
/// member name. Case is bridged by <see cref="SharpObject.LockNameComparer"/> — Penn's own lookup is
/// <c>strcasecmp</c> (<c>src/lock.c:364</c>) — but an abbreviation is not, so a member may not be
/// spelled shorter than the lock it names. <c>StandardLockLookupTests</c> pins this for every member.
/// </remarks>
public enum LockType
{
	Basic,
	Enter,
	Use,
	Zone,
	Page,
	Teleport,
	Speech,
	Listen,
	Command,
	Parent,
	Link,
	Leave,
	Drop,
	Give,
	From,
	Pay,
	Receive,
	Mail,
	Follow,
	Examine,
	ChZone,
	Forward,
	Control,
	DropTo,
	Destroy,
	Interact,
	MailForward,
	Take,
	Open,
	Filter,
	InFilter,
	DropIn,
	ChOwn
}
