using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
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

	/// <summary>
	/// The copies taken before a portal package operation: the same copy routine, into
	/// <see cref="WorldBackupOptions.PackageOperationRoot"/> with its own retention. Null when
	/// <see cref="WorldBackupOptions.PackageOperationKeep"/> turns them off.
	/// </summary>
	private readonly WorldBackupWriter? _packageOperationWriter;

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
	{
		Func<string, CancellationToken, ValueTask> copy = (directory, ct) => accessor.CopyToAsync(directory, compact, ct);
		// The copy's size is known before it is taken: LMDB writes the live pages for a compacting copy
		// and the file up to its last page otherwise. An accessor that is not the provider itself (a test
		// double) gives no estimate, and so no free-space check before the run.
		Func<long>? estimate = accessor is LightningDatabase database
			? () => database.Store.Usage().CopyBytes(compact)
			: null;
		_writer = new WorldBackupWriter(options, copy, logger) { EstimateCopyBytes = estimate };
		_packageOperationWriter = options.PackageOperationKeep > 0
			? new WorldBackupWriter(options with
			{
				Root = options.PackageOperationRoot,
				Keep = options.PackageOperationKeep,
				Interval = TimeSpan.Zero
			}, copy, logger)
			{ EstimateCopyBytes = estimate }
			: null;
	}

	public bool IsSupported => true;

	public string UnavailableReason => string.Empty;

	public string Root => _writer.Root;

	public int Keep => _writer.Keep;

	public TimeSpan ScheduledInterval => _writer.ScheduledInterval;

	public ValueTask<Result<WorldBackup>> CreateAsync(CancellationToken ct = default)
		=> _writer.CreateAsync(ct);

	public IReadOnlyList<WorldBackup> List() => _writer.List();

	public int PackageOperationKeep => _packageOperationWriter?.Keep ?? 0;

	public ValueTask<Result<WorldBackup>> CreateBeforePackageOperationAsync(CancellationToken ct = default)
		=> _packageOperationWriter?.CreateAsync(ct)
			?? ValueTask.FromResult<Result<WorldBackup>>(new Error<string>("automatic backups before package operations are turned off"));

	public IReadOnlyList<WorldBackup> ListPackageOperationBackups() => _packageOperationWriter?.List() ?? [];
}
