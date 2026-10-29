namespace SharpMUSH.Library.Models;

/// <summary>
/// A destroyed object on the recover list (RhostMUSH's name for it): the whole object, held under its own number
/// until it is recovered or purged. Its attributes and expanded data stay where they were; only its row and what
/// it pointed at are kept here, the relations as objids so a recovery can tell a live one from a reused number.
/// </summary>
/// <param name="Objid">The object's number and creation time, which a recovery gives back unchanged.</param>
/// <param name="Name">Its name when it was destroyed.</param>
/// <param name="Type">PLAYER, ROOM, THING or EXIT.</param>
/// <param name="Owner">Its owner when it was destroyed.</param>
/// <param name="Location">Where it was: a thing's or player's location, an exit's source room.</param>
/// <param name="Home">A thing's or player's home, an exit's destination, a room's drop-to.</param>
/// <param name="Parent">Its parent, if it had one.</param>
/// <param name="Zone">Its zone, if it had one.</param>
/// <param name="Flags">The flags it carried, without GOING and GOING_TWICE.</param>
/// <param name="Powers">The powers it carried.</param>
/// <param name="Roles">The roles assigned to it.</param>
/// <param name="Account">The web account a character was linked to, by its key.</param>
/// <param name="DestroyedBy">Who ran the @destroy that put it here, when that is known.</param>
/// <param name="DestroyedAt">When it was freed onto the list.</param>
/// <param name="Keep">The mark that keeps it from expiring and from every bulk purge, when it has one.</param>
/// <param name="Bytes">What its attributes and expanded data occupy, measured when it was put here.</param>
public sealed record RecoverEntry(
	DBRef Objid,
	string Name,
	string Type,
	DBRef? Owner,
	DBRef? Location,
	DBRef? Home,
	DBRef? Parent,
	DBRef? Zone,
	IReadOnlyList<string> Flags,
	IReadOnlyList<string> Powers,
	IReadOnlyList<string> Roles,
	string? Account,
	DBRef? DestroyedBy,
	DateTimeOffset DestroyedAt,
	RecoverKeep? Keep,
	long Bytes);

/// <summary>Why an entry is kept off the age pass and every bulk purge, and who said so.</summary>
public sealed record RecoverKeep(DBRef By, string Reason, DateTimeOffset At);

/// <summary>
/// What a recovery puts back: the entry's own objid, and the relations the engine settled on after checking which
/// of the recorded ones are still live. A relation left null stays unset.
/// </summary>
public sealed record RecoverRestore(
	DBRef Objid,
	DBRef Owner,
	DBRef? Location,
	DBRef? Home,
	DBRef? Parent,
	DBRef? Zone,
	bool RelinkAccount);

/// <summary>The size of the recover list, and of the kept entries apart, since those never shrink on their own.</summary>
public sealed record RecoverListUsage(long Entries, long Bytes, long KeptEntries, long KeptBytes);
