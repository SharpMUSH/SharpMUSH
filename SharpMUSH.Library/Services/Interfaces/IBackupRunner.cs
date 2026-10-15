using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// A backup of the world as the game sees it, PennMUSH's dump: <c>@backup</c>, <c>@dump</c> and the backup schedule
/// all run one through here, so each shows the dump messages to everyone and raises the backup and dump events.
/// </summary>
public interface IBackupRunner
{
	/// <summary>
	/// The warning <paramref name="minutes"/> (5 or 1) before a scheduled backup: <c>DUMP`5MIN</c> or <c>DUMP`1MIN</c>,
	/// then dump_warning_5min or dump_warning_1min to everyone.
	/// </summary>
	ValueTask WarnAsync(int minutes);

	/// <summary>
	/// Writes a backup, between dump_message and dump_complete. A written copy raises <c>BACKUP`COMPLETE</c> and
	/// <c>DUMP`COMPLETE</c>; a failed one raises <c>BACKUP`ERROR</c> and <c>DUMP`ERROR</c> and tells wizards and
	/// royalty.
	/// </summary>
	/// <param name="enactor">Who asked for it; null for the schedule.</param>
	ValueTask<Result<WorldBackup>> RunAsync(DBRef? enactor, CancellationToken cancellationToken = default);
}
