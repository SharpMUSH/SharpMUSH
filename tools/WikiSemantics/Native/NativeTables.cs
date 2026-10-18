using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Database.Lightning.Semantics;

namespace SharpMUSH.Tools.WikiSemantics.Native;

internal sealed class NativeTables
{
	internal const string Prefix = "prototype.semantic.b1.";
	internal readonly TableDef Metadata, Registry, Pages, Summaries, Facts, Forward, Postings, Catalog, Partitions, Mutations;
	internal IReadOnlyList<TableDef> All { get; }
	internal WikiSemanticTables Projection { get; }
	internal NativeTables(ILightningStorageAccessor storage)
	{
		Metadata = storage.OpenTable(Prefix + "metadata", false);
		Registry = storage.OpenTable(Prefix + "registry", false);
		Pages = storage.OpenTable(Prefix + "pages", false);
		Summaries = storage.OpenTable(Prefix + "summaries", false);
		Facts = storage.OpenTable(Prefix + "facts", false);
		Forward = storage.OpenTable(Prefix + "forward", false);
		// One typed ordered posting layout serves reverse/equality/integer-range evidence.
		Postings = storage.OpenTable(Prefix + "postings", false);
		Catalog = storage.OpenTable(Prefix + "catalog", false);
		Partitions = storage.OpenTable(Prefix + "partitions", false);
		Mutations = storage.OpenTable(Prefix + "mutations", false);
		All = [Metadata, Registry, Pages, Summaries, Facts, Forward, Postings, Catalog, Partitions, Mutations];
		Projection = new(Metadata, Registry, Summaries, Facts, Forward, Postings, Catalog, Partitions, legacyB1Summary: true);
	}
}
