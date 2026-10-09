using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DotNext.Threading;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAttributeStore"/>: attributes, attribute flags, and the attribute-entry table.
/// <para>
/// The attribute tree is a flat ordered keyspace rather than a graph. <see cref="Tables.AttrMeta"/> holds
/// one row per node keyed <c>Keys.Attr(dbref, LONGNAME)</c> — the dbref's eight big-endian bytes, a 0x00
/// separator, then the full backtick-joined path upper-cased — and <see cref="Tables.AttrVal"/> holds the
/// serialized <see cref="MString"/> under the identical key. Two properties of that encoding do all the
/// work the graph edges used to: every attribute of one object is one prefix range, and a node's long name
/// is a byte-prefix of every descendant's, so LMDB's key order <em>is</em> preorder — which is exactly the
/// parent-before-child ordering <see cref="GetAttributesByRegexAsync"/> is contractually required to
/// produce (see <c>IAttributeStore</c>'s remarks and <c>@CLONE</c>'s no_clone propagation).
/// </para>
/// <para>
/// The inheritance members
/// (<see cref="GetAttributeWithInheritanceAsync"/>, <see cref="GetLazyAttributeWithInheritanceAsync"/>)
/// resolve inside one snapshot, on metadata, stopping at the first decisive candidate — see
/// <see cref="ResolveInheritance"/>.
/// </para>
/// </summary>
public partial class LightningDatabase
{
	private const string BranchFlag = "branch";

	#region Attribute reads

	public IAsyncEnumerable<SharpAttribute> GetAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpAttribute>(ct => GetAttributeCoreAsync(dbref, attribute, ct));

	private async IAsyncEnumerable<SharpAttribute> GetAttributeCoreAsync(DBRef dbref, string[] attribute,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var n = (long)dbref.Number;
		var path = Store.Read(tx =>
		{
			var walk = ReadPathPrefixes(tx, n, attribute);
			// All-or-nothing: a partially resolving path is not a hit (IAttributeStore.GetAttributeAsync).
			return walk.Count != attribute.Length
				? []
				: walk.Select(e => HydrateAttribute(tx, n, e.LongName, e.Meta, ReadAttributeValue(tx, n, e.LongName))).ToList();
		});

		foreach (var attr in path)
		{
			ct.ThrowIfCancellationRequested();
			yield return attr;
		}
	}

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<LazySharpAttribute>(ct => GetLazyAttributeCoreAsync(dbref, attribute, ct));

	private async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributeCoreAsync(DBRef dbref, string[] attribute,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var n = (long)dbref.Number;
		var path = Store.Read(tx =>
		{
			var walk = ReadPathPrefixes(tx, n, attribute);
			return walk.Count != attribute.Length
				? []
				: walk.Select(e => HydrateLazyAttribute(tx, n, e.LongName, e.Meta)).ToList();
		});

		foreach (var attr in path)
		{
			ct.ThrowIfCancellationRequested();
			yield return attr;
		}
	}

	public IAsyncEnumerable<SharpAttribute> GetAttributesAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpAttribute>(ct => ScanAttributesCoreAsync(dbref, LiteralPrefixOf(attributePattern), GlobToRegex(attributePattern), ct));

	public IAsyncEnumerable<SharpAttribute> GetAttributesByRegexAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpAttribute>(ct => ScanAttributesCoreAsync(dbref, "", RawRegex(attributePattern), ct));

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<LazySharpAttribute>(ct => ScanLazyAttributesCoreAsync(dbref, LiteralPrefixOf(attributePattern), GlobToRegex(attributePattern), ct));

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesByRegexAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<LazySharpAttribute>(ct => ScanLazyAttributesCoreAsync(dbref, "", RawRegex(attributePattern), ct));

