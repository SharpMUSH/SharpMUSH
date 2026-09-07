using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// <see cref="IWorldBackupService"/> for a provider whose world lives in a database server rather
/// than a directory this process owns. Registered so the command, the scheduled service and anything
/// else can depend on the interface unconditionally and say why nothing happened, instead of each of
/// them having to know which provider is running.
/// </summary>
public sealed class UnsupportedWorldBackupService(string provider) : IWorldBackupService
{
	public bool IsSupported => false;

	public string Root => string.Empty;

	public int Keep => 0;

	public TimeSpan ScheduledInterval => TimeSpan.Zero;

	public ValueTask<OneOf<WorldBackup, Error<string>>> CreateAsync(CancellationToken ct = default)
		=> ValueTask.FromResult<OneOf<WorldBackup, Error<string>>>(new Error<string>(
			$"The {provider} provider keeps no world directory to copy; back it up with that database's own tools."));

	public IReadOnlyList<WorldBackup> List() => [];
}
