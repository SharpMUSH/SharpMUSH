using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// World backup for the Lightning provider, over
/// <see cref="ILightningStorageAccessor.CopyToAsync"/> — LMDB's own copy routine, which takes a read
/// transaction and writes the whole environment out from it. Writes keep landing while it runs and
/// the copy is the snapshot of that transaction, so the game never stops and the result is a
/// point-in-time copy by construction, not merely a fast one.
///
/// <para>Reading a live <c>data.mdb</c> with an ordinary file copy is what this exists to replace:
/// commits move pages under the reader, and the result is not promised to open. A copy taken this
/// way is a complete environment, and the directory it lands in is what a snapshot tool should
/// read.</para>
///
/// <para>Staging, the <c>latest</c> pointer and retention are <see cref="WorldBackupWriter"/>'s, the
/// same as every other provider; the only Lightning-specific part is filling the directory.</para>
/// </summary>
public sealed class LightningWorldBackupService : IWorldBackupService
{
	private readonly WorldBackupWriter _writer;

	/// <param name="compact">
	/// Whether the copy omits free pages. A compacted copy is smaller — often much smaller on a world
	/// that has seen a lot of deletion — and slower to produce, because every page is rewritten rather
	/// than the file being copied through.
	/// </param>
	public LightningWorldBackupService(
		ILightningStorageAccessor accessor,
		WorldBackupOptions options,
		bool compact,
		ILogger<LightningWorldBackupService> logger)
		=> _writer = new WorldBackupWriter(options,
			(directory, ct) => accessor.CopyToAsync(directory, compact, ct), logger);

	public bool IsSupported => true;

	public string UnavailableReason => string.Empty;

	public string Root => _writer.Root;

	public int Keep => _writer.Keep;

	public TimeSpan ScheduledInterval => _writer.ScheduledInterval;

	public ValueTask<OneOf<WorldBackup, Error<string>>> CreateAsync(CancellationToken ct = default)
		=> _writer.CreateAsync(ct);

	public IReadOnlyList<WorldBackup> List() => _writer.List();
}
