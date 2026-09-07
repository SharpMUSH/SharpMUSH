using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Takes a consistent copy of the world without stopping the game, so a snapshot tool has something
/// safe to read. Only a provider that keeps the world in a directory of its own can offer this — the
/// Lightning provider copies its LMDB environment with the routine LMDB supplies for exactly that —
/// so every other provider registers an implementation that reports <see cref="IsSupported"/> false
/// and refuses.
/// </summary>
public interface IWorldBackupService
{
	/// <summary>Whether this provider can copy its world at all. False makes every other member inert.</summary>
	bool IsSupported { get; }

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
