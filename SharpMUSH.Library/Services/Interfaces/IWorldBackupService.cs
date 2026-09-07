using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Takes a copy of the world without stopping the game, into a directory this process owns, so a
/// snapshot tool has something safe to read.
///
/// <para>What that costs differs by provider. The Lightning provider copies its LMDB environment
/// with the routine LMDB supplies for exactly this, which is consistent by construction. A provider
/// whose database is a server this process only talks to may have no way to produce such a copy at
/// all — ArangoDB's hot-backup API is Enterprise-only, and its Community answer is <c>arangodump</c>
/// run outside the game. Those register an implementation reporting <see cref="IsSupported"/> false
/// and saying why in <see cref="UnavailableReason"/>, rather than one blanket claim that fits none of
/// them.</para>
/// </summary>
public interface IWorldBackupService
{
	/// <summary>Whether this provider can copy its world at all. False makes every other member inert.</summary>
	bool IsSupported { get; }

	/// <summary>
	/// Why <see cref="CreateAsync"/> will refuse, phrased for whoever typed the command — what to use
	/// instead, when there is something. Empty when <see cref="IsSupported"/>.
	/// </summary>
	string UnavailableReason { get; }

	/// <summary>Directory the copies are written into. Empty when unsupported.</summary>
	string Root { get; }

	/// <summary>How many copies <see cref="CreateAsync"/> leaves behind, oldest deleted first.</summary>
	int Keep { get; }

	/// <summary>
	/// How often the scheduled backup runs. <see cref="TimeSpan.Zero"/> leaves scheduling off, which
	/// is the default; a manual trigger still works.
	/// </summary>
	TimeSpan ScheduledInterval { get; }

	/// <summary>
	/// Copies the world into a fresh timestamped directory under <see cref="Root"/>, repoints
	/// <c>latest</c> at it and applies <see cref="Keep"/>. The copy is written aside and moved into
	/// place only once complete, so a reader never sees a partial one. Reports a failure rather than
	/// throwing, apart from cancellation.
	/// </summary>
	ValueTask<OneOf<WorldBackup, Error<string>>> CreateAsync(CancellationToken ct = default);

	/// <summary>The copies currently on disk, newest first. Empty when unsupported.</summary>
	IReadOnlyList<WorldBackup> List();
}
