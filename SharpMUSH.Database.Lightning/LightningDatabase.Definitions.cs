using System.Collections.Frozen;
using System.Collections.Immutable;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The in-memory copies of the three definition tables — <see cref="Tables.Flag"/>, <see cref="Tables.Power"/>
/// and <see cref="Tables.AttrFlag"/>. Every object read resolves its flag and power names, and every attribute
/// read its flag names, through these; without them each read point-reads and JSON-decodes the definition
/// rows again. The tables are small (tens of rows) and change only through the definition write jobs in this
/// provider, the migration seed and a directory swap, so a whole-table copy is cheap to keep and cheap to
/// rebuild.
/// <para>
/// Consistency: a copy is published only when it was read in a fresh transaction that began after the
/// latest invalidation (see <see cref="DefinitionCache{TRecord}"/>). A write job that changes a definition
/// invalidates once its commit has landed and rebuilds before returning, so its caller — and everyone
/// after it — reads the new definitions. A read already inside its own transaction while the copy is
/// absent decodes the table from that transaction instead, which is consistent with what it is reading,
/// and does not publish it.
/// </para>
/// <para>
/// The records cached are immutable; the Library models (<see cref="Library.Models.SharpObjectFlag"/> and
/// friends) have setters, so every read still maps a fresh model from the cached record rather than
/// handing out a shared instance.
/// </para>
/// </summary>
public partial class LightningDatabase
{
	private readonly DefinitionCache<FlagRecord> _flagDefinitions = new(Tables.Flag, record => record.Aliases);
	private readonly DefinitionCache<PowerRecord> _powerDefinitions = new(Tables.Power, record => record.AllAliases);
	private readonly DefinitionCache<AttributeFlagRecord> _attributeFlagDefinitions = new(Tables.AttrFlag, _ => []);

	/// <summary>The flag definitions, from the copy or — when there is none — read and published now.
	/// Must not be called inside a read or write job: it may open a read of its own.</summary>
	internal DefinitionMap<FlagRecord> FlagDefinitions() => _flagDefinitions.Get(Store);

	/// <inheritdoc cref="FlagDefinitions()"/>
	internal DefinitionMap<PowerRecord> PowerDefinitions() => _powerDefinitions.Get(Store);

	/// <inheritdoc cref="FlagDefinitions()"/>
	internal DefinitionMap<AttributeFlagRecord> AttributeFlagDefinitions() => _attributeFlagDefinitions.Get(Store);

	/// <summary>The flag definitions for a read already inside <paramref name="tx"/>: the copy when there is one,
	/// otherwise decoded from <paramref name="tx"/> itself (and not published).</summary>
	internal DefinitionMap<FlagRecord> FlagDefinitions(ITx tx) => _flagDefinitions.Get(tx);

	/// <inheritdoc cref="FlagDefinitions(ITx)"/>
	internal DefinitionMap<PowerRecord> PowerDefinitions(ITx tx) => _powerDefinitions.Get(tx);

	/// <inheritdoc cref="FlagDefinitions(ITx)"/>
	internal DefinitionMap<AttributeFlagRecord> AttributeFlagDefinitions(ITx tx) => _attributeFlagDefinitions.Get(tx);

	/// <summary>
	/// Drops every definition copy. Called after anything that changes the definition tables other than the
	/// definition write jobs themselves: migration seeding, a wipe, a staging directory swapped in. Also for a
	/// test that writes a definition row directly.
	/// </summary>
	internal void InvalidateDefinitions()
	{
		_flagDefinitions.Invalidate();
		_powerDefinitions.Invalidate();
		_attributeFlagDefinitions.Invalidate();
	}

	/// <summary>
	/// <see cref="InvalidateDefinitions"/>, then reads all three tables again and publishes them. The reads that
	/// resolve flags inside their own transaction never publish a copy, so anything that drops the copies on the
	/// way to serving reads again — migration, a wipe, a swap — rebuilds them before it returns.
	/// </summary>
	internal void ReloadDefinitions()
	{
		InvalidateDefinitions();
		_flagDefinitions.Get(Store);
		_powerDefinitions.Get(Store);
		_attributeFlagDefinitions.Get(Store);
	}