	/// <summary>
	/// The one scan behind all four pattern readers: seek to <c>dbref + literalPrefix</c>, walk while the
	/// key keeps that prefix, keep the rows whose long name matches. <paramref name="literalPrefix"/> is
	/// only an index optimisation — <paramref name="filter"/> alone decides membership — so the regex
	/// readers pass <c>""</c> and range the object's whole attribute space.
	/// <para>
	/// The filter is time-bounded per match (<see cref="SoftcodeRegex.MatchTimeout"/>), and the scan as a
	/// whole answers to the ambient <see cref="ExecutionBudget"/>: every row checks what is left of it, so
	/// a pattern that is slow on every name stops when the evaluation's time is up rather than after
	/// a fresh timeout per row.
	/// </para>
	/// <para>
	/// The match runs when the consumer reaches the row, never ahead of it, so the budget and the
	/// per-match timeout are judged at the moment the row is asked for. That is also why the eager form
	/// reads a value only after its name has matched, in a point read of its own: reading values with the
	/// page would copy the body of every candidate under the prefix, and a broad prefix with few matches
	/// would pay for all of them. Flags resolve through the in-memory definitions.
	/// </para>
	/// </summary>
	private async IAsyncEnumerable<SharpAttribute> ScanAttributesCoreAsync(DBRef dbref, string literalPrefix, Regex filter,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var n = (long)dbref.Number;
		await foreach (var (key, meta) in Store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(n, literalPrefix), ct: ct))
		{
			if (MatchingRow(key, filter, ct) is not { } longName)
			{
				continue;
			}

			var value = Store.Read(tx => tx.TryGet(Tables.AttrVal, key, out var body) ? body : null);
			ReadStats.ValueRead(value?.Length ?? 0);
			yield return HydrateAttribute(AttributeFlagDefinitions(), n, longName, Codec.Deserialize<AttrMetaRecord>(meta), value);
		}
	}

	/// <inheritdoc cref="ScanAttributesCoreAsync"/>
	private async IAsyncEnumerable<LazySharpAttribute> ScanLazyAttributesCoreAsync(DBRef dbref, string literalPrefix, Regex filter,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var n = (long)dbref.Number;
		await foreach (var (key, value) in Store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(n, literalPrefix), ct: ct))
		{
			if (MatchingRow(key, filter, ct) is not { } longName)
			{
				continue;
			}

			yield return HydrateLazyAttribute(AttributeFlagDefinitions(), n, longName, Codec.Deserialize<AttrMetaRecord>(value));
		}
	}

	/// <summary>One <c>attr.meta</c> row of a pattern scan: counted, then its long name when
	/// <paramref name="filter"/> admits it (see <see cref="NameMatches"/>), otherwise null.</summary>
	private string? MatchingRow(byte[] key, Regex filter, CancellationToken ct)
	{
		ReadStats.MetaRowRead();
		var longName = Keys.ParseAttr(key).LongName;
		return NameMatches(filter, longName, ct) ? longName : null;
	}

	/// <summary>
	/// One row of a pattern scan: the scan's remaining budget first, then the time-bounded match. A
	/// match that runs out its own timeout throws <see cref="RegexMatchTimeoutException"/>, which the
	/// softcode callers report as <c>#-1 REGEXP TIMEOUT</c>.
	/// </summary>
	/// <remarks>
	/// The filter's timeout was fixed when the scan began. Once less than that is left of the budget, the
	/// row is matched with a filter bounded by what is left instead, since a synchronous match does not
	/// observe the budget's token.
	/// </remarks>
	private static bool NameMatches(Regex filter, string longName, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var bounded = ExecutionBudget.Current?.RemainingOrThrow() < filter.MatchTimeout
			? SoftcodeRegex.Create(filter.ToString(), filter.Options)
			: filter;
		return bounded.IsMatch(longName);
	}

	public IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, InheritanceWalk? walk = null, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AttributeWithInheritance>(ct =>
			GetAttributeWithInheritanceCoreAsync(dbref, attribute, checkParent, walk ?? InheritanceWalk.ParentsOnly, ct));

	private async IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceCoreAsync(DBRef dbref, string[] attribute,
		bool checkParent, InheritanceWalk walk, [EnumeratorCancellation] CancellationToken ct)
	{
		var resolved = Store.Read(tx => ResolveInheritance(tx, (long)dbref.Number, attribute, checkParent, walk) is { } hit
			? BuildInheritanceHit(tx, hit,
				(readTx, owner, longName, meta) => HydrateAttribute(readTx, owner, longName, meta, ReadAttributeValue(readTx, owner, longName)),
				static a => a.Flags,
				static (attrs, source, kind, flags) => new AttributeWithInheritance(attrs, source, kind, flags))
			: null);

		if (resolved is not null)
		{
			ct.ThrowIfCancellationRequested();
			yield return resolved;
		}
	}

	public IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, InheritanceWalk? walk = null, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<LazyAttributeWithInheritance>(ct =>
			GetLazyAttributeWithInheritanceCoreAsync(dbref, attribute, checkParent, walk ?? InheritanceWalk.ParentsOnly, ct));

	/// <inheritdoc cref="GetAttributeWithInheritanceCoreAsync"/>
	private async IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceCoreAsync(DBRef dbref, string[] attribute,
		bool checkParent, InheritanceWalk walk, [EnumeratorCancellation] CancellationToken ct)
	{
		var resolved = Store.Read(tx => ResolveInheritance(tx, (long)dbref.Number, attribute, checkParent, walk) is { } hit
			? BuildInheritanceHit(tx, hit, (readTx, owner, longName, meta) => HydrateLazyAttribute(readTx, owner, longName, meta),
				static a => a.Flags,
				static (attrs, source, kind, flags) => new LazyAttributeWithInheritance(attrs, source, kind, flags))
			: null);

		if (resolved is not null)
		{
			ct.ThrowIfCancellationRequested();
			yield return resolved;
		}
	}

	#endregion

	#region Attribute entries

	public IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpAttributeEntry>(GetAllAttributeEntriesCoreAsync);

	private async IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		var entries = Store.Read(tx => tx.Range(Tables.AttrEntry, [])
			.Select(entry => MapEntry(Codec.Deserialize<AttributeEntryRecord>(entry.Value)))
			.ToList());

		foreach (var entry in entries)
		{
			ct.ThrowIfCancellationRequested();
			yield return entry;
		}
	}

	public ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default)
		=> new(Store.Read(tx => ReadAttributeEntry(tx, name)));

	public async ValueTask<SharpAttributeEntry?> CreateOrUpdateAttributeEntryAsync(string name, string[] defaultFlags,
		string? limit = null, string[]? enumValues = null, char enumDelimiter = ' ',
		CancellationToken cancellationToken = default)
	{
		var record = NewAttributeEntryRecord(name, defaultFlags, limit, enumValues, enumDelimiter);

		await Store.WriteAsync(tx => tx.Put(Tables.AttrEntry, Keys.Upper(name), Codec.Serialize(record)), cancellationToken);
		return MapEntry(record);
	}

	public async ValueTask<SharpAttributeEntry?> CreateAttributeEntryIfAbsentAsync(string name, string[] defaultFlags,
		string? limit = null, string[]? enumValues = null, char enumDelimiter = ' ',
		CancellationToken cancellationToken = default)
	{
		var record = NewAttributeEntryRecord(name, defaultFlags, limit, enumValues, enumDelimiter);

		var created = await Store.WriteAsync(tx =>
		{
			var key = Keys.Upper(name);
			if (tx.TryGet(Tables.AttrEntry, key, out _)) return false;
			tx.Put(Tables.AttrEntry, key, Codec.Serialize(record));
			return true;
		}, cancellationToken);
		return created ? MapEntry(record) : null;
	}

	public async ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.AttrEntry, Keys.Upper(name)), cancellationToken);

	#endregion

	#region Attribute writes

	public async ValueTask<bool> SetAttributeOwnerAsync(DBRef dbref, string[] attribute, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		if (attribute.Length == 0) return false;
		var key = Keys.Attr(dbref.Number, string.Join('`', attribute.Select(segment => segment.ToUpperInvariant())));
		return await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.AttrMeta, key, out var bytes)) return false;
			var metadata = Codec.Deserialize<AttrMetaRecord>(bytes);
			tx.Put(Tables.AttrMeta, key, Codec.Serialize(metadata with { Owner = (long)owner.Object.Key }));
			return true;
		}, cancellationToken);
	}

	/// <summary>
	/// One write job for the whole path. Every prefix of <paramref name="attribute"/> that has no
	/// <see cref="Tables.AttrMeta"/> row is created — owned by <paramref name="owner"/>, carrying whatever
	/// default flags <see cref="Tables.AttrEntry"/> configures for that level's long name, and holding an
	/// empty value; the leaf then takes <paramref name="value"/>.
	/// <para>
	/// As PennMUSH's <c>atr_add</c>: an entry's default flags apply only when a node is created, so a node
	/// that already exists keeps the flags its owner chose. The leaf's owner becomes the setter; an existing
	/// branch keeps its owner and only gains the <c>branch</c> flag, so a leaf that grows a child becomes a
	/// branch.
	/// </para>
	/// </summary>
	public async ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner,
		CancellationToken cancellationToken = default)
	{
		if (attribute.Length == 0)
		{
			return false;
		}

		var n = (long)dbref.Number;
		var write = PreparedAttributeWrite.From(new AttributeWrite(attribute, value, owner, []));

		return await Store.WriteAsync(tx =>
		{
			if (ReadObject(tx, n) is null)
			{
				return false;
			}

			WriteAttributePath(tx, n, write);
			if (TouchesAliases(write.Path))
			{
				SyncPlayerAliases(tx, n);
			}

			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> SetAttributesAsync(DBRef dbref, IReadOnlyList<AttributeWrite> attributes,
		CancellationToken cancellationToken = default)
	{
		var n = (long)dbref.Number;
		var writes = attributes
			.Where(write => write.Path.Length > 0)
			.Select(PreparedAttributeWrite.From)
			.ToArray();

		return await Store.WriteAsync(tx =>
		{
			if (ReadObject(tx, n) is null)
			{
				return false;
			}

			foreach (var write in writes)
			{
				WriteAttributePath(tx, n, write);
			}

			// The importer's path: an imported player's ALIAS is indexed as it lands.
			if (writes.Any(write => TouchesAliases(write.Path)))
			{
				SyncPlayerAliases(tx, n);
			}

			return true;
		}, cancellationToken);
	}

	/// <summary>
	/// An <see cref="AttributeWrite"/> with its path upper-cased and its value serialized, so the writer
	/// thread does neither.
	/// </summary>
	private readonly record struct PreparedAttributeWrite(string[] Path, byte[] Value, long Owner, string[] Flags)
	{
		public static PreparedAttributeWrite From(AttributeWrite write) => new(
			[.. write.Path.Select(segment => segment.ToUpperInvariant())],
			Keys.Str(MarkupTextSerializer.Serialize(write.Value)),
			write.Owner.Object.Key,
			[.. write.Flags.Select(flag => flag.Name)]);
	}

	private static readonly byte[] EmptyAttributeValue = Keys.Str(MarkupTextSerializer.Serialize(MarkupText.Empty));

	/// <summary>
	/// One attribute set, as <see cref="SetAttributeAsync"/> describes it, inside the caller's transaction.
	/// The write's own flags land on its leaf alongside the entry's defaults.
	/// </summary>
	private static void WriteAttributePath(ITx tx, long n, PreparedAttributeWrite write)
	{
		var path = write.Path;
		var longName = string.Empty;

		for (var level = 0; level < path.Length; level++)
		{
			longName = level == 0 ? path[0] : $"{longName}`{path[level]}";
			var key = Keys.Attr(n, longName);
			var isLeaf = level == path.Length - 1;
			var existing = tx.TryGet(Tables.AttrMeta, key, out var bytes) ? Codec.Deserialize<AttrMetaRecord>(bytes) : null;
			var entry = ReadAttributeEntryRecord(tx, longName);

			// The entry's defaults are a new node's flags; an existing node keeps its own.
			var flags = new List<string>(existing?.Flags ?? entry?.DefaultFlags ?? []);

			if (isLeaf)
			{
				foreach (var flagName in write.Flags)
				{
					AddFlag(flags, flagName);
				}
			}
			else
			{
				AddFlag(flags, BranchFlag);
			}

			tx.Put(Tables.AttrMeta, key, Codec.Serialize(new AttrMetaRecord
			{
				Owner = isLeaf || existing is null ? write.Owner : existing.Owner,
				Flags = [.. flags],
				Entry = entry?.Name ?? existing?.Entry
			}));

			// A node that already exists keeps its value unless it is the leaf being set.
			if (isLeaf)
			{
				tx.Put(Tables.AttrVal, key, write.Value);
			}
			else if (existing is null)
			{
				tx.Put(Tables.AttrVal, key, EmptyAttributeValue);
			}
		}
	}

	/// <summary>
	/// The provider's one full-table write: attribute ownership is stored inside each
	/// <see cref="Tables.AttrMeta"/> row rather than as an indexed edge, so "every attribute owned by X"
	/// can only be answered by a scan. It runs on player deletion (probate transfer), not on any hot path.
	/// </summary>
	public async ValueTask ReassignAttributeOwnerAsync(SharpPlayer oldOwner, SharpPlayer newOwner, CancellationToken cancellationToken = default)
	{
		var oldDbref = (long)oldOwner.Object.Key;
		var newDbref = (long)newOwner.Object.Key;

		await Store.WriteAsync(tx =>
		{
			var owned = tx.Range(Tables.AttrMeta, [])
				.Select(entry => (entry.Key, Meta: Codec.Deserialize<AttrMetaRecord>(entry.Value)))
				.Where(entry => entry.Meta.Owner == oldDbref)
				.ToList();

			foreach (var (key, meta) in owned)
			{
				tx.Put(Tables.AttrMeta, key, Codec.Serialize(meta with { Owner = newDbref }));
			}
		}, cancellationToken);
	}

	public async ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag,
		CancellationToken cancellationToken = default)
		=> await UpdateLeafFlagsAsync((long)dbref.Key, attribute, flags => AddFlag(flags, flag.Name), cancellationToken);

	public async ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
	{
		var (dbref, longName) = ParseAttributeKey(attr.Key);
		await UpdateFlagsAsync(dbref, longName, flags => AddFlag(flags, flag.Name), cancellationToken);
	}

	public async ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag,
		CancellationToken cancellationToken = default)
		=> await UpdateLeafFlagsAsync((long)dbref.Key, attribute, flags => RemoveFlag(flags, flag.Name), cancellationToken);

	public async ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
	{
		var (dbref, longName) = ParseAttributeKey(attr.Key);
		await UpdateFlagsAsync(dbref, longName, flags => RemoveFlag(flags, flag.Name), cancellationToken);
	}

	public ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken cancellationToken = default)
		=> new(AttributeFlagDefinitions().ByName(flagName) is { } record ? MapAttributeFlag(record) : null);

	public IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpAttributeFlag>(GetAttributeFlagsCoreAsync);

	private async IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		foreach (var record in AttributeFlagDefinitions().Ordered)
		{
			ct.ThrowIfCancellationRequested();
			yield return MapAttributeFlag(record);
		}
	}

	/// <summary>
	/// Empties the node's value when it still has children (a branch has to survive to carry them), and
	/// otherwise deletes it outright, dropping <c>branch</c> from the parent when that was its last child.
	/// </summary>
	public async ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
	{
		var path = attribute.Select(segment => segment.ToUpperInvariant()).ToArray();
		var n = (long)dbref.Number;
		var empty = Keys.Str(MarkupTextSerializer.Serialize(MarkupText.Empty));

		return await Store.WriteAsync(tx =>
		{
			if (ReadPathPrefixes(tx, n, path).Count != path.Length || path.Length == 0)
			{
				return false;
			}

			var longName = string.Join('`', path);
			var key = Keys.Attr(n, longName);

			if (HasChildren(tx, n, longName))
			{
				tx.Put(Tables.AttrVal, key, empty);
			}
			else
			{
				tx.Delete(Tables.AttrMeta, key);
				tx.Delete(Tables.AttrVal, key);
				DropParentBranchWhenChildless(tx, n, path);
			}

			if (TouchesAliases(path))
			{
				SyncPlayerAliases(tx, n);
			}

			return true;
		}, cancellationToken);
	}

	/// <summary>Deletes the node and every descendant — one exact-key delete plus one prefix delete over
	/// <c>LONGNAME`</c> in both tables, which is the whole subtree and nothing else (a sibling such as
	/// <c>FOOZ</c> shares the <c>FOO</c> prefix but not the <c>FOO`</c> one).</summary>
	public async ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
	{
		var path = attribute.Select(segment => segment.ToUpperInvariant()).ToArray();
		var n = (long)dbref.Number;

		return await Store.WriteAsync(tx =>
		{
			if (ReadPathPrefixes(tx, n, path).Count != path.Length || path.Length == 0)
			{
				return false;
			}

			var longName = string.Join('`', path);
			var key = Keys.Attr(n, longName);
			var descendants = Keys.Attr(n, longName + "`");

			tx.Delete(Tables.AttrMeta, key);
			tx.Delete(Tables.AttrVal, key);
			tx.DeletePrefix(Tables.AttrMeta, descendants);
			tx.DeletePrefix(Tables.AttrVal, descendants);
			DropParentBranchWhenChildless(tx, n, path);
			if (TouchesAliases(path))
			{
				SyncPlayerAliases(tx, n);
			}

			return true;
		}, cancellationToken);
	}

	#endregion

	#region Attribute helpers

	/// <summary>The candidate the inheritance walk settled on: the object it resolved against, that
	/// object's root..leaf path as metadata (values not yet read), and how it was reached.</summary>
	private sealed record InheritanceHit(long Owner, IReadOnlyList<(string LongName, AttrMetaRecord Meta)> Path, AttributeSource Source);

	/// <summary>
	/// PennMUSH's <c>atr_get_with_parent</c> (<c>src/attrib.c:1203-1278</c>), inside a single snapshot and
	/// on metadata alone: <see cref="WalkInheritance"/> under the name as given, then, when that found
	/// nothing anywhere, once more under the standard attribute the name means (<c>atr_match</c>, see
	/// <see cref="AttributeNameMatch"/>). A <c>no_inherit</c> barrier ends the lookup before the retry
	/// (<c>return NULL</c>, <c>attrib.c:1240-1252</c>). With <paramref name="checkParent"/> false this is
	/// <c>atr_get_noparent</c> (<c>attrib.c:1293-1314</c>): the object alone, under the name and then its
	/// match. No value is read here; <see cref="BuildInheritanceHit{T,TResult}"/> hydrates the winner alone.
	/// </summary>
	private InheritanceHit? ResolveInheritance(ITx tx, long dbref, string[] path, bool checkParent, InheritanceWalk walk)
	{
		var hit = WalkInheritance(tx, dbref, path, checkParent, walk, out var barrier);
		if (hit is not null || barrier)
		{
			return hit;
		}

		var name = string.Join('`', path);
		var match = AttributeNameMatch.Match(name,
			candidate => ReadAttributeEntryRecord(tx, candidate)?.DefaultFlags,
			prefix => tx.Range(Tables.AttrEntry, Keys.Upper(prefix))
				.Select(entry => Codec.Deserialize<AttributeEntryRecord>(entry.Value).Name));

		return match is null || match.Equals(name, StringComparison.OrdinalIgnoreCase)
			? null
			: WalkInheritance(tx, dbref, match.Split('`'), checkParent, walk, out _);
	}

	/// <summary>
	/// One pass of <c>atr_get_with_parent</c>'s target loop (<c>attrib.c:1218-1270</c>). The object, then
	/// <c>Parent()</c> repeatedly; when the chain ends, the type ancestor and its own parents. Each leg
	/// visits at most <see cref="InheritanceWalk.MaxParents"/> objects counting where it starts, so a chain
	/// that long never reaches the ancestor (the loop ends with <c>target</c> still good). An ancestor met
	/// in the explicit chain is used there and not visited again (<c>attrib.c:1226</c>).
	/// <para>
	/// On every target but the object, <c>no_inherit</c> on any existing prefix of the path — or the leaf
	/// — is a barrier: the lookup ends with nothing (<paramref name="barrier"/> set). A target holding only
	/// part of the path is passed over (<c>goto continue_target</c>).
	/// </para>
	/// </summary>
	private InheritanceHit? WalkInheritance(ITx tx, long obj, string[] path, bool checkParent, InheritanceWalk walk,
		out bool barrier)
	{
		barrier = false;
		var self = ReadPathPrefixes(tx, obj, path);
		if (self.Count == path.Length)
		{
			return new InheritanceHit(obj, self, AttributeSource.Self);
		}

		if (!checkParent)
		{
			return null;
		}

		// An object that is its own type ancestor does not visit itself twice (attrib.c:1226).
		var ancestor = walk.Ancestor is { } given && given.Number != obj ? (long?)given.Number : null;
		var source = AttributeSource.Parent;
		var depth = 1;
		var target = GetSingleEdge(tx, Tables.Parent.Forward, obj);
		if (target is null)
		{
			depth = 0;
			target = ancestor;
			source = AttributeSource.Ancestor;
		}

		while (depth < walk.MaxParents && target is { } current)
		{
			if (current == ancestor)
			{
				ancestor = null;
			}

			var prefixes = current == obj ? self : ReadPathPrefixes(tx, current, path);
			if (current != obj && prefixes.Any(entry => IsNoInheritMeta(tx, entry.Meta)))
			{
				barrier = true;
				return null;
			}

			if (prefixes.Count == path.Length)
			{
				return new InheritanceHit(current, prefixes, current == obj ? AttributeSource.Self : source);
			}

			depth++;
			target = GetSingleEdge(tx, Tables.Parent.Forward, current);
			if (target is null)
			{
				depth = 0;
				target = ancestor;
				source = AttributeSource.Ancestor;
			}
		}

		return null;
	}

	/// <summary>
	/// Hydrates the winning candidate's path — the only values an inherited read reads — and builds the
	/// result: a self hit keeps every flag of its leaf, an inherited one only the inheritable ones.
	/// </summary>
	private static TResult BuildInheritanceHit<T, TResult>(
		ITx tx,
		InheritanceHit hit,
		Func<ITx, long, string, AttrMetaRecord, T> hydrate,
		Func<T, IEnumerable<SharpAttributeFlag>> flagsOf,
		Func<T[], DBRef, AttributeSource, IEnumerable<SharpAttributeFlag>, TResult> build)
	{
		T[] attributes = [.. hit.Path.Select(entry => hydrate(tx, hit.Owner, entry.LongName, entry.Meta))];
		var flags = flagsOf(attributes[^1]);
		return build(attributes, new DBRef((int)hit.Owner), hit.Source,
			hit.Source == AttributeSource.Self ? flags : flags.Where(flag => flag.Inheritable));
	}

	/// <summary>
	/// <see cref="SharpAttributeExtensions.IsNoInherit(SharpAttribute)"/> on a metadata row, without
	/// hydrating it: the same answer, because hydration resolves each stored flag name through
	/// <see cref="Tables.AttrFlag"/> (dropping a name whose definition is gone) and the test then looks for
	/// a resolved flag named <c>no_inherit</c>.
	/// </summary>
	private bool IsNoInheritMeta(ITx tx, AttrMetaRecord meta)
	{
		var definitions = AttributeFlagDefinitions(tx);
		return meta.Flags.Any(name => definitions.ByName(name) is { } record
			&& record.Name.Equals("no_inherit", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Point-reads each prefix of <paramref name="path"/> in turn and stops at the first segment with no
	/// row. A result shorter than <paramref name="path"/> means the walk stopped early — the caller decides
	/// whether that is a miss (<see cref="GetAttributeAsync"/>) or a usable partial (the inheritance walk).
	/// </summary>
	internal IReadOnlyList<(string LongName, AttrMetaRecord Meta)> ReadPathPrefixes(ITx tx, long dbref, string[] path)
	{
		ReadStats.PathWalk();
		var resolved = new List<(string, AttrMetaRecord)>(path.Length);
		var longName = string.Empty;
		for (var level = 0; level < path.Length; level++)
		{
			var segment = path[level].ToUpperInvariant();
			longName = level == 0 ? segment : $"{longName}`{segment}";
			ReadStats.MetaRowRead();
			if (!tx.TryGet(Tables.AttrMeta, Keys.Attr(dbref, longName), out var bytes))
			{
				break;
			}

			resolved.Add((longName, Codec.Deserialize<AttrMetaRecord>(bytes)));
		}

		return resolved;
	}

	/// <summary>
	/// Builds the Library model for one attribute row. Owner, entry and child list are lazy loaders that
	/// open their own <see cref="LightningStore.Read{T}"/> when asked — never the <paramref name="tx"/>
	/// passed here, which is only valid for the duration of the call that produced
	/// <paramref name="meta"/>. Flags resolve through <see cref="Tables.AttrFlag"/> inside this call
	/// because the row is already open.
	/// </summary>
	internal SharpAttribute HydrateAttribute(ITx tx, long dbref, string longName, AttrMetaRecord meta, byte[]? value)
		=> HydrateAttribute(AttributeFlagDefinitions(tx), dbref, longName, meta, value);

	/// <inheritdoc cref="HydrateAttribute(ITx, long, string, AttrMetaRecord, byte[])"/>
	private SharpAttribute HydrateAttribute(DefinitionMap<AttributeFlagRecord> flagDefinitions, long dbref, string longName,
		AttrMetaRecord meta, byte[]? value)
		=> new(
			AttributeIdOf(dbref, longName),
			AttributeKeyOf(dbref, longName),
			LeafNameOf(longName),
			ReadAttributeFlags(flagDefinitions, meta.Flags),
			null,
			longName,
			new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(_ => Task.FromResult<IAsyncEnumerable<SharpAttribute>>(
				new FreshAsyncEnumerable<SharpAttribute>(ct => ChildAttributesCoreAsync(dbref, longName, ct)))),
			new AsyncLazy<SharpPlayer?>(_ => Task.FromResult(LoadAttributeOwner(meta.Owner))),
			new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult(LoadAttributeEntry(meta.Entry))))
		{
			Value = DeserializeValue(value)
		};

	/// <inheritdoc cref="HydrateAttribute"/>
	internal LazySharpAttribute HydrateLazyAttribute(ITx tx, long dbref, string longName, AttrMetaRecord meta)
		=> HydrateLazyAttribute(AttributeFlagDefinitions(tx), dbref, longName, meta);

	/// <inheritdoc cref="HydrateAttribute(ITx, long, string, AttrMetaRecord, byte[])"/>
	private LazySharpAttribute HydrateLazyAttribute(DefinitionMap<AttributeFlagRecord> flagDefinitions, long dbref, string longName,
		AttrMetaRecord meta)
		=> new(
			AttributeIdOf(dbref, longName),
			AttributeKeyOf(dbref, longName),
			LeafNameOf(longName),
			ReadAttributeFlags(flagDefinitions, meta.Flags),
			null,
			longName,
			new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(_ => Task.FromResult<IAsyncEnumerable<LazySharpAttribute>>(
				new FreshAsyncEnumerable<LazySharpAttribute>(ct => ChildLazyAttributesCoreAsync(dbref, longName, ct)))),
			new AsyncLazy<SharpPlayer?>(_ => Task.FromResult(LoadAttributeOwner(meta.Owner))),
			new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult(LoadAttributeEntry(meta.Entry))),
			// The whole point of the lazy shape: the attr.val row stays unread until someone asks.
			// Resettable, so a content scan can drop a body once it has been tested
			// (LazySharpAttributeExtensions.ReadValueOnceAsync) instead of keeping every one it read.
			Value: new AsyncLazy<MString>(_ => Task.FromResult(
				Store.Read(readTx => DeserializeValue(ReadAttributeValue(readTx, dbref, longName)))), resettable: true));

	private byte[]? ReadAttributeValue(ITx tx, long dbref, string longName)
	{
		var value = tx.TryGet(Tables.AttrVal, Keys.Attr(dbref, longName), out var bytes) ? bytes : null;
		ReadStats.ValueRead(value?.Length ?? 0);
		return value;
	}

	private static MString DeserializeValue(byte[]? value)
		=> value is null ? MarkupText.Empty : MarkupTextSerializer.Deserialize(Keys.ReadStr(value));

	/// <summary>Resolves stored flag names through <see cref="Tables.AttrFlag"/>; a name whose definition
	/// has since been deleted is dropped rather than surfaced as null, matching <c>ReadObjectFlags</c>.</summary>
	private static SharpAttributeFlag[] ReadAttributeFlags(DefinitionMap<AttributeFlagRecord> definitions, string[] names)
		=> names.Length == 0 ? [] : [.. names.Select(definitions.ByName).OfType<AttributeFlagRecord>().Select(MapAttributeFlag)];

	private SharpPlayer? LoadAttributeOwner(long? owner)
	{
		if (owner is not { } ownerDbref)
		{
			return null;
		}

		return Store.Read<SharpPlayer?>(tx =>
		{
			var found = ReadObject(tx, ownerDbref);
			return found is null
				? null
				: Hydrate(found.Value.Dbref, found.Value.Record) is SharpPlayer owner
					? owner
					: throw new InvalidOperationException($"The owner of #{ownerDbref} is not a player");
		});
	}

	private SharpAttributeEntry? LoadAttributeEntry(string? name)
		=> name is null ? null : Store.Read(tx => ReadAttributeEntry(tx, name));

	private static SharpAttributeEntry? ReadAttributeEntry(ITx tx, string name)
		=> ReadAttributeEntryRecord(tx, name) is { } record ? MapEntry(record) : null;

	private static AttributeEntryRecord? ReadAttributeEntryRecord(ITx tx, string name)
		=> tx.TryGet(Tables.AttrEntry, Keys.Upper(name), out var bytes) ? Codec.Deserialize<AttributeEntryRecord>(bytes) : null;

	/// <summary>The direct children of <paramref name="longName"/>, in key order; see <see cref="DirectChildrenAsync{T}"/>.</summary>
	private IAsyncEnumerable<SharpAttribute> ChildAttributesCoreAsync(long dbref, string longName, CancellationToken ct)
		=> DirectChildrenAsync(Keys.Attr(dbref, longName + "`"),
			(tx, childName, meta) => HydrateAttribute(tx, dbref, childName, meta, ReadAttributeValue(tx, dbref, childName)), ct);

	/// <inheritdoc cref="ChildAttributesCoreAsync"/>
	private IAsyncEnumerable<LazySharpAttribute> ChildLazyAttributesCoreAsync(long dbref, string longName, CancellationToken ct)
		=> DirectChildrenAsync(Keys.Attr(dbref, longName + "`"),
			(tx, childName, meta) => HydrateLazyAttribute(tx, dbref, childName, meta), ct);

	/// <summary>Top-level attributes of an object: the direct children of the object's attribute space.</summary>
	internal IAsyncEnumerable<SharpAttribute> TopLevelAttributesCoreAsync(long dbref, CancellationToken ct)
		=> DirectChildrenAsync(Keys.AttrPrefix(dbref),
			(tx, name, meta) => HydrateAttribute(tx, dbref, name, meta, ReadAttributeValue(tx, dbref, name)), ct);

	/// <inheritdoc cref="TopLevelAttributesCoreAsync"/>
	internal IAsyncEnumerable<LazySharpAttribute> TopLevelLazyAttributesCoreAsync(long dbref, CancellationToken ct)
		=> DirectChildrenAsync(Keys.AttrPrefix(dbref),
			(tx, name, meta) => HydrateLazyAttribute(tx, dbref, name, meta), ct);

	/// <summary>How many direct children one read transaction collects before the stream yields them.</summary>
	internal const int ChildPageSize = 256;

	/// <summary>
	/// The rows directly under <paramref name="parentPrefix"/> — <c>dbref·0x00</c> for the top level,
	/// <c>dbref·0x00·LONGNAME`</c> for a branch — without reading their descendants. Key order is preorder,
	/// so a child's subtree is the contiguous run of keys starting <c>CHILD`</c>; on meeting the first of
	/// them the cursor seeks straight past the run (the same key with its backtick raised to the next byte)
	/// instead of stepping through it. A sibling that shares the child's prefix but sorts before the
	/// backtick (<c>FOO_X</c>, <c>FOO1</c> beside <c>FOO</c>) lies between the child and its subtree and is
	/// read normally. Each page of <see cref="ChildPageSize"/> children is one read transaction, hydrated
	/// inside it; the next page starts at the first child the previous one did not take, so a wide branch
	/// is never held in one long snapshot.
	/// </summary>
	private async IAsyncEnumerable<T> DirectChildrenAsync<T>(byte[] parentPrefix, Func<ITx, string, AttrMetaRecord, T> hydrate,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		byte[]? start = parentPrefix;
		while (start is not null)
		{
			ct.ThrowIfCancellationRequested();
			var from = start;
			var (page, resume) = Store.Read(tx =>
			{
				var (rows, next) = ReadDirectChildPage(tx, parentPrefix, from, ChildPageSize);
				return (rows.Select(row => hydrate(tx, row.LongName, row.Meta)).ToList(), next);
			});

			foreach (var item in page)
			{
				ct.ThrowIfCancellationRequested();
				yield return item;
			}

			start = resume;
		}
	}

	/// <summary>One page of <see cref="DirectChildrenAsync{T}"/>: up to <paramref name="pageSize"/> child rows
	/// from <paramref name="start"/> on, and the key the next page starts at (null when there is none).</summary>
	private (List<(string LongName, AttrMetaRecord Meta)> Rows, byte[]? Resume) ReadDirectChildPage(ITx tx, byte[] parentPrefix,
		byte[] start, int pageSize)
	{
		var rows = new List<(string, AttrMetaRecord)>();
		var seek = start;
		while (true)
		{
			byte[]? skipTo = null;
			foreach (var (key, value) in tx.RangeFromKey(Tables.AttrMeta, seek))
			{
				ReadStats.MetaRowRead();
				if (!Keys.StartsWith(key, parentPrefix))
				{
					return (rows, null);
				}

				var tick = key.AsSpan(parentPrefix.Length).IndexOf((byte)'`');
				if (tick >= 0)
				{
					skipTo = key[..(parentPrefix.Length + tick + 1)];
					skipTo[^1]++;
					break;
				}

				if (rows.Count == pageSize)
				{
					return (rows, key);
				}

				rows.Add((Keys.ParseAttr(key).LongName, Codec.Deserialize<AttrMetaRecord>(value)));
			}

			if (skipTo is null)
			{
				return (rows, null);
			}

			seek = skipTo;
		}
	}

	/// <summary>Every attribute of an object, in preorder — which is simply key order.</summary>
	/// <remarks>Hydrated inside each page's own transaction, as <see cref="ScanAttributesCoreAsync"/> is.</remarks>
	internal IAsyncEnumerable<SharpAttribute> AllAttributesCoreAsync(long dbref, CancellationToken ct)
		=> Store.RangeMapAsync(Tables.AttrMeta, Keys.AttrPrefix(dbref), (tx, key, value) =>
		{
			var longName = Keys.ParseAttr(key).LongName;
			return HydrateAttribute(tx, dbref, longName, Codec.Deserialize<AttrMetaRecord>(value), ReadAttributeValue(tx, dbref, longName));
		}, ct: ct);

	/// <inheritdoc cref="AllAttributesCoreAsync"/>
	internal IAsyncEnumerable<LazySharpAttribute> AllLazyAttributesCoreAsync(long dbref, CancellationToken ct)
		=> Store.RangeMapAsync(Tables.AttrMeta, Keys.AttrPrefix(dbref),
			(tx, key, value) => HydrateLazyAttribute(tx, dbref, Keys.ParseAttr(key).LongName, Codec.Deserialize<AttrMetaRecord>(value)),
			ct: ct);

	private static bool HasChildren(ITx tx, long dbref, string longName)
		=> tx.Range(Tables.AttrMeta, Keys.Attr(dbref, longName + "`")).Any();

	/// <summary>After a node goes away, the parent stops being a branch if nothing else hangs off it.</summary>
	private static void DropParentBranchWhenChildless(ITx tx, long dbref, string[] path)
	{
		if (path.Length < 2)
		{
			return;
		}

		var parentLongName = string.Join('`', path.AsSpan(..^1));
		if (HasChildren(tx, dbref, parentLongName))
		{
			return;
		}

		var parentKey = Keys.Attr(dbref, parentLongName);
		if (!tx.TryGet(Tables.AttrMeta, parentKey, out var bytes))
		{
			return;
		}

		var parent = Codec.Deserialize<AttrMetaRecord>(bytes);
		var flags = new List<string>(parent.Flags);
		if (RemoveFlag(flags, BranchFlag))
		{
			tx.Put(Tables.AttrMeta, parentKey, Codec.Serialize(parent with { Flags = [.. flags] }));
		}
	}

	private async ValueTask<bool> UpdateLeafFlagsAsync(long dbref, string[] attribute, Func<List<string>, bool> mutate,
		CancellationToken ct)
	{
		var path = attribute.Select(segment => segment.ToUpperInvariant()).ToArray();
		return await Store.WriteAsync(tx =>
		{
			var walk = ReadPathPrefixes(tx, dbref, path);
			if (walk.Count == 0 || walk.Count != path.Length)
			{
				return false;
			}

			ApplyFlagChange(tx, dbref, walk[^1].LongName, walk[^1].Meta, mutate);
			return true;
		}, ct);
	}

	private async ValueTask UpdateFlagsAsync(long dbref, string longName, Func<List<string>, bool> mutate, CancellationToken ct)
		=> await Store.WriteAsync(tx =>
		{
			if (tx.TryGet(Tables.AttrMeta, Keys.Attr(dbref, longName), out var bytes))
			{
				ApplyFlagChange(tx, dbref, longName, Codec.Deserialize<AttrMetaRecord>(bytes), mutate);
			}
		}, ct);

	private static void ApplyFlagChange(ITx tx, long dbref, string longName, AttrMetaRecord meta, Func<List<string>, bool> mutate)
	{
		var flags = new List<string>(meta.Flags);
		if (mutate(flags))
		{
			tx.Put(Tables.AttrMeta, Keys.Attr(dbref, longName), Codec.Serialize(meta with { Flags = [.. flags] }));
		}
	}

	private static bool AddFlag(List<string> flags, string name)
	{
		if (flags.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		flags.Add(name);
		return true;
	}

	private static bool RemoveFlag(List<string> flags, string name)
		=> flags.RemoveAll(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) > 0;

	private static SharpAttributeFlag MapAttributeFlag(AttributeFlagRecord record) => new()
	{
		Id = $"AttributeFlag/{record.Name}",
		Key = record.Name,
		Name = record.Name,
		Symbol = record.Symbol,
		System = record.System,
		Inheritable = record.Inheritable
	};

	private static SharpAttributeEntry MapEntry(AttributeEntryRecord record) => new()
	{
		Id = $"AttributeEntry/{record.Name}",
		Name = record.Name,
		DefaultFlags = record.DefaultFlags,
		Limit = string.IsNullOrEmpty(record.Limit) ? null : record.Limit,
		Enum = record.Enum is { Length: > 0 } ? record.Enum : null,
		EnumDelimiter = record.EnumDelimiter is { Length: 1 } delimiter ? delimiter[0] : ' '
	};

	/// <summary>A space delimiter is the default and is not written, so older rows and new ones read alike.</summary>
	private static AttributeEntryRecord NewAttributeEntryRecord(string name, string[] defaultFlags, string? limit,
		string[]? enumValues, char enumDelimiter) => new()
		{
			Name = name,
			DefaultFlags = defaultFlags,
			Limit = limit,
			Enum = enumValues,
			EnumDelimiter = enumDelimiter == ' ' ? null : enumDelimiter.ToString()
		};

	/// <summary>The identity <see cref="SetAttributeFlagAsync(SharpAttribute,SharpAttributeFlag,CancellationToken)"/>
	/// reads back: <c>dbref_LONGNAME</c>, split at the first underscore (the dbref half is all digits, so a
	/// long name containing underscores is unambiguous).</summary>
	private static string AttributeKeyOf(long dbref, string longName) => $"{dbref}_{longName}";

	private static string AttributeIdOf(long dbref, string longName) => $"Attribute/{AttributeKeyOf(dbref, longName)}";

	private static (long Dbref, string LongName) ParseAttributeKey(string key)
	{
		var bare = key.StartsWith("Attribute/", StringComparison.Ordinal) ? key["Attribute/".Length..] : key;
		var split = bare.IndexOf('_');
		return (long.Parse(bare[..split]), bare[(split + 1)..]);
	}

	private static string LeafNameOf(string longName)
	{
		var last = longName.LastIndexOf('`');
		return last < 0 ? longName : longName[(last + 1)..];
	}

	/// <summary>The leading run of the glob that is literal — everything before the first wildcard — which
	/// is the key prefix the range can seek to. Purely an index hint; the regex still decides matches.</summary>
	private static string LiteralPrefixOf(string pattern)
	{
		var wildcard = pattern.IndexOfAny(['*', '?']);
		return wildcard < 0 ? pattern : pattern[..wildcard];
	}

	/// <summary>
	/// The backtick-aware attribute-name glob dialect: <c>**</c> crosses tree levels, a single
	/// <c>*</c> stays inside one (<c>[^`]*</c>), <c>?</c> is one character, every other regex
	/// metacharacter is escaped, and a trailing backtick means "direct children only".
	/// </summary>
	/// <remarks>
	/// This is deliberately NOT the general MUSH wildcard (<c>MushText.Glob.ToRegex</c>), where
	/// <c>*</c> matches anything including a backtick. Routing attribute matching at that dialect
	/// makes a single <c>*</c> cross tree levels, so <c>obj/*</c> silently starts returning
	/// grandchildren. The two dialects stay separate on purpose.
	/// </remarks>
	internal static Regex GlobToRegex(string pattern)
	{
		var converted = WildcardToRegex().Replace(pattern, m => m.Value switch
		{
			"**" => ".*",
			"*" => "[^`]*",
			"?" => ".",
			_ => $"\\{m.Value}"
		});

		if (converted.EndsWith('`'))
		{
			converted += "[^`]+";
		}

		return SoftcodeRegex.Create($"^{converted}$", NameRegexOptions);
	}

	/// <summary>The regex readers take their pattern raw — no wildcard conversion and no anchoring; a
	/// caller wanting "everything" passes <c>.*</c>. An invalid pattern throws
	/// <see cref="RegexParseException"/>.</summary>
	private static Regex RawRegex(string pattern) => SoftcodeRegex.Create(pattern, NameRegexOptions);

	/// <summary>
	/// Attribute names match case-insensitively and culture-invariantly. Built through
	/// <see cref="SoftcodeRegex"/>, like every other pattern whose text came from softcode: each match is
	/// time-bounded, and the same text is compiled once and shared across scans.
	/// </summary>
	private const RegexOptions NameRegexOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

	[GeneratedRegex(@"\*\*|[.*+?^${}()|[\]/\\]")]
	private static partial Regex WildcardToRegex();

	#endregion
}
