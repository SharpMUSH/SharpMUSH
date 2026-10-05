using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Takes a copy of the world without stopping the game, into a directory this process owns, so a
/// snapshot tool has something safe to read.
///
/// <para>What that costs differs by provider. The Lightning provider copies its LMDB environment
/// with the routine LMDB supplies for exactly this, which is consistent by construction. A provider
/// whose database is a server this process only talks to may have no way to produce such a copy at
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
	ValueTask<Result<WorldBackup>> CreateAsync(CancellationToken ct = default);

	/// <summary>The copies currently on disk, newest first. Empty when unsupported.</summary>
	IReadOnlyList<WorldBackup> List();

	/// <summary>
	/// How many automatic copies taken before a portal package operation are kept, in a directory of
	/// their own so they never evict the copies <see cref="CreateAsync"/> keeps. Zero means none is
	/// taken, as does an unsupported provider.
	/// </summary>
	int PackageOperationKeep { get; }

	/// <summary>
	/// Copies the world before a portal package apply, rollback or uninstall (#1333), into the
	/// pre-package directory under <see cref="Root"/> and with <see cref="PackageOperationKeep"/> as its
	/// retention. Otherwise the same as <see cref="CreateAsync"/>. Covers the world only: a managed
	/// package's <c>plugins/</c> directory is not in it.
	/// </summary>
	ValueTask<Result<WorldBackup>> CreateBeforePackageOperationAsync(CancellationToken ct = default);

	/// <summary>The pre-package-operation copies currently on disk, newest first.</summary>
	IReadOnlyList<WorldBackup> ListPackageOperationBackups();
}
