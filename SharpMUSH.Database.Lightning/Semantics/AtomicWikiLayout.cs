using System.Security.Cryptography;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

internal sealed record AtomicWikiActivation(bool Active, Guid FixtureId, string SchemaDigest,
	long Sequence, int Count, string IdentityDigest, string RequirementsDigest, ulong Generation = 1);

internal static class AtomicWikiLayout
{
	internal const string Prefix = "prototype.semantic.b2.v1.";
	internal const string ReservedPrefix = "prototype.semantic.b2.";
	internal static readonly byte[] MarkerKey = System.Text.Encoding.UTF8.GetBytes("prototype.semantic.activation/v1");
	internal static readonly byte[] RootKey = [0];
	internal static readonly byte[] CertificateKey = [3];
	internal static readonly byte[] RawEpochKey = System.Text.Encoding.UTF8.GetBytes("prototype.semantic.raw-epoch/v1");
	/// <summary>
	/// Prototype schema 2: the pinned PF2e extractor's properties, for fixture-only canonical fact imports.
	/// Ordinary page edits commit no facts under it; only an explicit import does.
	/// </summary>
	internal static readonly SemanticSchema Schema = new(2,
	[
		new("pf2e:level", 2, SemanticType.Integer, Cardinality.Single, []),
		new("pf2e:rank", 2, SemanticType.Integer, Cardinality.Single, []),
		new("pf2e:tradition", 2, SemanticType.Keyword, Cardinality.Multiple, [new("arcane"), new("divine"), new("occult"), new("primal")]),
		new("pf2e:class", 2, SemanticType.PageRef, Cardinality.Multiple, []),
		new("pf2e:ancestry", 2, SemanticType.PageRef, Cardinality.Single, []),
		new("pf2e:source-book", 2, SemanticType.Keyword, Cardinality.Single, [new("player core"), new("player core 2"), new("gm core")])
	]);
	internal static readonly byte[] SchemaBytes = WikiSemanticCodec.SchemaIdentity(Schema);
	internal static readonly string SchemaDigest = Convert.ToHexStringLower(SHA256.HashData(SchemaBytes));
	internal static readonly TableDef Metadata = TableDef.Node(Prefix + "metadata");
	internal static readonly TableDef Registry = TableDef.Node(Prefix + "registry");
	internal static readonly TableDef Summaries = TableDef.Node(Prefix + "summaries");
	internal static readonly TableDef Facts = TableDef.Node(Prefix + "facts");
	internal static readonly TableDef Forward = TableDef.Node(Prefix + "forward");
	internal static readonly TableDef Postings = TableDef.Node(Prefix + "postings");
	internal static readonly TableDef Catalog = TableDef.Node(Prefix + "catalog");
	internal static readonly TableDef Partitions = TableDef.Node(Prefix + "partitions");
	internal static readonly TableDef Mutations = TableDef.Node(Prefix + "mutations");
	/// <summary>While a rebuild is staged: (commit sequence, subject) for every projection write, in that write's own job.</summary>
	internal static readonly TableDef Journal = TableDef.Node(Prefix + "journal");

	// A generation's secondary state lives in one of two slots: odd generations in the tables above, even ones in
	// these. A rebuild fills the slot the active generation is not using, so the layout never grows.
	internal const string AlternatePrefix = Prefix + "s1.";
	internal static readonly TableDef AlternateSummaries = TableDef.Node(AlternatePrefix + "summaries");
	internal static readonly TableDef AlternateForward = TableDef.Node(AlternatePrefix + "forward");
	internal static readonly TableDef AlternatePostings = TableDef.Node(AlternatePrefix + "postings");
	internal static readonly TableDef AlternateCatalog = TableDef.Node(AlternatePrefix + "catalog");
	internal static readonly TableDef AlternatePartitions = TableDef.Node(AlternatePrefix + "partitions");
	internal static readonly TableDef AlternateClasses = TableDef.Node(AlternatePrefix + "classes");
	internal static readonly TableDef AlternateCounters = TableDef.Node(AlternatePrefix + "counters");

	internal static readonly TableDef[] All = [Metadata, Registry, Summaries, Facts, Forward, Postings, Catalog, Partitions, Mutations, Journal,
		AlternateSummaries, AlternateForward, AlternatePostings, AlternateCatalog, AlternatePartitions, AlternateClasses, AlternateCounters];

	/// <summary>Generation 1's tables, the layout every activation starts with.</summary>
	internal static WikiSemanticTables Tables => ForGeneration(1);

	/// <summary>The tables <paramref name="generation"/>'s secondary state lives in; canonical tables are shared.</summary>
	internal static WikiSemanticTables ForGeneration(ulong generation)
	{
		ArgumentOutOfRangeException.ThrowIfZero(generation);
		return generation % 2 == 1
			? new(Metadata, Registry, Summaries, Facts, Forward, Postings, Catalog, Partitions)
			: new(Metadata, Registry, AlternateSummaries, Facts, AlternateForward, AlternatePostings, AlternateCatalog, AlternatePartitions,
				classes: AlternateClasses, counters: AlternateCounters);
	}
	internal static byte[] Encode(AtomicWikiActivation state) => WikiSemanticCodec.Encode(w =>
	{
		w.Byte(1); w.Byte(7); w.Byte(state.Active ? (byte)1 : (byte)0);
		w.String(state.FixtureId.ToString("N")); w.Int64(1); w.String(state.SchemaDigest);
		w.Int64(checked((long)state.Generation)); w.Byte(0); // active generation, QueryReady=false
		w.Int64(state.Sequence); w.Int32(state.Count); w.String(state.IdentityDigest); w.String(state.RequirementsDigest);
		w.Int64(1); // baseline requirements snapshot version
	}, 512);
	internal static AtomicWikiActivation Decode(ReadOnlySpan<byte> bytes, Guid fixtureId)
	{
		if (bytes.Length > 512) throw new InvalidDataException("Invalid activation marker length.");
		var reader = new WikiSemanticCodec.Reader(bytes, 7);
		var active = reader.Boolean();
		if (!Guid.TryParseExact(reader.String(), "N", out var id) || id != fixtureId || reader.Int64() != 1
			|| reader.String() != SchemaDigest || reader.Int64() is not (>= 1 and var generation) || reader.Boolean())
			throw new InvalidDataException("Invalid activation identity.");
		var sequence = reader.Int64();
		var count = reader.Count(128, 1);
		var identities = reader.String();
		var requirements = reader.String();
		if (reader.Int64() != 1) throw new InvalidDataException("Unsupported requirements snapshot version.");
		reader.End();
		if (sequence <= 0 || identities.Length != 64 || requirements.Length != 64) throw new InvalidDataException("Invalid activation certificate.");
		return new(active, id, SchemaDigest, sequence, count, identities, requirements, (ulong)generation);
	}
}