	/// <summary>
	/// Runs a write job that changes the definitions behind <paramref name="cache"/>, then drops the copy and
	/// rebuilds it before returning. Invalidating after the commit, rather than inside the job, is what keeps a
	/// concurrent reader from publishing a copy read from the snapshot before it (see
	/// <see cref="DefinitionCache{TRecord}.Get(LightningStore)"/>). The copy is dropped even when the job fails —
	/// a batch can fail after the job's own work ran — so nothing stale outlives an error.
	/// </summary>
	private async ValueTask<T> WriteDefinitionAsync<TRecord, T>(DefinitionCache<TRecord> cache, Func<ITx, T> job, CancellationToken ct)
		where TRecord : class
	{
		T result;
		try
		{
			result = await Store.WriteAsync(job, ct);
		}
		finally
		{
			cache.Invalidate();
		}

		cache.Get(Store);
		return result;
	}
}

/// <summary>
/// One definition table, decoded: the rows in key order, the canonical-name lookup (the table key, which is the
/// name upper-cased invariantly) and the alias lookup (case-insensitive; the first row in key order holding an
/// alias wins, as the old table scan did).
/// </summary>
internal sealed class DefinitionMap<TRecord>(
	ImmutableArray<TRecord> ordered,
	FrozenDictionary<string, TRecord> byKey,
	FrozenDictionary<string, TRecord> byAlias)
	where TRecord : class
{
	/// <summary>Every row, in the table's key order.</summary>
	public ImmutableArray<TRecord> Ordered { get; } = ordered;

	/// <summary>The row stored under <c>Keys.Upper(<paramref name="name"/>)</c>, as a point read of the table would find it.</summary>
	public TRecord? ByName(string name)
		=> name.Length == 0 ? null : byKey.GetValueOrDefault(name.ToUpperInvariant());

	/// <summary>The row stored under <paramref name="upperName"/> exactly — a value already in <c>Keys.Upper</c> form,
	/// such as one read back from an object's flag edges.</summary>
	public TRecord? ByStoredName(string upperName) => byKey.GetValueOrDefault(upperName);

	/// <summary>The first row, in key order, that lists <paramref name="alias"/> among its aliases, ignoring case.</summary>
	public TRecord? ByAlias(string alias) => byAlias.GetValueOrDefault(alias);

	public static DefinitionMap<TRecord> Read(ITx tx, TableDef table, Func<TRecord, IEnumerable<string>> aliasesOf)
	{
		var ordered = ImmutableArray.CreateBuilder<TRecord>();
		var byKey = new Dictionary<string, TRecord>(StringComparer.Ordinal);
		var byAlias = new Dictionary<string, TRecord>(StringComparer.OrdinalIgnoreCase);
		foreach (var (key, value) in tx.Range(table, []))
		{
			var record = Codec.Deserialize<TRecord>(value);
			ordered.Add(record);
			byKey[Keys.ReadStr(key)] = record;
			foreach (var alias in aliasesOf(record))
			{
				byAlias.TryAdd(alias, record);
			}
		}

		return new DefinitionMap<TRecord>(ordered.ToImmutable(), byKey.ToFrozenDictionary(StringComparer.Ordinal),
			byAlias.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
	}
}

/// <summary>
/// The published copy of one definition table and the generation that guards it. <see cref="Get(LightningStore)"/>
/// notes the generation before it opens its read and publishes what it read only if no invalidation came in
/// between; an invalidation always follows the commit it reports, so a read that saw the old rows can never
/// publish them over the new ones.
/// </summary>
internal sealed class DefinitionCache<TRecord>(TableDef table, Func<TRecord, IEnumerable<string>> aliasesOf)
	where TRecord : class
{
	private readonly Lock _sync = new();
	private DefinitionMap<TRecord>? _map;
	private long _generation;

	public DefinitionMap<TRecord> Get(LightningStore store)
	{
		if (Volatile.Read(ref _map) is { } map)
		{
			return map;
		}

		long generation;
		lock (_sync)
		{
			generation = _generation;
		}

		var built = store.Read(Build);
		lock (_sync)
		{
			if (_generation == generation)
			{
				_map = built;
			}
		}

		return built;
	}

	public DefinitionMap<TRecord> Get(ITx tx) => Volatile.Read(ref _map) ?? Build(tx);

	public void Invalidate()
	{
		lock (_sync)
		{
			_generation++;
			_map = null;
		}
	}

	private DefinitionMap<TRecord> Build(ITx tx) => DefinitionMap<TRecord>.Read(tx, table, aliasesOf);
}
