using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// A staging world in its own LMDB directory beside the live one. It <i>is</i> a
/// <see cref="LightningDatabase"/> — same code, same tables, its own store and its own writer thread —
/// so an importer writes to it through the ordinary provider surface and nothing it does can reach the
/// live world. Promotion is a directory swap (see <see cref="LightningStore.SwapDirectory"/>): the live
/// singleton keeps its identity and its callers, and only the directory beneath it changes.
/// </summary>
public sealed class LightningStagingDatabase : LightningDatabase, IStagingDatabase
{
	private readonly LightningDatabase _live;
	private readonly ILogger _logger;
	private bool _aborted;

	public string StagingId { get; }

	public bool IsPromoted { get; private set; }

	/// <summary>The directory this staging world lives in — moved into the live path on promotion,
	/// deleted on abort.</summary>
	public string StagingPath { get; }

	internal LightningStagingDatabase(
		ILogger<LightningDatabase> logger,
		LightningStoreOptions stagingOptions,
		IPasswordService passwordService,
		LightningDatabase live,
		string stagingId,
		IReadOnlyList<IMigrationSource>? migrationSources,
		IReadOnlyList<PluginFlag>? pluginFlags)
		: base(logger, stagingOptions, passwordService, migrationSources, pluginFlags)
	{
		_live = live;
		_logger = logger;
		StagingId = stagingId;
		StagingPath = stagingOptions.Path;
	}

	/// <summary>
	/// Closes this world and moves its directory into the live path, keeping the outgoing live directory
	/// at <c>&lt;live&gt;.previous</c>. The live instance is not replaced: it drains its writer, swaps the
	/// directory under its own gate and reopens, so every reference the host already holds keeps working
	/// and reads issued mid-swap block on the gate rather than failing. This instance is spent afterwards.
	/// </summary>
	public async Task PromoteToLiveAsync(CancellationToken ct = default)
	{
		if (IsPromoted) throw new InvalidOperationException("Staging already promoted.");
		if (_aborted) throw new InvalidOperationException("Staging was aborted.");

		// Must be fully closed before the move: LMDB writes its lock file and pending pages into this
		// directory, and the live environment is about to open it as its own.
		Store.Dispose();
		var previousPath = _live.Store.Path + ".previous";
		_live.Store.SwapDirectory(StagingPath, previousPath);

		// The live instance now fronts a world whose ids it never allocated — rebuild its allocators or
		// the next @create would overwrite an imported object.
		await _live.RecomputeCountersAsync(ct);

		IsPromoted = true;
		_logger.LogInformation("Lightning staging database {StagingId} promoted to live at {Path}; the world it replaced is kept at {PreviousPath}",
			StagingId, _live.Store.Path, previousPath);
	}

	/// <summary>Closes and deletes this world. The live one is never touched. Idempotent, and a no-op
	/// after a promotion.</summary>
	public Task AbortAsync(CancellationToken ct = default)
	{
		if (IsPromoted || _aborted) return Task.CompletedTask;
		_aborted = true;

		Store.Dispose();
		try
		{
			if (Directory.Exists(StagingPath)) Directory.Delete(StagingPath, recursive: true);
		}
		catch (IOException ex)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer thread's join. Leaving the directory
			// behind costs disk, not correctness — nothing reads a staging directory again.
			_logger.LogWarning(ex, "Could not delete Lightning staging directory {StagingPath}", StagingPath);
		}

		_logger.LogInformation("Lightning staging database {StagingId} aborted", StagingId);
		return Task.CompletedTask;
	}

	public async ValueTask DisposeAsync() => await AbortAsync();
}
