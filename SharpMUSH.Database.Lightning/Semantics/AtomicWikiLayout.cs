using System.Security.Cryptography;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

internal sealed record AtomicWikiActivation(bool Active, Guid FixtureId, string SchemaDigest,
	long Sequence, int Count, string IdentityDigest, string RequirementsDigest);

internal static class AtomicWikiLayout
{
	internal const string Prefix = "prototype.semantic.b2.v1.";
	internal const string ReservedPrefix = "prototype.semantic.b2.";
	internal static readonly byte[] MarkerKey = System.Text.Encoding.UTF8.GetBytes("prototype.semantic.activation/v1");
	internal static readonly byte[] RootKey = [0];
	internal static readonly byte[] CertificateKey = [3];
	internal static readonly byte[] RawEpochKey = System.Text.Encoding.UTF8.GetBytes("prototype.semantic.raw-epoch/v1");
	internal static readonly SemanticSchema Schema = new(1, []);
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
	internal static readonly TableDef[] All = [Metadata, Registry, Summaries, Facts, Forward, Postings, Catalog, Partitions, Mutations];
	internal static WikiSemanticTables Tables => new(Metadata, Registry, Summaries, Facts, Forward, Postings, Catalog, Partitions);
	internal static byte[] Encode(AtomicWikiActivation state) => WikiSemanticCodec.Encode(w =>
	{
		w.Byte(1); w.Byte(7); w.Byte(state.Active ? (byte)1 : (byte)0);
		w.String(state.FixtureId.ToString("N")); w.Int64(1); w.String(state.SchemaDigest);
		w.Int64(1); w.Byte(0); // generation 1, QueryReady=false
		w.Int64(state.Sequence); w.Int32(state.Count); w.String(state.IdentityDigest); w.String(state.RequirementsDigest);
		w.Int64(1); // baseline requirements snapshot version
	}, 512);
	internal static AtomicWikiActivation Decode(ReadOnlySpan<byte> bytes, Guid fixtureId)
	{
		if (bytes.Length > 512) throw new InvalidDataException("Invalid activation marker length.");
		var reader = new WikiSemanticCodec.Reader(bytes, 7);
		var active = reader.Boolean();
		if (!Guid.TryParseExact(reader.String(), "N", out var id) || id != fixtureId || reader.Int64() != 1
			|| reader.String() != SchemaDigest || reader.Int64() != 1 || reader.Boolean()) throw new InvalidDataException("Invalid activation identity.");
		var sequence = reader.Int64();
		var count = reader.Count(128, 1);
		var identities = reader.String();
		var requirements = reader.String();
		if (reader.Int64() != 1) throw new InvalidDataException("Unsupported requirements snapshot version.");
		reader.End();
		if (sequence <= 0 || identities.Length != 64 || requirements.Length != 64) throw new InvalidDataException("Invalid activation certificate.");
		return new(active, id, SchemaDigest, sequence, count, identities, requirements);
	}
}
