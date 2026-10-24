using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tests;

/// <summary>
/// The properties the fact tests state, defined in the world under test. Defining one again with the same definition
/// changes nothing, so tests sharing a world may each define them.
/// </summary>
public static class WikiFactTestProperties
{
	public static readonly IReadOnlyList<PropertyDefinition> All =
	[
		new("pf2e:level", WikiFactProperties.SchemaVersion, SemanticType.Integer, Cardinality.Single, []),
		new("pf2e:rank", WikiFactProperties.SchemaVersion, SemanticType.Integer, Cardinality.Single, []),
		new("pf2e:tradition", WikiFactProperties.SchemaVersion, SemanticType.Keyword, Cardinality.Multiple,
			[new("arcane"), new("divine"), new("occult"), new("primal")]),
		new("pf2e:class", WikiFactProperties.SchemaVersion, SemanticType.PageRef, Cardinality.Multiple, []),
		new("pf2e:ancestry", WikiFactProperties.SchemaVersion, SemanticType.PageRef, Cardinality.Single, []),
		new("pf2e:source-book", WikiFactProperties.SchemaVersion, SemanticType.Keyword, Cardinality.Single,
			[new("player core"), new("player core 2"), new("gm core")]),
		new("pf2e:price", WikiFactProperties.SchemaVersion, SemanticType.Decimal, Cardinality.Single, []),
		new("pf2e:published", WikiFactProperties.SchemaVersion, SemanticType.Date, Cardinality.Single, []),
		new("pf2e:motto", WikiFactProperties.SchemaVersion, SemanticType.Text, Cardinality.Single, []),
	];

	public static async Task DefineAsync(IWikiStore store)
	{
		foreach (var property in All) _ = (await store.SetFactPropertyAsync(property)).Expect<PropertyDefinition>();
	}

	public static async Task DefineAsync(IWikiService wiki)
	{
		foreach (var property in All) _ = (await wiki.SetFactPropertyAsync(property)).Expect<PropertyDefinition>();
	}
}
