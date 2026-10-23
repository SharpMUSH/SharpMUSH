using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The fact index, <see cref="Tables.WikiFact"/>: one empty entry per value a page states, keyed
/// <c>property 0x00 tag value page</c>. The pages stating one value are one prefix, and an integer property's values
/// sort as numbers, so a range of them is one range read.
/// </summary>
/// <remarks>
/// <para>Every page write moves the page's entries in the same job (<see cref="WikiListIndexes"/>), from the moment
/// the index starts building; the build itself only adds the pages it has not reached, so a write never waits for it
/// and a build stopped at shutdown resumes from <see cref="FactIndexState.ResumeAt"/>.</para>
/// <para>Entries are typed by the schema they were built under. A different schema, or a state this build cannot
/// read, clears the index first; while it clears, writes leave it alone.</para>
/// </remarks>
public partial class LightningDatabase
{
	private const byte FactIntegerTag = 1;
	private const byte FactTextTag = 2;
	private const int FactIndexStateLength = 1 + 8 + 8 + 32;
	private static readonly byte[] FactIndexStateKey = Keys.Str("wiki.fact.index");

	/// <summary>What the entries are typed by: each property's id and type.</summary>
	private static readonly byte[] FactIndexSchemaDigest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
		WikiFactSchema.Schema.Properties.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => $"{p.Id}\0{p.Type}"))));

	/// <param name="ResumeAt">The first page key a build has not indexed yet.</param>
	/// <param name="Indexed">Pages the build has indexed, for the progress it reports.</param>
	/// <param name="Digest">The schema the entries were typed by; empty when the stored state could not be read.</param>
	private sealed record FactIndexState(WikiFactIndexPhase Phase, long ResumeAt, long Indexed, byte[] Digest)
	{
		/// <summary>A world that never had an index: nothing is in it, and the schema is the running one.</summary>
		internal static FactIndexState Fresh => new(WikiFactIndexPhase.Building, 0, 0, FactIndexSchemaDigest);

		/// <summary>Whether page writes keep the entries: they are typed by the running schema and not being cleared.</summary>
		internal bool Maintained => Phase != WikiFactIndexPhase.Clearing && Digest.AsSpan().SequenceEqual(FactIndexSchemaDigest);
	}

	private static FactIndexState ReadFactIndexState(ITx tx)
	{
		if (!tx.TryGet(Tables.Meta, FactIndexStateKey, out var bytes)) return FactIndexState.Fresh;
		// A state another build wrote in a shape this one does not know is cleared and built again, never trusted.
		if (bytes.Length != FactIndexStateLength || !Enum.IsDefined((WikiFactIndexPhase)bytes[0]))
			return new(WikiFactIndexPhase.Clearing, 0, 0, []);
		return new((WikiFactIndexPhase)bytes[0], BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(1)),
			BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(9)), bytes[17..]);
	}

	private static void WriteFactIndexState(ITx tx, FactIndexState state)
	{
		var bytes = new byte[FactIndexStateLength];
		bytes[0] = (byte)state.Phase;
		BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(1), state.ResumeAt);
		BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(9), state.Indexed);
		state.Digest.CopyTo(bytes, 17);
		tx.Put(Tables.Meta, FactIndexStateKey, bytes);
	}

	/// <summary>Adds (<paramref name="add"/>) or removes the index entries for the facts <paramref name="r"/> states.</summary>
	private static void WikiFactEntries(ITx tx, long pageKey, WikiPageRecord r, bool add)
	{
		if (r.Facts is not { Length: > 0 } facts || !ReadFactIndexState(tx).Maintained) return;
		PutFactEntries(tx, pageKey, facts, add);
	}

	private static void PutFactEntries(ITx tx, long pageKey, WikiPageFactRecord[] facts, bool add)
	{
		foreach (var fact in facts)
		{
			if (FactValuePrefix(fact.Property, fact.Value) is not { } prefix) continue;
			var key = Keys.Concat(prefix, Keys.Dbref(pageKey));
			if (add) tx.Put(Tables.WikiFact, key, []);
			else tx.Delete(Tables.WikiFact, key);
		}
	}

	/// <summary>The entries of one property: <c>property 0x00</c>.</summary>
	private static byte[] FactPropertyPrefix(string property) => Keys.Concat(Keys.Str(property), Keys.Sep);

	/// <summary>
	/// The prefix of the pages stating <paramref name="value"/> for <paramref name="property"/>, typed by the schema: an
	/// integer as eight bytes that sort as the number does, anything else as its text and a 0x00. Null for a property
	/// the schema does not define, which no query can name.
	/// </summary>
	private static byte[]? FactValuePrefix(string property, string value)
	{
		if (WikiFactDefinition(property) is not { } definition) return null;
		return definition.Type == SemanticType.Integer
			&& long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
			? Keys.Concat(FactPropertyPrefix(property), [FactIntegerTag], FactOrderedInteger(number))
			: Keys.Concat(FactPropertyPrefix(property), [FactTextTag], Keys.Str(value), Keys.Sep);
	}

	private static PropertyDefinition? WikiFactDefinition(string property)
		=> WikiFactSchema.Schema.Properties.FirstOrDefault(p => p.Id == property);

	/// <summary>Big-endian with the sign bit flipped, so byte order is numeric order across negatives.</summary>
	private static byte[] FactOrderedInteger(long value)
	{
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, unchecked((ulong)value ^ 0x8000_0000_0000_0000UL));
		return bytes;
	}

	public Task<WikiFactIndexStatus> GetFactIndexStatusAsync()
		=> Task.FromResult(Store.Read(tx => FactIndexStatus(tx, ReadFactIndexState(tx))));

	private static WikiFactIndexStatus FactIndexStatus(ITx tx, FactIndexState state)
	{
		var pages = tx.Count(Tables.WikiPage);
		return state.Maintained
			? new(state.Phase, state.Phase == WikiFactIndexPhase.Ready ? pages : Math.Min(state.Indexed, pages), pages)
			: new(WikiFactIndexPhase.Clearing, 0, pages);
	}

	public async Task<WikiFactIndexStatus> BuildFactIndexBatchAsync(int batchSize)
	{
		var batch = Math.Max(1, batchSize);
		return await Store.WriteAsync(tx =>
		{
			var state = ReadFactIndexState(tx);
			// A schema change starts the index over; the entries typed by the old one are cleared first.
			if (!state.Digest.AsSpan().SequenceEqual(FactIndexSchemaDigest))
				state = new(WikiFactIndexPhase.Clearing, 0, 0, FactIndexSchemaDigest);
			state = state.Phase switch
			{
				WikiFactIndexPhase.Clearing => ClearFactEntries(tx, state, batch),
				WikiFactIndexPhase.Building => IndexFactPages(tx, state, batch),
				_ => state
			};
			WriteFactIndexState(tx, state);
			return FactIndexStatus(tx, state);
		});
	}

	private static FactIndexState ClearFactEntries(ITx tx, FactIndexState state, int batch)
	{
		var keys = tx.Range(Tables.WikiFact, []).Take(batch + 1).Select(entry => entry.Key).ToList();
		foreach (var key in keys.Take(batch)) tx.Delete(Tables.WikiFact, key);
		return keys.Count > batch ? state : new(WikiFactIndexPhase.Building, 0, 0, state.Digest);
	}

	/// <summary>
	/// Indexes the next <paramref name="batch"/> pages from where the build reached. A page a write already indexed is
	/// indexed again to the same entries.
	/// </summary>
	private static FactIndexState IndexFactPages(ITx tx, FactIndexState state, int batch)
	{
		var indexed = 0;
		foreach (var (key, value) in tx.RangeFromKey(Tables.WikiPage, WikiPageKey(state.ResumeAt)))
		{
			var pageKey = Keys.ReadDbref(key);
			if (indexed == batch) return state with { ResumeAt = pageKey, Indexed = state.Indexed + indexed };
			if (Codec.Deserialize<WikiPageRecord>(value) is { Facts: { Length: > 0 } facts }) PutFactEntries(tx, pageKey, facts, add: true);
			indexed++;
		}
		return new(WikiFactIndexPhase.Ready, 0, state.Indexed + indexed, state.Digest);
	}

	public async Task<WikiFactIndexStatus> RebuildFactIndexAsync()
		=> await Store.WriteAsync(tx =>
		{
			var state = new FactIndexState(WikiFactIndexPhase.Clearing, 0, 0, FactIndexSchemaDigest);
			WriteFactIndexState(tx, state);
			return FactIndexStatus(tx, state);
		});
}
