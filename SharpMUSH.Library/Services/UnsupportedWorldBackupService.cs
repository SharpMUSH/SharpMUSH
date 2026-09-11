using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// <see cref="IWorldBackupService"/> for a provider that cannot copy its own world. Registered so the
/// command and the scheduled service can depend on the interface unconditionally and say why nothing
/// happened, instead of each of them having to know which provider is running.
///
/// <para><paramref name="reason"/> is carried per provider rather than stated once here, because the
/// reasons genuinely differ — a database this process only reaches over the network is not the same
/// situation as one whose support is simply not written yet, and an operator reading the refusal
/// needs to know which of those they are looking at.</para>
/// </summary>
public sealed class UnsupportedWorldBackupService(string provider, string reason) : IWorldBackupService
{
	public bool IsSupported => false;

	public string UnavailableReason => $"the {provider} provider {reason}";

	public string Root => string.Empty;

	public int Keep => 0;

	public TimeSpan ScheduledInterval => TimeSpan.Zero;

	public ValueTask<Result<WorldBackup>> CreateAsync(CancellationToken ct = default)
		=> ValueTask.FromResult<Result<WorldBackup>>(new Error<string>(UnavailableReason));

	public IReadOnlyList<WorldBackup> List() => [];
}
