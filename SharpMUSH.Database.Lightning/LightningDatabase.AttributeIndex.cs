using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SharpMUSH.Database;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAttributeIndexStore"/>: attribute indexes, kept in two tables.
/// <para>
/// <see cref="Tables.AttrIdx"/> holds <c>UPPER(long name) 0x00 &lt;part&gt;</c> → dbref, one duplicate per object, so
/// the objects holding one value are one duplicate run in dbref order and the values of one index are one key range
/// in their own order. A part is the folded value as UTF-8 (lower-cased unless the index is <c>exact</c>); in a
/// <c>numeric</c> index it starts with a class byte, <see cref="NumberClass"/> followed by the number as an
/// order-preserving eight-byte double, or <see cref="TextClass"/> followed by the text, so every number sorts before
/// every text. A part is cut at <see cref="IndexValueBytes"/> on a character boundary, and a probe that reaches the
/// cut is checked against the stored value: in full for equality and prefix, and on the bound's key for a range.
/// Values that share their first <see cref="IndexValueBytes"/> bytes share a key, so among themselves they sort by dbref.
/// </para>
/// <para>
/// <see cref="Tables.AttrIdxOf"/> holds an indexed copy's own key (<c>Keys.Attr</c>) → the parts it is listed under,
/// so a write replaces its old entries after one read, and a wipe or destroy finds an object's entries in the prefix
/// its attributes use. Both are written in the same transaction as the value, at every place the provider writes
/// one: <see cref="WriteAttributePath"/> (set, import, migration), <see cref="ClearAttributeAsync"/>,
/// <see cref="WipeAttributeAsync"/>, the flag writes (a copy flagged <c>internal</c> is never listed) and
/// <see cref="DeleteObjectAsync"/>. A write of an attribute with no index reads nothing more than it did: the entry
/// it already reads for default flags says whether there is one.
/// </para>
/// <para>
/// An object's type never changes once it is created, so a <c>type:</c> scope is decided once, when a copy is written.
/// </para>
/// </summary>
public partial class LightningDatabase : IAttributeIndexStore
{
	/// <summary>Bytes of a value kept in an index key; LMDB keys stop at 511 and the name comes first.</summary>
	internal const int IndexValueBytes = 400;

	/// <summary>A part shorter than this was never cut; one this long may have been (a cut backs off up to three
	/// bytes to a character boundary), so an equality probe this long checks the stored value.</summary>
	private const int RecheckFromBytes = IndexValueBytes - 3;

	/// <summary>The class byte of a number in a numeric index.</summary>
	internal const byte NumberClass = 0x01;

	/// <summary>The class byte of a value that is not a number in a numeric index.</summary>
	internal const byte TextClass = 0x02;

	private const string InternalFlag = "internal";

	#region Encoding

	/// <summary>The key prefix of every entry of the index on <paramref name="longName"/>.</summary>
	internal static byte[] IndexPrefix(string longName) => Keys.Concat(Keys.Upper(longName), Keys.Sep);

	private static byte[] IndexKey(byte[] prefix, ReadOnlySpan<byte> part)
	{
		var key = new byte[prefix.Length + part.Length];
		prefix.CopyTo(key, 0);
		part.CopyTo(key.AsSpan(prefix.Length));
		return key;
	}

	/// <summary>
	/// <c>isnum()</c>'s test, under the invariant culture the server runs in: a decimal number, with optional spaces
	/// around it, an optional sign and thousands separators; no exponent, hex or infinity.
	/// </summary>
	private static bool TryNumber(string text, out decimal number)
		=> decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

	/// <summary>The index part of one element of a value: folded, classed in a numeric index, and cut.</summary>
	internal static byte[] EncodePart(string element, AttributeIndexRecord index)
	{
		if (index.Numeric && TryNumber(element, out var number))
		{
			var part = new byte[9];
			part[0] = NumberClass;
			// -0 and 0 are one number, as eq() says.
			var value = (double)number;
			var bits = BitConverter.DoubleToInt64Bits(value == 0 ? 0d : value);
			var ordered = bits < 0 ? ~(ulong)bits : (ulong)bits ^ 0x8000_0000_0000_0000UL;
			BinaryPrimitives.WriteUInt64BigEndian(part.AsSpan(1), ordered);
			return part;
		}

		return EncodeTextPart(element, index);
	}

