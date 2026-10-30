using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// LMDB embedded storage provider: one directory per world, one write job at a time serializing
/// every mutation (<see cref="Store.LightningWriter"/>), readers running lock-free snapshots. This file holds construction, the store handle,
/// database lifecycle (<see cref="WipeDatabaseAsync"/>, <see cref="CreateStagingAsync"/>) and the small
/// helpers every area partial shares for allocating dbrefs and maintaining the single- and multi-valued
/// dbref-to-dbref edge tables. <see cref="Migration.LightningMigration"/> (a partial of this class) holds
/// <c>Migrate()</c>; every other file under this namespace implements one storage interface area.
/// </summary>
/// <param name="relations">
/// Required — the caller must state its intent explicitly, never fall through a default. The host
/// (<c>Startup.cs</c>) always resolves and passes the live <see cref="IObjectRelationLoader"/>; only
/// <see cref="LightningStagingDatabase"/> and unit-test fixtures that bypass the host's Mediator cache
/// pass <see langword="null"/> — see the <see cref="_relations"/> field doc for why.
/// </param>
public partial class LightningDatabase(
	ILogger<LightningDatabase> logger,
	LightningStoreOptions options,
	IPasswordService passwordService,
	IObjectRelationLoader? relations,
	IReadOnlyList<IMigrationSource>? migrationSources = null,
	IReadOnlyList<PluginFlag>? pluginFlags = null,
	AccountCredentialGate? credentialGate = null,
	Lazy<IEnumerable<IAccountCredentialRevocation>>? credentialRevokers = null)
	: ISharpDatabase, IPackageRegistryService, IRoleRegistryService, ILayoutRegistryService,
		IApplicationRegistryService, ILightningStorageAccessor, IAsyncDisposable
{
	/// <summary>Serializes <c>Migrate()</c> across concurrent callers; migration itself is idempotent, but
	/// running two passes concurrently could interleave the "is this id applied yet" check with its write.</summary>
	private static readonly SemaphoreSlim MigrateLock = new(1, 1);

	private readonly ILogger<LightningDatabase> _logger = logger;
	private readonly AccountCredentialGate _credentialGate = credentialGate ?? new();
	private readonly Lazy<IEnumerable<IAccountCredentialRevocation>> _credentialRevokers = credentialRevokers ?? new(() => []);
	private readonly LightningStoreOptions _options = options;
	private readonly IPasswordService _passwordService = passwordService;

	/// <summary>
	/// How an object this provider builds resolves the relations that point at other objects — location,
	/// home, owner, parent, zone, drop-to, exit destination. The host's loader answers through the
	/// Mediator's object cache, so the object handed back is the one every other reader already holds and
	/// follows the same invalidation; without it each read would hydrate a private snapshot from LMDB.
	/// <para>Null for a staging world, deliberately: its relations must resolve inside its own directory,
	/// and the host's cache fronts the live one.</para>
	/// </summary>
	private readonly IObjectRelationLoader? _relations = relations;

	private readonly IReadOnlyList<IMigrationSource> _migrationSources = migrationSources ?? [];
	private readonly IReadOnlyList<PluginFlag> _pluginFlags = pluginFlags ?? [];

	internal LightningStore Store { get; } = new(options, logger);

	/// <summary>
	/// Closes the store this instance owns — the LMDB environment, its lock file and the writer thread.
	/// The provider is a singleton, so in the host this runs when the container is disposed at shutdown
	/// (<c>ServiceProvider</c> disposes singletons implementing <see cref="IAsyncDisposable"/>); tests own
	/// their instance and dispose it themselves. Idempotent, as is the store's own disposal.
	/// </summary>
	public virtual ValueTask DisposeAsync()
	{
		Store.Dispose();
		return ValueTask.CompletedTask;
	}

	/// <summary>Parses either a bare dbref string ("5") or a typed one ("PLAYER/5") into its numeric id.</summary>
	internal static long ParseDbref(string id) => long.Parse(id.Contains('/') ? id[(id.IndexOf('/') + 1)..] : id.TrimStart('#'));

	/// <summary>
	/// The id a creation that names none takes, inside its write job: the earliest due free number that is not
	/// protected (<see cref="DueFreeNumber"/>), or else the <c>next_dbref</c> counter, which it increments.
	/// </summary>
	internal long AllocateDbref(ITx tx)
	{
		if (DueFreeNumber(tx) is { } free)
		{
			ForgetFreeNumber(tx, free);
			return free;
		}

		var current = ReadNextDbref(tx);
		tx.Put(Tables.Meta, Keys.Str("next_dbref"), Keys.Dbref(current + 1));
		return current;
	}

	/// <summary>
	/// Allocates the dbref the caller named, or nothing when that id is not free to take.
	/// <para>PennMUSH's <c>make_first_free_wrapper</c> (<c>src/destroy.c:928-949</c>) only hands back a
	/// slot that is already on the free list: a dbref below <c>db_top</c> whose object has been recycled
	/// into garbage. SharpMUSH keeps no garbage rows, so the free slots are the holes: below the counter,
	/// holding no object and held by no recover entry, whether or not they are due for reuse yet (asking for
	/// one is how a quarantine is cut short). An id at or above the counter is refused rather than
	/// jumping the counter to reach it, so one mistyped argument cannot strand a block of dbrefs.</para>
	/// <para>An import (<paramref name="import"/>) is the one caller entitled to an id at or above the
	/// counter: the source database's ids are what its softcode holds, so the counter follows the import
	/// to one past the highest id it takes. The source's holes stay holes, as they are its garbage.</para>
	/// <para>Runs inside the caller's write job, which is what makes this check and the object write that
	/// follows it atomic against a second creation racing for the same hole.</para>
	/// </summary>
	internal long? AllocateDbrefAt(ITx tx, long requested, bool import = false)
	{
		if (!MayTake(tx, requested, import)) return null;

		if (requested < ReadNextDbref(tx))
		{
			ForgetFreeNumber(tx, requested);
		}
		else
		{
			tx.Put(Tables.Meta, Keys.Str("next_dbref"), Keys.Dbref(requested + 1));
		}

		return requested;
	}

	/// <summary>Whether <see cref="AllocateDbrefAt"/> would hand out <paramref name="requested"/>, without writing.</summary>
	private static bool MayTake(ITx tx, long requested, bool import = false)
		=> requested >= 0
			&& !tx.TryGet(Tables.Obj, Keys.Dbref(requested), out _)
			&& !IsHeldByRecoverEntry(tx, requested)
			&& (import || requested < ReadNextDbref(tx));

	/// <summary>
	/// The dbref an import names, or the next one when it names none. An id that already holds an
	/// object is refused, and nothing is written.
	/// </summary>
	private long AllocateDbrefFor(ITx tx, long? imported)
		=> imported is not { } requested
			? AllocateDbref(tx)
			: AllocateDbrefAt(tx, requested, import: true)
				?? throw new InvalidOperationException($"#{requested} is not free to take");

	private static long ReadNextDbref(ITx tx)
		=> tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out var v) ? Keys.ReadDbref(v) : 0;

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

	/// <summary>
	/// Puts <paramref name="content"/> in <paramref name="container"/>, or nowhere, as the newest arrival.
	/// Location is the one edge that keeps an order: each placement takes the next arrival number, the
	/// forward value is the container followed by that number, and the reverse value is the number
	/// inverted followed by the content, so a container's duplicates sort newest arrival first, the order
	/// PennMUSH keeps its contents list in.
	/// </summary>
	internal static void SetLocation(ITx tx, long content, long? container)
	{
		var key = Keys.Dbref(content);
		foreach (var old in tx.Dups(Tables.Location.Forward, key).ToList())
		{
			tx.Delete(Tables.Location.Reverse, old.AsSpan(0, 8), ArrivalEntry(ReadArrival(old), content));
			tx.Delete(Tables.Location.Forward, key, old);
		}

		if (container is not { } target) return;
		var arrival = NextArrival(tx);
		tx.Put(Tables.Location.Forward, key, Keys.Concat(Keys.Dbref(target), ArrivalBytes(arrival)));
		tx.Put(Tables.Location.Reverse, Keys.Dbref(target), ArrivalEntry(arrival, content));
	}

	/// <summary>Removes every location entry naming <paramref name="dbref"/>, as content or as container.</summary>
	internal static void DeleteLocationEdges(ITx tx, long dbref)
	{
		SetLocation(tx, dbref, null);
		var key = Keys.Dbref(dbref);
		foreach (var entry in tx.Dups(Tables.Location.Reverse, key).ToList())
		{
			var arrival = ulong.MaxValue - BinaryPrimitives.ReadUInt64BigEndian(entry);
			tx.Delete(Tables.Location.Forward, entry.AsSpan(8), Keys.Concat(key, ArrivalBytes(arrival)));
		}
		tx.DeletePrefix(Tables.Location.Reverse, key);
	}

	/// <summary>The content a reverse location value names.</summary>
	internal static long ReadArrivalContent(ReadOnlySpan<byte> entry) => Keys.ReadDbref(entry[8..]);

	private static ulong ReadArrival(ReadOnlySpan<byte> forward) => BinaryPrimitives.ReadUInt64BigEndian(forward[8..]);

	private static byte[] ArrivalBytes(ulong arrival)
	{
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, arrival);
		return bytes;
	}

	private static byte[] ArrivalEntry(ulong arrival, long content)
		=> Keys.Concat(ArrivalBytes(ulong.MaxValue - arrival), Keys.Dbref(content));

	private static ulong NextArrival(ITx tx)
	{
		var key = Keys.Str("next_arrival");
		var next = tx.TryGet(Tables.Meta, key, out var value) ? BinaryPrimitives.ReadUInt64BigEndian(value) : 0;
		tx.Put(Tables.Meta, key, ArrivalBytes(next + 1));
		return next;
	}

	internal Task MutateWorldAuthorityAsync(Func<Task> mutation, CancellationToken cancellationToken = default)
	{
		return _credentialGate.RunAsync(mutation, cancellationToken);
	}

	/// <summary>Drains the writer, closes the environment, deletes the directory, reopens it empty and
	/// migrates from scratch.</summary>
	public async ValueTask WipeDatabaseAsync(CancellationToken cancellationToken = default)
		=> await MutateWorldAuthorityAsync(async () =>
		{
			_logger.LogWarning("WIPING DATABASE at {Path} - this is destructive and irreversible!", Store.Path);
			Store.WipeDirectory();
			InvalidateDefinitions();
			await Migrate(cancellationToken);
		}, cancellationToken);

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
		try
		{
			await staging.Migrate(ct);
		}
		catch
		{
			// The caller never receives this instance, so nothing else will ever close its store or delete
			// its directory: a failed migration must not leave a writer thread and an LMDB environment
			// running on a directory no one owns.
			await staging.AbortAsync(CancellationToken.None);
			throw;
		}

		return staging;
	}

	/// <summary>Hot backup of the whole environment into <paramref name="path"/>, which is created if it
	/// does not exist. Reads and writes continue while it runs; it is not on <see cref="ISharpDatabase"/>
	/// because no other provider has a directory to copy.</summary>
	public async ValueTask CopyToAsync(string path, bool compact = true, CancellationToken ct = default)
		=> await Task.Run(() => Store.CopyTo(path, compact), ct);

	/// <summary>
	/// Rebuilds every allocator from the data actually on disk. A promoted world's counters were written
	/// by whoever built it, so the live instance — which is about to hand out the next dbref, mail id,
	/// wiki id and account id — has to be told where that data ends before it allocates over the top of it.
	/// Every <c>next_*</c> key in <see cref="Tables.Meta"/> belongs here; one left out is an allocator
	/// handing back ids a staged world already used.
	/// </summary>
	internal async ValueTask RecomputeCountersAsync(CancellationToken ct = default)
		=> await Store.WriteAsync(tx =>
		{
			RecomputeNextDbref(tx);
			RecomputeCounter(tx, "next_mail", Tables.Mail);
			RecomputeCounter(tx, "next_wiki", Tables.WikiPage);
			RecomputeCounter(tx, "next_roll", Tables.Roll);
			RecomputeAccountCounter(tx);
		}, ct);

	/// <summary>Raises <paramref name="counterKey"/> to (highest key in <paramref name="table"/>) + 1 when
	/// that is larger than what is stored — a floor, never a ceiling this lowers. For tables whose keys are
	/// <see cref="Keys.Dbref"/> blobs; <see cref="Tables.Account"/> is keyed by decimal string instead and
	/// has <see cref="RecomputeAccountCounter"/>.</summary>
	private static void RecomputeCounter(ITx tx, string counterKey, TableDef table)
		=> RaiseCounter(tx, counterKey, HighestKey(tx, table));

	/// <summary>
	/// The highest <see cref="Keys.Dbref"/> key in <paramref name="table"/>, or -1 when it is empty: one seek to the
	/// table's last key, since big-endian ids sort numerically. Every id these tables hold is non-negative (a
	/// negative one would encode above them all).
	/// </summary>
	internal static long HighestKey(ITx tx, TableDef table)
		=> tx.RangeReverse(table, []).Select(entry => Keys.ReadDbref(entry.Key)).DefaultIfEmpty(-1).First();

	public async ValueTask<int> ReleaseTrailingDbrefsAsync(CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			var next = Math.Max(HighestKey(tx, Tables.Obj), HighestKey(tx, Tables.Recover)) + 1;
			tx.Put(Tables.Meta, Keys.Str("next_dbref"), Keys.Dbref(next));
			ForgetFreeNumbersFrom(tx, next);
			return (int)next;
		}, cancellationToken);

	/// <summary>The account table's keys are the decimal strings <see cref="AllocateAccountId"/> hands out,
	/// so they sort and decode as text rather than as fixed-width dbrefs and need their own scan.</summary>
	private static void RecomputeAccountCounter(ITx tx)
	{
		var highest = -1L;
		foreach (var (key, _) in tx.Range(Tables.Account, []))
		{
			if (long.TryParse(Keys.ReadStr(key), out var id) && id > highest) highest = id;
		}

		RaiseCounter(tx, "next_account_id", highest);
	}

	private static void RaiseCounter(ITx tx, string counterKey, long highestUsedId)
	{
		var current = tx.TryGet(Tables.Meta, Keys.Str(counterKey), out var v) ? Keys.ReadDbref(v) : 0;
		if (highestUsedId + 1 > current)
		{
			tx.Put(Tables.Meta, Keys.Str(counterKey), Keys.Dbref(highestUsedId + 1));
		}
	}
}
