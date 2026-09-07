using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// LMDB embedded storage provider: one directory per world, one dedicated writer thread serializing
/// every mutation, readers running lock-free snapshots. This file holds construction, the store handle,
/// database lifecycle (<see cref="WipeDatabaseAsync"/>, <see cref="CreateStagingAsync"/>) and the small
/// helpers every area partial shares for allocating dbrefs and maintaining the single- and multi-valued
/// dbref-to-dbref edge tables. <see cref="Migration.LightningMigration"/> (a partial of this class) holds
/// <c>Migrate()</c>; every other file under this namespace is one interface area, per
/// <c>docs/superpowers/specs/2026-09-06-lightning-provider-design.md</c> §4.
/// </summary>
public partial class LightningDatabase(
	ILogger<LightningDatabase> logger,
	LightningStoreOptions options,
	IPasswordService passwordService,
	IReadOnlyList<IMigrationSource>? migrationSources = null,
	IReadOnlyList<PluginFlag>? pluginFlags = null)
	: ISharpDatabase, IWikiService, IPackageRegistryService, IRoleRegistryService, ILayoutRegistryService,
		IApplicationRegistryService, ILightningStorageAccessor
{
	/// <summary>Serializes <c>Migrate()</c> across concurrent callers; migration itself is idempotent, but
	/// running two passes concurrently could interleave the "is this id applied yet" check with its write.</summary>
	private static readonly SemaphoreSlim MigrateLock = new(1, 1);

	private readonly ILogger<LightningDatabase> _logger = logger;
	private readonly LightningStoreOptions _options = options;
	private readonly IPasswordService _passwordService = passwordService;
	private readonly IReadOnlyList<IMigrationSource> _migrationSources = migrationSources ?? [];
	private readonly IReadOnlyList<PluginFlag> _pluginFlags = pluginFlags ?? [];

	internal LightningStore Store { get; } = new(options);

	/// <summary>Parses either a bare dbref string ("5") or a typed one ("PLAYER/5") into its numeric id.</summary>
	internal static long ParseDbref(string id) => long.Parse(id.Contains('/') ? id[(id.IndexOf('/') + 1)..] : id.TrimStart('#'));

	/// <summary>Reads and increments the <c>next_dbref</c> counter inside a write job, returning the id allocated to the caller.</summary>
	internal long AllocateDbref(ITx tx)
	{
		var current = tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out var v) ? Keys.ReadDbref(v) : 0;
		tx.Put(Tables.Meta, Keys.Str("next_dbref"), Keys.Dbref(current + 1));
		return current;
	}

	internal static void PutEdge(ITx tx, (TableDef Forward, TableDef Reverse) edge, long from, long to)
	{
		tx.Put(edge.Forward, Keys.Dbref(from), Keys.Dbref(to));
		tx.Put(edge.Reverse, Keys.Dbref(to), Keys.Dbref(from));
	}

	internal static void DeleteEdge(ITx tx, (TableDef Forward, TableDef Reverse) edge, long from, long to)
	{
		tx.Delete(edge.Forward, Keys.Dbref(from), Keys.Dbref(to));
		tx.Delete(edge.Reverse, Keys.Dbref(to), Keys.Dbref(from));
	}

	/// <summary>Single-valued relations (location, home, owner, parent, zone): replace whatever is there.</summary>
	internal static void SetSingleEdge(ITx tx, (TableDef Forward, TableDef Reverse) edge, long from, long? to)
	{
		foreach (var old in tx.Dups(edge.Forward, Keys.Dbref(from)).ToList())
			DeleteEdge(tx, edge, from, Keys.ReadDbref(old));
		if (to is { } t) PutEdge(tx, edge, from, t);
	}

	internal static long? GetSingleEdge(ITx tx, TableDef forward, long from)
		=> tx.Dups(forward, Keys.Dbref(from)).Select(v => (long?)Keys.ReadDbref(v)).FirstOrDefault();

	/// <summary>Drains the writer, closes the environment, deletes the directory, reopens it empty and
	/// migrates from scratch.</summary>
	public async ValueTask WipeDatabaseAsync(CancellationToken cancellationToken = default)
	{
		_logger.LogWarning("WIPING DATABASE at {Path} - this is destructive and irreversible!", Store.Path);
		Store.WipeDirectory();
		await Migrate(cancellationToken);
	}

	/// <summary>
	/// A second, fully migrated world at <c>&lt;path&gt;.staging-&lt;id&gt;</c> with its own store and writer,
	/// isolated from this one until <see cref="LightningStagingDatabase.PromoteToLiveAsync"/> swaps its
	/// directory into this one's place.
	/// </summary>
	public async Task<IStagingDatabase> CreateStagingAsync(CancellationToken ct = default)
	{
		var stagingId = Guid.NewGuid().ToString("N")[..8];
		var stagingPath = Store.Path + ".staging-" + stagingId;
		if (Directory.Exists(stagingPath))
		{
			Directory.Delete(stagingPath, recursive: true);
		}

		_logger.LogInformation("Creating Lightning staging database at {StagingPath}", stagingPath);

		var staging = new LightningStagingDatabase(_logger, _options with { Path = stagingPath }, _passwordService,
			live: this, stagingId: stagingId, migrationSources: _migrationSources, pluginFlags: _pluginFlags);
		await staging.Migrate(ct);
		return staging;
	}

	/// <summary>Hot backup of the whole environment into <paramref name="path"/>, which is created if it
	/// does not exist. Reads and writes continue while it runs; it is not on <see cref="ISharpDatabase"/>
	/// because no other provider has a directory to copy.</summary>
	public async ValueTask CopyToAsync(string path, bool compact = true, CancellationToken ct = default)
		=> await Task.Run(() => Store.CopyTo(path, compact), ct);

	/// <summary>
	/// Rebuilds the allocators from the data actually on disk. A promoted world's counters were written
	/// by whoever built it, so the live instance — which is about to hand out the next dbref and the next
	/// mail id — has to be told where that data ends before it allocates over the top of it.
	/// </summary>
	internal async ValueTask RecomputeCountersAsync(CancellationToken ct = default)
		=> await Store.WriteAsync(tx =>
		{
			RecomputeNextDbref(tx);
			RecomputeCounter(tx, "next_mail", Tables.Mail);
		}, ct);

	/// <summary>Raises <paramref name="counterKey"/> to (highest key in <paramref name="table"/>) + 1 when
	/// that is larger than what is stored — a floor, never a ceiling this lowers.</summary>
	private static void RecomputeCounter(ITx tx, string counterKey, TableDef table)
	{
		var highest = -1L;
		foreach (var (key, _) in tx.Range(table, []))
		{
			var id = Keys.ReadDbref(key);
			if (id > highest) highest = id;
		}

		var current = tx.TryGet(Tables.Meta, Keys.Str(counterKey), out var v) ? Keys.ReadDbref(v) : 0;
		if (highest + 1 > current)
		{
			tx.Put(Tables.Meta, Keys.Str(counterKey), Keys.Dbref(highest + 1));
		}
	}
}