	/// <summary>
	/// A probe as text whatever it holds: what a prefix probe is, since a prefix of a number's digits says nothing about
	/// where the number sorts. In a numeric index it therefore matches text values only.
	/// </summary>
	internal static byte[] EncodeTextPart(string element, AttributeIndexRecord index)
	{
		var folded = index.Exact ? element : element.ToLowerInvariant();
		var text = CutText(Encoding.UTF8.GetBytes(folded), index.Numeric ? IndexValueBytes - 1 : IndexValueBytes);
		return index.Numeric ? [TextClass, .. text] : text;
	}

	/// <summary>The first <paramref name="limit"/> bytes of UTF-8 <paramref name="bytes"/>, backed off to a character boundary.</summary>
	private static byte[] CutText(byte[] bytes, int limit)
	{
		if (bytes.Length <= limit)
		{
			return bytes;
		}

		var cut = limit;
		while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;
		return bytes[..cut];
	}

	/// <summary>Whether a part's text may have been cut, so equality on it is checked against the stored value.</summary>
	private static bool ReachesLimit(byte[] part, AttributeIndexRecord index)
		=> index.Numeric
			? part.Length > 0 && part[0] == TextClass && part.Length - 1 >= RecheckFromBytes - 1
			: part.Length >= RecheckFromBytes;

