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
public sealed partial class LightningDatabase(
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

	/// <summary>Closes the environment, deletes the directory, reopens and migrates from scratch.</summary>
	public async ValueTask WipeDatabaseAsync(CancellationToken cancellationToken = default)
	{
		var path = Store.Path;
		Store.Close();
		if (Directory.Exists(path))
		{
			Directory.Delete(path, recursive: true);
		}

		Store.Reopen();
		await Migrate(cancellationToken);
	}

	public Task<IStagingDatabase> CreateStagingAsync(CancellationToken ct = default)
		=> throw new NotImplementedException("Lightning staging databases arrive in Task 18.");
}
