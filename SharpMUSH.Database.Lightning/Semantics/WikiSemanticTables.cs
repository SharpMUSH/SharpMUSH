using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <param name="classes">Where class identities live; generation 1 keeps them in <paramref name="registry"/>.</param>
/// <param name="counters">Where catalog totals live; generation 1 keeps them in <paramref name="metadata"/>.</param>
internal sealed class WikiSemanticTables(TableDef metadata, TableDef registry, TableDef summaries, TableDef facts,
	TableDef forward, TableDef postings, TableDef catalog, TableDef partitions, bool legacyB1Summary = false,
	TableDef? classes = null, TableDef? counters = null)
{
	internal readonly TableDef Metadata = metadata, Registry = registry, Summaries = summaries, Facts = facts,
		Forward = forward, Postings = postings, Catalog = catalog, Partitions = partitions;
	internal readonly TableDef Classes = classes ?? registry, Counters = counters ?? metadata;
	internal bool LegacyB1Summary { get; } = legacyB1Summary;

	/// <summary>The registry table identities of <paramref name="kind"/> live in.</summary>
	internal TableDef RegistryFor(WikiSemanticRegistryKind kind) => kind == WikiSemanticRegistryKind.Class ? Classes : Registry;

	/// <summary>
	/// The rows a generation owns, as (table, key prefix): what a rebuild must find empty and a retired generation
	/// clears. Generation 1 shares the registry and metadata tables, so there it owns only its class identities
	/// and catalog totals.
	/// </summary>
	internal (TableDef Table, byte[] Prefix)[] Owned =>
	[
		(Summaries, []), (Forward, []), (Postings, []), (Catalog, []), (Partitions, []),
		.. ReferenceEquals(Classes, Registry) ? [(Registry, [(byte)WikiSemanticRegistryKind.Class])] : new[] { (Classes, Array.Empty<byte>()) },
		.. ReferenceEquals(Counters, Metadata) ? [(Metadata, [(byte)30]), (Metadata, [(byte)31])] : new[] { (Counters, Array.Empty<byte>()) }
	];
}