	/// <summary>Every distinct part a value is listed under, in byte order; none for an empty value.</summary>
	internal static List<byte[]> PartsOf(string plain, AttributeIndexRecord index)
	{
		var parts = new List<byte[]>();
		foreach (var element in Options(index).Elements(plain))
		{
			var part = EncodePart(element, index);
			if (!parts.Any(existing => existing.AsSpan().SequenceEqual(part))) parts.Add(part);
		}

		parts.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));
		return parts;
	}

	/// <summary>
	/// Whether a stored value holds <paramref name="probe"/> as the index compares, without the cut: what an equality
	/// probe that reached the cut is checked against.
	/// </summary>
	private static bool HoldsExactly(string plain, string probe, AttributeIndexRecord index)
	{
		var wanted = Normalized(probe, index);
		return Options(index).Elements(plain).Any(element => Normalized(element, index) == wanted);
	}

	/// <summary>Whether an element of a stored value starts with <paramref name="probe"/>, as text in the index's case rule.</summary>
	private static bool StartsExactly(string plain, string probe, AttributeIndexRecord index)
	{
		var wanted = index.Exact ? probe : probe.ToLowerInvariant();
		return Options(index).Elements(plain)
			.Any(element => (index.Exact ? element : element.ToLowerInvariant()).StartsWith(wanted, StringComparison.Ordinal));
	}

	private static string Normalized(string element, AttributeIndexRecord index)
		=> index.Numeric && TryNumber(element, out var number)
			? "#" + ((double)number == 0 ? 0d : (double)number).ToString("R", CultureInfo.InvariantCulture)
			: "$" + (index.Exact ? element : element.ToLowerInvariant());

	/// <summary><c>u16 length, bytes</c> per part.</summary>
	private static byte[] EncodeParts(List<byte[]> parts)
	{
		var bytes = new byte[parts.Sum(part => part.Length + 2)];
		var offset = 0;
		foreach (var part in parts)
		{
			BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), (ushort)part.Length);
			part.CopyTo(bytes, offset + 2);
			offset += part.Length + 2;
		}

		return bytes;
	}

	private static List<byte[]> DecodeParts(ReadOnlySpan<byte> bytes)
	{
		var parts = new List<byte[]>();
		while (bytes.Length >= 2)
		{
			var length = BinaryPrimitives.ReadUInt16BigEndian(bytes);
			parts.Add(bytes.Slice(2, length).ToArray());
			bytes = bytes[(2 + length)..];
		}

		return parts;
	}

	private static AttributeIndexOptions Options(AttributeIndexRecord index)
		=> new(index.Exact, index.Numeric, index.Split, index.Type);

	private static AttributeIndexRecord ToRecord(AttributeIndexOptions options) => new()
	{
		Exact = options.Exact,
		Numeric = options.Numeric,
		Split = options.Split,
		Type = options.Type
	};

	private static bool SameOptions(AttributeIndexRecord a, AttributeIndexRecord b)
		=> a.Exact == b.Exact && a.Numeric == b.Numeric && a.Split == b.Split && a.Type == b.Type;

	private static AttributeIndexDefinition MapIndex(AttributeIndexRecord index)
		=> new(Options(index), index.Ready, index.ResumeAt);

	#endregion

	#region Keeping indexes in step with values

	private static bool InScope(AttributeIndexRecord index, string objectType)
		=> index.Type is null || index.Type.Equals(objectType, StringComparison.OrdinalIgnoreCase);

	private static bool IsInternal(IEnumerable<string> flags)
		=> flags.Any(flag => flag.Equals(InternalFlag, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Lists one copy under the parts of its value, replacing whatever it was listed under before: nothing for an
	/// empty value, a copy flagged <c>internal</c>, or an object outside the index's type.
	/// </summary>
	internal static void IndexCopy(ITx tx, long dbref, string longName, AttributeIndexRecord index, string objectType,
		IEnumerable<string> flags, string plain)
		=> ReplaceParts(tx, dbref, longName,
			InScope(index, objectType) && !IsInternal(flags) ? PartsOf(plain, index) : []);

	/// <summary>Takes one copy out of its index: one point read when it was never listed.</summary>
	internal static void UnindexCopy(ITx tx, long dbref, string longName) => ReplaceParts(tx, dbref, longName, []);

	/// <summary>Takes every listed copy under <paramref name="attrPrefix"/> (an object's, or a subtree's) out of its index.</summary>
	internal static void UnindexCopies(ITx tx, long dbref, byte[] attrPrefix)
	{
		var listed = tx.Range(Tables.AttrIdxOf, attrPrefix).ToList();
		if (listed.Count == 0)
		{
			return;
		}

		var value = Keys.Dbref(dbref);
		foreach (var (key, parts) in listed)
		{
			var prefix = IndexPrefix(Keys.ParseAttr(key).LongName);
			foreach (var part in DecodeParts(parts))
			{
				tx.Delete(Tables.AttrIdx, IndexKey(prefix, part), value);
			}

			tx.Delete(Tables.AttrIdxOf, key);
		}
	}

	/// <summary>
	/// Moves one copy from the parts it was listed under to <paramref name="wanted"/>, touching only what differs, so
	/// writing the same value again writes nothing.
	/// </summary>
	private static bool ReplaceParts(ITx tx, long dbref, string longName, List<byte[]> wanted)
	{
		var attrKey = Keys.Attr(dbref, longName);
		var had = tx.TryGet(Tables.AttrIdxOf, attrKey, out var old) ? DecodeParts(old) : [];
		if (had.Count == wanted.Count && had.Zip(wanted).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)))
		{
			return wanted.Count > 0;
		}

		var prefix = IndexPrefix(longName);
		var value = Keys.Dbref(dbref);
		foreach (var part in had.Where(part => !wanted.Any(w => w.AsSpan().SequenceEqual(part))))
		{
			tx.Delete(Tables.AttrIdx, IndexKey(prefix, part), value);
		}

		foreach (var part in wanted.Where(part => !had.Any(h => h.AsSpan().SequenceEqual(part))))
		{
			tx.Put(Tables.AttrIdx, IndexKey(prefix, part), value);
		}

		if (wanted.Count == 0)
		{
			tx.Delete(Tables.AttrIdxOf, attrKey);
		}
		else
		{
			tx.Put(Tables.AttrIdxOf, attrKey, EncodeParts(wanted));
		}

		return wanted.Count > 0;
	}

	/// <summary>
	/// After a copy's flags change: when <c>internal</c> came or went and its attribute is indexed, lists or unlists it.
	/// </summary>
	private static void ReindexAfterFlagChange(ITx tx, long dbref, string longName, bool wasInternal, IReadOnlyCollection<string> flags)
	{
		var isInternal = IsInternal(flags);
		if (wasInternal == isInternal || ReadAttributeEntryRecord(tx, longName)?.Index is not { } index)
		{
			return;
		}

		if (isInternal)
		{
			UnindexCopy(tx, dbref, longName);
			return;
		}

		var value = tx.TryGet(Tables.AttrVal, Keys.Attr(dbref, longName), out var bytes) ? bytes : null;
		IndexCopy(tx, dbref, longName, index, ReadObjectHeader(tx, dbref)?.Type ?? string.Empty, flags,
			DeserializeValue(value).ToPlainText());
	}

	/// <summary>Removes every entry of the index on <paramref name="longName"/>, in both tables.</summary>
	private static long DropIndexRows(ITx tx, string longName)
	{
		var prefix = IndexPrefix(longName);
		var rows = tx.Range(Tables.AttrIdx, prefix).ToList();
		foreach (var (key, value) in rows)
		{
			tx.Delete(Tables.AttrIdx, key, value);
			tx.Delete(Tables.AttrIdxOf, Keys.Attr(Keys.ReadDbref(value), longName));
		}

		return rows.Count;
	}

	#endregion

	#region Declaring and building

	public async ValueTask<Result<SharpAttributeEntry>> SetAttributeIndexAsync(string name, AttributeIndexOptions options,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync<Result<SharpAttributeEntry>>(tx =>
		{
			if (ReadAttributeEntryRecord(tx, name) is not { } record)
			{
				return new Error<string>($"{name.ToUpperInvariant()} is not a standard attribute.");
			}

			if (IsInternal(record.DefaultFlags))
			{
				return new Error<string>($"{record.Name} is internal by default, so no copy of it could be indexed.");
			}

			if (!AttributeIndexOptions.NameFits(name))
			{
				return new Error<string>(AttributeIndexOptions.NameTooLong(name));
			}

			var wanted = ToRecord(options);
			if (record.Index is { } current && SameOptions(current, wanted))
			{
				return MapEntry(record);
			}

			if (record.Index is not null)
			{
				DropIndexRows(tx, name);
			}

			var updated = record with { Index = wanted with { Ready = false, ResumeAt = 0 } };
			tx.Put(Tables.AttrEntry, Keys.Upper(name), Codec.Serialize(updated));
			return MapEntry(updated);
		}, cancellationToken);

	public async ValueTask<bool> DropAttributeIndexAsync(string name, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (ReadAttributeEntryRecord(tx, name) is not { Index: not null } record)
			{
				return false;
			}

			DropIndexRows(tx, name);
			tx.Put(Tables.AttrEntry, Keys.Upper(name), Codec.Serialize(record with { Index = null }));
			return true;
		}, cancellationToken);

	public async ValueTask<Found<AttributeIndexBuildStep>> BuildAttributeIndexBatchAsync(string name, int batchSize,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync<Found<AttributeIndexBuildStep>>(tx =>
		{
			if (ReadAttributeEntryRecord(tx, name) is not { Index: { } index } record)
			{
				return new NotFound();
			}

			var longName = name.ToUpperInvariant();
			var total = ScopeCount(tx, index.Type);
			if (index.Ready)
			{
				return new AttributeIndexBuildStep(0, 0, 0, true, total);
			}

			long scanned = 0, indexed = 0;
			long? next = null;
			foreach (var dbref in ObjectsFrom(tx, index.Type, index.ResumeAt))
			{
				if (scanned == Math.Max(1, batchSize))
				{
					next = dbref;
					break;
				}

				scanned++;
				var attrKey = Keys.Attr(dbref, longName);
				if (!tx.TryGet(Tables.AttrMeta, attrKey, out var meta))
				{
					continue;
				}

				var value = tx.TryGet(Tables.AttrVal, attrKey, out var bytes) ? bytes : null;
				var type = index.Type ?? ReadObjectHeader(tx, dbref)?.Type ?? string.Empty;
				if (ReplaceParts(tx, dbref, longName,
						InScope(index, type) && !IsInternal(Codec.Deserialize<AttrMetaRecord>(meta).Flags)
							? PartsOf(DeserializeValue(value).ToPlainText(), index)
							: []))
				{
					indexed++;
				}
			}

			var advanced = index with { Ready = next is null, ResumeAt = next ?? 0 };
			tx.Put(Tables.AttrEntry, Keys.Upper(name), Codec.Serialize(record with { Index = advanced }));
			return new AttributeIndexBuildStep(scanned, indexed, advanced.ResumeAt, advanced.Ready, total);
		}, cancellationToken);

	/// <summary>The objects an index on <paramref name="type"/> (every type when null) covers, from <paramref name="from"/> up.</summary>
	private static IEnumerable<long> ObjectsFrom(ITx tx, string? type, long from)
	{
		if (type is null)
		{
			return tx.RangeFromKey(Tables.Obj, Keys.Dbref(Math.Max(0, from))).Select(entry => Keys.ReadDbref(entry.Key));
		}

		var key = ObjTypeKey(type);
		var entries = from <= 0 ? tx.Range(Tables.ObjType, key) : tx.RangeFrom(Tables.ObjType, key, key, Keys.Dbref(from - 1));
		return entries
			.TakeWhile(entry => entry.Key.AsSpan().SequenceEqual(key))
			.Select(entry => Keys.ReadDbref(entry.Value));
	}

	private static long ScopeCount(ITx tx, string? type)
		=> type is null
			? tx.Count(Tables.Obj)
			: tx.CountDups(Tables.ObjType, ObjTypeKey(type)) switch
			{
				long count => count,
				_ => 0
			};

	public ValueTask<long> CountAttributeCopiesAsync(string name, string? type, CancellationToken cancellationToken = default)
		=> new(Store.Read(tx =>
		{
			var longName = name.ToUpperInvariant();
			return ObjectsFrom(tx, type, 0).LongCount(dbref => tx.TryGet(Tables.AttrMeta, Keys.Attr(dbref, longName), out _));
		}));

	#endregion

	#region Search seeds

	/// <summary>
	/// The <see cref="Tables.AttrIdx"/> key an <see cref="ObjectSearchFilter.AttributeValue"/> probe seeds from; null
	/// when the attribute has no ready index, which then matches nothing (<see cref="IndexListsCopy"/>).
	/// </summary>
	private static byte[]? AttributeIndexSeed(ITx tx, AttributeIndexProbe probe)
		=> ReadAttributeEntryRecord(tx, probe.Name) is { Index: { Ready: true } index }
			? IndexKey(IndexPrefix(probe.Name), EncodePart(probe.Value, index))
			: null;

	/// <summary>
	/// Whether the index on the probe's attribute lists <paramref name="dbref"/>'s own copy under the probe's value:
	/// <c>indexed(&lt;attr&gt;, =, &lt;value&gt;)</c>'s test for one object, the stored value checked when the probe
	/// reaches the cut.
	/// </summary>
	private static bool IndexListsCopy(ITx tx, long dbref, AttributeIndexProbe probe)
	{
		if (ReadAttributeEntryRecord(tx, probe.Name) is not { Index: { Ready: true } index }
			|| !tx.TryGet(Tables.AttrIdxOf, Keys.Attr(dbref, probe.Name.ToUpperInvariant()), out var listed))
		{
			return false;
		}

		var part = EncodePart(probe.Value, index);
		return DecodeParts(listed).Any(had => had.AsSpan().SequenceEqual(part))
			&& (!ReachesLimit(part, index) || StoredValueHolds(tx, dbref, probe.Name.ToUpperInvariant(), probe.Value, index));
	}

	#endregion

	#region Queries

	public ValueTask<IndexedAttributeResult> QueryAttributeIndexAsync(IndexedAttributeQuerySpec query,
		CancellationToken cancellationToken = default)
		=> new(Store.Read<IndexedAttributeResult>(tx =>
		{
			if (ReadAttributeEntryRecord(tx, query.Name) is not { Index: { } index })
			{
				return new NotIndexed();
			}

			if (!index.Ready)
			{
				return new IndexBuilding();
			}

			var prefix = IndexPrefix(query.Name);
			var prefixMatch = query.Operation == IndexedAttributeOperation.Prefix;
			var probe = prefixMatch ? EncodeTextPart(query.Value, index) : EncodePart(query.Value, index);
			var recheck = query.Operation is IndexedAttributeOperation.Equal or IndexedAttributeOperation.Prefix
				&& ReachesLimit(probe, index);
			var longName = query.Name.ToUpperInvariant();
			var upper = query.Operation == IndexedAttributeOperation.Between
				? EncodePart(query.UpperValue ?? query.Value, index)
				: probe;
			var bounds = Bounds(query, prefix, probe, upper);
			var hits = new List<IndexedAttributeHit>();
			byte[] lastKey = [];
			long lastDbref = 0;
			var limit = Math.Max(1, query.Limit);
			foreach (var (key, value) in Rows(tx, query, prefix, probe, upper))
			{
				if (hits.Count == limit)
				{
					return new IndexedAttributePage(hits, new IndexedAttributeCursor(lastKey, lastDbref));
				}

				var dbref = Keys.ReadDbref(value);
				lastKey = key;
				lastDbref = dbref;
				if (recheck && !StoredValueHolds(tx, dbref, longName, query.Value, index, prefixMatch))
				{
					continue;
				}

				if (bounds.Any(bound => !bound.Admits(tx, key, dbref, longName, index)))
				{
					continue;
				}

				hits.Add(query.Facts ? HitWithFacts(tx, dbref, longName) : new IndexedAttributeHit((int)dbref, 0, -1, IndexedAttributeFacts.None));
			}

			return new IndexedAttributePage(hits, null);
		}));

	public ValueTask<IndexedAttributeCount> CountAttributeIndexAsync(string name, string value,
		CancellationToken cancellationToken = default)
		=> new(Store.Read<IndexedAttributeCount>(tx =>
		{
			if (ReadAttributeEntryRecord(tx, name) is not { Index: { } index })
			{
				return new NotIndexed();
			}

			if (!index.Ready)
			{
				return new IndexBuilding();
			}

			var probe = EncodePart(value, index);
			var key = IndexKey(IndexPrefix(name), probe);
			if (!ReachesLimit(probe, index) && tx.CountDups(Tables.AttrIdx, key) is long count)
			{
				return count;
			}

			var longName = name.ToUpperInvariant();
			return tx.Dups(Tables.AttrIdx, key).LongCount(dbref => StoredValueHolds(tx, Keys.ReadDbref(dbref), longName, value, index));
		}));

	/// <summary>
	/// A range operation's bound: the key a row sits on when its value compares equal to <see cref="Value"/> as far as
	/// the index keeps it, and the comparison a row on that key must pass. Below the cut sitting on the key means equal;
	/// at the cut the stored value is compared in full, so a value longer than the cut lands on the right side.
	/// </summary>
	private sealed record IndexBound(byte[] Key, byte[] Part, string Value, Func<int, bool> Holds)
	{
		public bool Admits(ITx tx, byte[] key, long dbref, string longName, AttributeIndexRecord index)
			=> !key.AsSpan().SequenceEqual(Key)
				|| (ReachesLimit(Part, index)
					? StoredValueCompares(tx, dbref, longName, Part, Value, index, Holds)
					: Holds(0));
	}

	private static List<IndexBound> Bounds(IndexedAttributeQuerySpec query, byte[] prefix, byte[] probe, byte[] upper)
	{
		var upperKey = IndexKey(prefix, upper);
		IndexBound Lower(Func<int, bool> holds) => new(IndexKey(prefix, probe), probe, query.Value, holds);
		return query.Operation switch
		{
			IndexedAttributeOperation.Less => [Lower(c => c < 0)],
			IndexedAttributeOperation.LessOrEqual => [Lower(c => c <= 0)],
			IndexedAttributeOperation.Greater => [Lower(c => c > 0)],
			IndexedAttributeOperation.GreaterOrEqual => [Lower(c => c >= 0)],
			IndexedAttributeOperation.Between =>
				[Lower(c => c >= 0), new IndexBound(upperKey, upper, query.UpperValue ?? query.Value, c => c <= 0)],
			_ => []
		};
	}

	/// <summary>
	/// Whether an element of the copy's stored value listed under <paramref name="part"/> compares to
	/// <paramref name="probe"/> as <paramref name="holds"/> asks, in full: the folded text as UTF-8, the order the index
	/// keeps up to the cut. Only text is ever cut, so the elements compared are text.
	/// </summary>
	private static bool StoredValueCompares(ITx tx, long dbref, string longName, byte[] part, string probe,
		AttributeIndexRecord index, Func<int, bool> holds)
	{
		if (!tx.TryGet(Tables.AttrVal, Keys.Attr(dbref, longName), out var bytes))
		{
			return false;
		}

		var options = Options(index);
		var wanted = Encoding.UTF8.GetBytes(options.Fold(probe));
		return options.Elements(DeserializeValue(bytes).ToPlainText())
			.Where(element => EncodePart(element, index).AsSpan().SequenceEqual(part))
			.Any(element => holds(Encoding.UTF8.GetBytes(options.Fold(element)).AsSpan().SequenceCompareTo(wanted)));
	}

	/// <summary>
	/// The index rows <paramref name="query"/> asks for, in order, after its cursor. A range takes the rows on its bound
	/// keys too, whatever the operation, and <see cref="IndexBound"/> decides each of those.
	/// </summary>
	private static IEnumerable<(byte[] Key, byte[] Value)> Rows(ITx tx, IndexedAttributeQuerySpec query, byte[] prefix,
		byte[] probe, byte[] upper)
	{
		var probeKey = IndexKey(prefix, probe);
		var after = query.After;
		var afterValue = after is null ? null : Keys.Dbref(after.Dbref);

		IEnumerable<(byte[] Key, byte[] Value)> From(byte[] rangePrefix, byte[]? start = null)
			=> after is not null
				? tx.RangeFrom(Tables.AttrIdx, rangePrefix, after.Key, afterValue)
				: start is null
					? tx.Range(Tables.AttrIdx, rangePrefix)
					: tx.RangeFromKey(Tables.AttrIdx, start).TakeWhile(row => Keys.StartsWith(row.Key, rangePrefix));

		int Compare(byte[] key) => key.AsSpan().SequenceCompareTo(probeKey);

		switch (query.Operation)
		{
			case IndexedAttributeOperation.Equal:
				return From(probeKey).TakeWhile(row => row.Key.AsSpan().SequenceEqual(probeKey));
			case IndexedAttributeOperation.Prefix:
				return From(probeKey);
			case IndexedAttributeOperation.Less or IndexedAttributeOperation.LessOrEqual:
				return From(prefix).TakeWhile(row => Compare(row.Key) <= 0);
			case IndexedAttributeOperation.Greater or IndexedAttributeOperation.GreaterOrEqual:
				return From(prefix, probeKey);
			case IndexedAttributeOperation.Between:
				var upperKey = IndexKey(prefix, upper);
				return From(prefix, probeKey).TakeWhile(row => row.Key.AsSpan().SequenceCompareTo(upperKey) <= 0);
			case IndexedAttributeOperation.First:
				return From(prefix);
			case IndexedAttributeOperation.Last:
				return after is null
					? tx.RangeReverse(Tables.AttrIdx, prefix)
					// Every entry before (cursor key, cursor dbref), walking down: from the cursor key's last duplicate.
					: tx.RangeReverseBefore(Tables.AttrIdx, prefix, [.. after.Key, 0x00])
						.SkipWhile(row => row.Key.AsSpan().SequenceEqual(after.Key) && Keys.ReadDbref(row.Value) >= after.Dbref);
			default:
				return [];
		}
	}

	/// <summary>Whether the copy's stored value holds <paramref name="probe"/> in full, for an equality probe that reached the cut.</summary>
	private static bool StoredValueHolds(ITx tx, long dbref, string longName, string probe, AttributeIndexRecord index,
		bool prefix = false)
		=> tx.TryGet(Tables.AttrVal, Keys.Attr(dbref, longName), out var bytes)
			&& (prefix
				? StartsExactly(DeserializeValue(bytes).ToPlainText(), probe, index)
				: HoldsExactly(DeserializeValue(bytes).ToPlainText(), probe, index));

	/// <summary>
	/// A hit with what <c>IndexedAttributeVisibility</c> needs: the object's row (type, creation time, control lock),
	/// its owner, zone and VISUAL flag, its owner's SHARED flag, and the attribute flags along the copy's path.
	/// </summary>
	private static IndexedAttributeHit HitWithFacts(ITx tx, long dbref, string longName)
	{
		var facts = IndexedAttributeFacts.None;
		long creation = 0;
		if (tx.TryGet(Tables.Obj, Keys.Dbref(dbref), out var row))
		{
			var record = Codec.Deserialize<ObjectRecord>(row);
			creation = record.CreationTime;
			if (record.Type.Equals(DatabaseConstants.TypePlayer, StringComparison.OrdinalIgnoreCase)) facts |= IndexedAttributeFacts.IsPlayer;
			if (record.Locks.Keys.Any(name => name.Equals(nameof(LockType.Control), StringComparison.OrdinalIgnoreCase)))
				facts |= IndexedAttributeFacts.HasControlLock;
		}

		var owner = GetSingleEdge(tx, Tables.Owner.Forward, dbref) ?? -1;
		if (GetSingleEdge(tx, Tables.Zone.Forward, dbref) is not null) facts |= IndexedAttributeFacts.HasZone;
		if (HasMembershipEdge(Tables.ObjFlag.Forward, tx, dbref, "VISUAL")) facts |= IndexedAttributeFacts.ObjectVisual;
		if (owner >= 0 && HasMembershipEdge(Tables.ObjFlag.Forward, tx, owner, "SHARED")) facts |= IndexedAttributeFacts.OwnerShared;

		var allVisual = true;
		var level = string.Empty;
		string[] leafFlags = [];
		foreach (var segment in longName.Split('`'))
		{
			level = level.Length == 0 ? segment : $"{level}`{segment}";
			var flags = tx.TryGet(Tables.AttrMeta, Keys.Attr(dbref, level), out var meta) ? Codec.Deserialize<AttrMetaRecord>(meta).Flags : [];
			if (flags.Any(flag => flag.Equals("mortal_dark", StringComparison.OrdinalIgnoreCase))) facts |= IndexedAttributeFacts.MortalDark;
			if (flags.Any(flag => flag.Equals("nearby", StringComparison.OrdinalIgnoreCase))) facts |= IndexedAttributeFacts.AnyNearby;
			allVisual &= flags.Any(flag => flag.Equals("visual", StringComparison.OrdinalIgnoreCase));
			leafFlags = flags;
		}

		if (allVisual) facts |= IndexedAttributeFacts.AllVisual;
		if (IsInternal(leafFlags)) facts |= IndexedAttributeFacts.Internal;
		return new IndexedAttributeHit((int)dbref, creation, (int)owner, facts);
	}

	/// <summary>
	/// Each index's rows and its share of the two index tables' pages, by the bytes its entries take. A walk of the
	/// index entries, without copying them out.
	/// </summary>
	public IReadOnlyList<AttributeIndexUsage> GetAttributeIndexUsage()
	{
		var indexes = Store.Read(tx => tx.Range(Tables.AttrEntry, [])
			.Select(entry => Codec.Deserialize<AttributeEntryRecord>(entry.Value))
			.SelectMany(entry => entry.Index is { } index ? [(entry.Name, Index: index)] : Array.Empty<(string Name, AttributeIndexRecord Index)>())
			.ToList());
		if (indexes.Count == 0)
		{
			return [];
		}

		var idxTable = Store.TableFootprint(Tables.AttrIdx);
		var ofTable = Store.TableFootprint(Tables.AttrIdxOf);
		var footprints = indexes
			.Select(entry =>
			{
				var name = entry.Name.ToUpperInvariant();
				var (rows, keyBytes) = Store.PrefixFootprint(Tables.AttrIdx, IndexPrefix(name));
				// Each row is its key and an eight-byte dbref; its copy's attr.idx.of entry is the attribute key and the part.
				var prefixLength = IndexPrefix(name).Length;
				var idxBytes = keyBytes + rows * 8;
				var ofBytes = keyBytes - rows * prefixLength + rows * (2 + 9 + Encoding.UTF8.GetByteCount(name));
				return (Entry: entry, Rows: rows, IdxBytes: idxBytes, OfBytes: ofBytes);
			})
			.ToList();
		var idxTotal = Math.Max(1, footprints.Sum(f => f.IdxBytes));
		var ofTotal = Math.Max(1, footprints.Sum(f => f.OfBytes));

		return footprints
			.Select(f =>
			{
				var pages = (long)Math.Ceiling(idxTable.Pages * (double)f.IdxBytes / idxTotal + ofTable.Pages * (double)f.OfBytes / ofTotal);
				return new AttributeIndexUsage(f.Entry.Name, Options(f.Entry.Index).Words, f.Rows, pages, pages * idxTable.PageSize,
					f.Entry.Index.Ready, f.Entry.Index.ResumeAt);
			})
			.OrderBy(usage => usage.Name, StringComparer.Ordinal)
			.ToList();
	}

	#endregion
}
