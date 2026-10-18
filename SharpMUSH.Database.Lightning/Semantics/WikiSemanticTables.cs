using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

internal sealed class WikiSemanticTables(TableDef metadata, TableDef registry, TableDef summaries, TableDef facts,
	TableDef forward, TableDef postings, TableDef catalog, TableDef partitions, bool legacyB1Summary = false)
{
	internal readonly TableDef Metadata = metadata, Registry = registry, Summaries = summaries, Facts = facts,
		Forward = forward, Postings = postings, Catalog = catalog, Partitions = partitions;
	internal bool LegacyB1Summary { get; } = legacyB1Summary;
}
