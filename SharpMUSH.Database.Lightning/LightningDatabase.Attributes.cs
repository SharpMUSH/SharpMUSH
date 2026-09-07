using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DotNext.Threading;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

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
/// Semantics are ported from <c>SurrealDatabase.Attributes.cs</c>. The inheritance members
/// (<see cref="GetAttributeWithInheritanceAsync"/>, <see cref="GetLazyAttributeWithInheritanceAsync"/>)
/// resolve their whole candidate set inside one snapshot — see
/// <see cref="CollectInheritanceCandidates{T}"/>.
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
	/// </summary>
	private async IAsyncEnumerable<SharpAttribute> ScanAttributesCoreAsync(DBRef dbref, string literalPrefix, Regex filter,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var n = (long)dbref.Number;
		await foreach (var (key, value) in Store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(n, literalPrefix), ct: ct))
		{
			var longName = Keys.ParseAttr(key).LongName;
			if (!filter.IsMatch(longName))
			{
				continue;
			}

			var meta = Codec.Deserialize<AttrMetaRecord>(value);
			yield return Store.Read(tx => HydrateAttribute(tx, n, longName, meta, ReadAttributeValue(tx, n, longName)));
		}
	}

	/// <inheritdoc cref="ScanAttributesCoreAsync"/>
	private async IAsyncEnumerable<LazySharpAttribute> ScanLazyAttributesCoreAsync(DBRef dbref, string literalPrefix, Regex filter,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var n = (long)dbref.Number;
		await foreach (var (key, value) in Store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(n, literalPrefix), ct: ct))
		{
			var longName = Keys.ParseAttr(key).LongName;
			if (!filter.IsMatch(longName))
			{
				continue;
			}

			var meta = Codec.Deserialize<AttrMetaRecord>(value);
			yield return Store.Read(tx => HydrateLazyAttribute(tx, n, longName, meta));
		}
	}

	public IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AttributeWithInheritance>(ct =>
			GetAttributeWithInheritanceCoreAsync(dbref, attribute, checkParent, ct));

	private async IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceCoreAsync(DBRef dbref, string[] attribute,
		bool checkParent, [EnumeratorCancellation] CancellationToken ct)
	{
		var candidates = Store.Read(tx => CollectInheritanceCandidates(tx, (long)dbref.Number, attribute, checkParent,
			(readTx, owner, longName, meta) => HydrateAttribute(readTx, owner, longName, meta, ReadAttributeValue(readTx, owner, longName))));

		var resolved = ResolveInheritance(candidates, attribute.Length, static a => a.IsNoInherit(), static a => a.Flags,
			static (attrs, source, kind, flags) => new AttributeWithInheritance(attrs, source, kind, flags));

		if (resolved is not null)
		{
			ct.ThrowIfCancellationRequested();
			yield return resolved;
		}
	}

	public IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<LazyAttributeWithInheritance>(ct =>
			GetLazyAttributeWithInheritanceCoreAsync(dbref, attribute, checkParent, ct));

	/// <inheritdoc cref="GetAttributeWithInheritanceCoreAsync"/>
	private async IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceCoreAsync(DBRef dbref, string[] attribute,
		bool checkParent, [EnumeratorCancellation] CancellationToken ct)
	{
		var candidates = Store.Read(tx => CollectInheritanceCandidates(tx, (long)dbref.Number, attribute, checkParent, HydrateLazyAttribute));

		var resolved = ResolveInheritance(candidates, attribute.Length, static a => a.IsNoInherit(), static a => a.Flags,
			static (attrs, source, kind, flags) => new LazyAttributeWithInheritance(attrs, source, kind, flags));

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
		string? limit = null, string[]? enumValues = null, CancellationToken cancellationToken = default)
	{
		var record = new AttributeEntryRecord
		{
			Name = name,
			DefaultFlags = defaultFlags,
			Limit = limit,
			Enum = enumValues
		};

		await Store.WriteAsync(tx => tx.Put(Tables.AttrEntry, Keys.Upper(name), Codec.Serialize(record)), cancellationToken);
		return MapEntry(record);
	}

	public async ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.AttrEntry, Keys.Upper(name)), cancellationToken);

	#endregion

	#region Attribute writes

	/// <summary>
	/// One write job for the whole path. Every prefix of <paramref name="attribute"/> that has no
	/// <see cref="Tables.AttrMeta"/> row is created — owned by <paramref name="owner"/>, carrying whatever
	/// default flags <see cref="Tables.AttrEntry"/> configures for that level's long name, and holding an
	/// empty value; the leaf then takes <paramref name="value"/>.
	/// <para>
	/// Overwriting an existing node matches <c>SurrealDatabase.Attributes.cs</c> exactly: flags are only
	/// ever added, never cleared (its <c>RELATE … WHERE id NOT IN …</c> guards at lines 327-344), and the
	/// owner is re-pointed at the setter on <em>every</em> level of the path, leaf and auto-created branch
	/// alike (its delete-then-relate of <c>has_attribute_owner</c> at lines 318-325). The <c>branch</c>
	/// flag lands on every non-leaf level that lacks it, so a leaf that grows a child becomes a branch.
	/// </para>
	/// </summary>
	public async ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner,
		CancellationToken cancellationToken = default)
	{
		var path = attribute.Select(segment => segment.ToUpperInvariant()).ToArray();
		if (path.Length == 0)
		{
			return false;
		}

		var n = (long)dbref.Number;
		var ownerDbref = (long)owner.Object.Key;
		var serialized = Keys.Str(MarkupTextSerializer.Serialize(value));
		var empty = Keys.Str(MarkupTextSerializer.Serialize(MarkupText.Empty));

		return await Store.WriteAsync(tx =>
		{
			if (ReadObject(tx, n) is null)
			{
				return false;
			}

			for (var level = 0; level < path.Length; level++)
			{
				var longName = string.Join('`', path.Take(level + 1));
				var key = Keys.Attr(n, longName);
				var isLeaf = level == path.Length - 1;
				var existing = tx.TryGet(Tables.AttrMeta, key, out var bytes) ? Codec.Deserialize<AttrMetaRecord>(bytes) : null;
				var entry = ReadAttributeEntryRecord(tx, longName);

				var flags = new List<string>(existing?.Flags ?? []);
				foreach (var flagName in entry?.DefaultFlags ?? [])
				{
					AddFlag(flags, flagName);
				}

				if (!isLeaf)
				{
					AddFlag(flags, BranchFlag);
				}

				tx.Put(Tables.AttrMeta, key, Codec.Serialize(new AttrMetaRecord
				{
					Owner = ownerDbref,
					Flags = [.. flags],
					Entry = entry?.Name ?? existing?.Entry
				}));

				// A node that already exists keeps its value unless it is the leaf being set — the same
				// `value = value ?? ''` an ancestor upsert gets in the SurrealDB provider.
				if (isLeaf)
				{
					tx.Put(Tables.AttrVal, key, serialized);
				}
				else if (existing is null)
				{
					tx.Put(Tables.AttrVal, key, empty);
				}
			}

			return true;
		}, cancellationToken);
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
		=> new(Store.Read(tx => tx.TryGet(Tables.AttrFlag, Keys.Upper(flagName), out var bytes)
			? MapAttributeFlag(Codec.Deserialize<AttributeFlagRecord>(bytes))
			: null));

	public IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpAttributeFlag>(GetAttributeFlagsCoreAsync);

	private async IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		var flags = Store.Read(tx => tx.Range(Tables.AttrFlag, [])
			.Select(entry => MapAttributeFlag(Codec.Deserialize<AttributeFlagRecord>(entry.Value)))
			.ToList());

		foreach (var flag in flags)
		{
			ct.ThrowIfCancellationRequested();
			yield return flag;
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
				return true;
			}

			tx.Delete(Tables.AttrMeta, key);
			tx.Delete(Tables.AttrVal, key);
			DropParentBranchWhenChildless(tx, n, path);
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
			return true;
		}, cancellationToken);
	}

	#endregion

	#region Attribute helpers

	/// <summary>
	/// The bound ArangoDB spells as <c>1..100 OUTBOUND</c> on its parent and zone graphs
	/// (<c>ArangoDatabase.Attributes.cs:936, 953-954</c>) and SurrealDB as its <c>GetParentChainAsync</c>
	/// loop counter.
	/// </summary>
	private const int InheritanceHopLimit = 100;

	/// <summary>One object the inheritance walk may resolve against, carrying whatever prefix of the
	/// requested path actually exists on it — possibly shorter than the path, which is what makes the
	/// no_inherit gate below able to fire on a branch that has no leaf.</summary>
	private sealed record InheritanceCandidate<T>(DBRef Source, T[] Attributes);

	/// <summary>The three candidate groups ArangoDB's single query returns (<c>{ self, parents, zones }</c>,
	/// <c>ArangoDatabase.Attributes.cs:971</c>), gathered here from one LMDB snapshot instead.</summary>
	private sealed record InheritanceCandidates<T>(
		InheritanceCandidate<T>? Self,
		List<InheritanceCandidate<T>> Parents,
		List<InheritanceCandidate<T>> Zones);

	/// <summary>
	/// Everything the inheritance walk needs, read inside a single snapshot: the object itself, then its
	/// parent chain, then the zone chain of every member of <c>[self, parent, grandparent, …]</c> in that
	/// order. Mirrors ArangoDB's <c>selfAttrs</c>/<c>parentCandidates</c>/<c>zoneCandidates</c>
	/// (<c>ArangoDatabase.Attributes.cs:923-970</c>) including their two short-circuits: a complete hit on
	/// the object itself suppresses both inherited groups, and a candidate with no prefix at all is
	/// dropped (their <c>FILTER LENGTH(...) &gt; 0</c>).
	/// </summary>
	private InheritanceCandidates<T> CollectInheritanceCandidates<T>(ITx tx, long dbref, string[] path, bool checkParent,
		Func<ITx, long, string, AttrMetaRecord, T> hydrate)
	{
		InheritanceCandidate<T> CandidateOf(long owner) => new(new DBRef((int)owner),
			[.. ReadPathPrefixes(tx, owner, path).Select(entry => hydrate(tx, owner, entry.LongName, entry.Meta))]);

		var self = CandidateOf(dbref);
		if (self.Attributes.Length == path.Length)
		{
			return new InheritanceCandidates<T>(self, [], []);
		}

		if (!checkParent)
		{
			return new InheritanceCandidates<T>(null, [], []);
		}

		var chain = EdgeChain(tx, Tables.Parent.Forward, dbref);

		return new InheritanceCandidates<T>(
			null,
			[.. chain.Skip(1).Select(CandidateOf).Where(candidate => candidate.Attributes.Length > 0)],
			[.. chain.SelectMany(member => EdgeChain(tx, Tables.Zone.Forward, member).Skip(1))
				.Select(CandidateOf).Where(candidate => candidate.Attributes.Length > 0)]);
	}

	/// <summary>
	/// Follows a single-valued edge from <paramref name="start"/>, returning <c>[start, next, next-of-next, …]</c>.
	/// A visited set and <see cref="InheritanceHopLimit"/> both bound it, so a parent (or zone) cycle
	/// terminates rather than spinning — the graph traversals this replaces get that from ArangoDB's
	/// path uniqueness and its <c>1..100</c> depth.
	/// </summary>
	private static List<long> EdgeChain(ITx tx, TableDef forward, long start)
	{
		var chain = new List<long> { start };
		var visited = new HashSet<long> { start };
		var current = start;

		for (var hop = 0; hop < InheritanceHopLimit; hop++)
		{
			if (GetSingleEdge(tx, forward, current) is not { } next || !visited.Add(next))
			{
				break;
			}

			chain.Add(next);
			current = next;
		}

		return chain;
	}

	/// <summary>
	/// The C# half of ArangoDB's inheritance walk, ported rather than referenced
	/// (<c>ArangoDatabase.Attributes.cs:996-1045</c> and its <c>EvaluateInheritanceCandidateAsync</c> at
	/// <c>1104-1118</c>): the object's own complete hit wins outright and keeps every flag; otherwise each
	/// parent then each zone is tested in order, where <c>no_inherit</c> anywhere on the candidate's
	/// existing prefix aborts the whole walk (PennMUSH <c>atr_get_with_parent</c>, <c>attrib.c:1232-1252</c>,
	/// returns NULL rather than falling through to a more distant ancestor) and only a prefix reaching the
	/// requested length is a match, contributing just its inheritable flags.
	/// </summary>
	private static TResult? ResolveInheritance<T, TResult>(
		InheritanceCandidates<T> candidates,
		int expectedLength,
		Func<T, bool> isNoInherit,
		Func<T, IEnumerable<SharpAttributeFlag>> flagsOf,
		Func<T[], DBRef, AttributeSource, IEnumerable<SharpAttributeFlag>, TResult> build)
		where TResult : class
	{
		if (candidates.Self is { } self)
		{
			return build(self.Attributes, self.Source, AttributeSource.Self, flagsOf(self.Attributes[^1]));
		}

		var inherited = candidates.Parents.Select(candidate => (candidate, Kind: AttributeSource.Parent))
			.Concat(candidates.Zones.Select(candidate => (candidate, Kind: AttributeSource.Zone)));

		foreach (var (candidate, kind) in inherited)
		{
			if (candidate.Attributes.Any(isNoInherit))
			{
				return null;
			}

			if (candidate.Attributes.Length == expectedLength)
			{
				return build(candidate.Attributes, candidate.Source, kind,
					flagsOf(candidate.Attributes[^1]).Where(flag => flag.Inheritable));
			}
		}

		return null;
	}

	/// <summary>
	/// Point-reads each prefix of <paramref name="path"/> in turn and stops at the first segment with no
	/// row. A result shorter than <paramref name="path"/> means the walk stopped early — the caller decides
	/// whether that is a miss (<see cref="GetAttributeAsync"/>) or a usable partial (the inheritance walk).
	/// </summary>
	internal IReadOnlyList<(string LongName, AttrMetaRecord Meta)> ReadPathPrefixes(ITx tx, long dbref, string[] path)
	{
		var resolved = new List<(string, AttrMetaRecord)>(path.Length);
		for (var level = 0; level < path.Length; level++)
		{
			var longName = string.Join('`', path.Take(level + 1)).ToUpperInvariant();
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
		=> new(
			AttributeIdOf(dbref, longName),
			AttributeKeyOf(dbref, longName),
			LeafNameOf(longName),
			ReadAttributeFlags(tx, meta.Flags),
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
		=> new(
			AttributeIdOf(dbref, longName),
			AttributeKeyOf(dbref, longName),
			LeafNameOf(longName),
			ReadAttributeFlags(tx, meta.Flags),
			null,
			longName,
			new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(_ => Task.FromResult<IAsyncEnumerable<LazySharpAttribute>>(
				new FreshAsyncEnumerable<LazySharpAttribute>(ct => ChildLazyAttributesCoreAsync(dbref, longName, ct)))),
			new AsyncLazy<SharpPlayer?>(_ => Task.FromResult(LoadAttributeOwner(meta.Owner))),
			new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult(LoadAttributeEntry(meta.Entry))),
			// The whole point of the lazy shape: the attr.val row stays unread until someone asks.
			Value: new AsyncLazy<MString>(_ => Task.FromResult(
				Store.Read(readTx => DeserializeValue(ReadAttributeValue(readTx, dbref, longName))))));

	private static byte[]? ReadAttributeValue(ITx tx, long dbref, string longName)
		=> tx.TryGet(Tables.AttrVal, Keys.Attr(dbref, longName), out var bytes) ? bytes : null;

	private static MString DeserializeValue(byte[]? value)
		=> value is null ? MarkupText.Empty : MarkupTextSerializer.Deserialize(Keys.ReadStr(value));

	/// <summary>Resolves stored flag names through <see cref="Tables.AttrFlag"/>; a name whose definition
	/// has since been deleted is dropped rather than surfaced as null, matching <c>ReadObjectFlags</c>.</summary>
	private static SharpAttributeFlag[] ReadAttributeFlags(ITx tx, string[] names)
		=> [.. names
			.Select(name => tx.TryGet(Tables.AttrFlag, Keys.Upper(name), out var bytes) ? Codec.Deserialize<AttributeFlagRecord>(bytes) : null)
			.Where(record => record is not null)
			.Select(record => MapAttributeFlag(record!))];

	private SharpPlayer? LoadAttributeOwner(long? owner)
	{
		if (owner is not { } ownerDbref)
		{
			return null;
		}

		return Store.Read<SharpPlayer?>(tx =>
		{
			var found = ReadObject(tx, ownerDbref);
			return found is null ? null : Hydrate(found.Value.Dbref, found.Value.Record).AsPlayer;
		});
	}

	private SharpAttributeEntry? LoadAttributeEntry(string? name)
		=> name is null ? null : Store.Read(tx => ReadAttributeEntry(tx, name));

	private static SharpAttributeEntry? ReadAttributeEntry(ITx tx, string name)
		=> ReadAttributeEntryRecord(tx, name) is { } record ? MapEntry(record) : null;

	private static AttributeEntryRecord? ReadAttributeEntryRecord(ITx tx, string name)
		=> tx.TryGet(Tables.AttrEntry, Keys.Upper(name), out var bytes) ? Codec.Deserialize<AttributeEntryRecord>(bytes) : null;

	/// <summary>The direct children of <paramref name="longName"/>: the <c>LONGNAME`</c> range, minus the
	/// grandchildren, which are exactly the keys carrying a further backtick.</summary>
	private async IAsyncEnumerable<SharpAttribute> ChildAttributesCoreAsync(long dbref, string longName,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var children = Store.Read(tx => ReadChildRows(tx, dbref, longName)
			.Select(child => HydrateAttribute(tx, dbref, child.LongName, child.Meta, ReadAttributeValue(tx, dbref, child.LongName)))
			.ToList());

		foreach (var child in children)
		{
			ct.ThrowIfCancellationRequested();
			yield return child;
		}
	}

	/// <inheritdoc cref="ChildAttributesCoreAsync"/>
	private async IAsyncEnumerable<LazySharpAttribute> ChildLazyAttributesCoreAsync(long dbref, string longName,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var children = Store.Read(tx => ReadChildRows(tx, dbref, longName)
			.Select(child => HydrateLazyAttribute(tx, dbref, child.LongName, child.Meta))
			.ToList());

		foreach (var child in children)
		{
			ct.ThrowIfCancellationRequested();
			yield return child;
		}
	}

	/// <summary>Top-level attributes of an object: the whole dbref range, minus everything with a backtick.</summary>
	internal async IAsyncEnumerable<SharpAttribute> TopLevelAttributesCoreAsync(long dbref, [EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (key, value) in Store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(dbref), ct: ct))
		{
			var longName = Keys.ParseAttr(key).LongName;
			if (longName.Contains('`'))
			{
				continue;
			}

			var meta = Codec.Deserialize<AttrMetaRecord>(value);
			yield return Store.Read(tx => HydrateAttribute(tx, dbref, longName, meta, ReadAttributeValue(tx, dbref, longName)));
		}
	}

	/// <inheritdoc cref="TopLevelAttributesCoreAsync"/>
	internal async IAsyncEnumerable<LazySharpAttribute> TopLevelLazyAttributesCoreAsync(long dbref, [EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (key, value) in Store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(dbref), ct: ct))
		{
			var longName = Keys.ParseAttr(key).LongName;
			if (longName.Contains('`'))
			{
				continue;
			}

			yield return Store.Read(tx => HydrateLazyAttribute(tx, dbref, longName, Codec.Deserialize<AttrMetaRecord>(value)));
		}
	}

	/// <summary>Every attribute of an object, in preorder — which is simply key order.</summary>
	internal async IAsyncEnumerable<SharpAttribute> AllAttributesCoreAsync(long dbref, [EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (key, value) in Store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(dbref), ct: ct))
		{
			var longName = Keys.ParseAttr(key).LongName;
			var meta = Codec.Deserialize<AttrMetaRecord>(value);
			yield return Store.Read(tx => HydrateAttribute(tx, dbref, longName, meta, ReadAttributeValue(tx, dbref, longName)));
		}
	}

	/// <inheritdoc cref="AllAttributesCoreAsync"/>
	internal async IAsyncEnumerable<LazySharpAttribute> AllLazyAttributesCoreAsync(long dbref, [EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (key, value) in Store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(dbref), ct: ct))
		{
			var longName = Keys.ParseAttr(key).LongName;
			yield return Store.Read(tx => HydrateLazyAttribute(tx, dbref, longName, Codec.Deserialize<AttrMetaRecord>(value)));
		}
	}

	private static List<(string LongName, AttrMetaRecord Meta)> ReadChildRows(ITx tx, long dbref, string longName)
		=> [.. tx.Range(Tables.AttrMeta, Keys.Attr(dbref, longName + "`"))
			.Select(entry => (LongName: Keys.ParseAttr(entry.Key).LongName, Value: entry.Value))
			.Where(entry => !entry.LongName.AsSpan(longName.Length + 1).Contains('`'))
			.Select(entry => (entry.LongName, Codec.Deserialize<AttrMetaRecord>(entry.Value)))];

	private static bool HasChildren(ITx tx, long dbref, string longName)
		=> tx.Range(Tables.AttrMeta, Keys.Attr(dbref, longName + "`")).Any();

	/// <summary>After a node goes away, the parent stops being a branch if nothing else hangs off it.</summary>
	private static void DropParentBranchWhenChildless(ITx tx, long dbref, string[] path)
	{
		if (path.Length < 2)
		{
			return;
		}

		var parentLongName = string.Join('`', path.Take(path.Length - 1));
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
		Enum = record.Enum is { Length: > 0 } ? record.Enum : null
	};

	/// <summary>The identity <see cref="SetAttributeFlagAsync(SharpAttribute,SharpAttributeFlag,CancellationToken)"/>
	/// reads back: <c>dbref_LONGNAME</c>, split at the first underscore (the dbref half is all digits, so a
	/// long name containing underscores is unambiguous). Shaped like the SurrealDB provider's attribute key.</summary>
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
	/// The backtick-aware glob conversion the other providers share (<c>ArangoDatabase.Attributes.cs</c>
	/// 297-344): <c>**</c> crosses tree levels, a single <c>*</c> stays inside one (<c>[^`]*</c>),
	/// <c>?</c> is one character, every other regex metacharacter is escaped, and a trailing backtick
	/// means "direct children only".
	/// </summary>
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

		return new Regex($"^{converted}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	/// <summary>The regex readers take their pattern raw — no wildcard conversion, no anchoring — exactly
	/// as ArangoDB's <c>GetAttributesByRegexAsync</c> does; a caller wanting "everything" passes <c>.*</c>.</summary>
	private static Regex RawRegex(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	[GeneratedRegex(@"\*\*|[.*+?^${}()|[\]/]")]
	private static partial Regex WildcardToRegex();

	#endregion
}
