using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Database.Lightning;

/// <summary>The properties a page's facts may state, and how a fact the schema refuses is told.</summary>
internal static class WikiFactSchema
{
	/// <summary>The PF2e properties: a page's facts are typed against them whenever the page is written.</summary>
	internal static readonly SemanticSchema Schema = new(2,
	[
		new("pf2e:level", 2, SemanticType.Integer, Cardinality.Single, []),
		new("pf2e:rank", 2, SemanticType.Integer, Cardinality.Single, []),
		new("pf2e:tradition", 2, SemanticType.Keyword, Cardinality.Multiple, [new("arcane"), new("divine"), new("occult"), new("primal")]),
		new("pf2e:class", 2, SemanticType.PageRef, Cardinality.Multiple, []),
		new("pf2e:ancestry", 2, SemanticType.PageRef, Cardinality.Single, []),
		new("pf2e:source-book", 2, SemanticType.Keyword, Cardinality.Single, [new("player core"), new("player core 2"), new("gm core")])
	]);

	internal static string Describe(ValidationFailure failure) => failure.Code switch
	{
		ValidationCode.UnknownProperty => $"No property {failure.Detail} is defined.",
		ValidationCode.InvalidEnum => $"{failure.Detail} does not allow that value.",
		ValidationCode.InvalidKeyword => $"{failure.Detail} has a value that is not a keyword.",
		ValidationCode.Cardinality => $"{failure.Detail} takes one value.",
		_ => failure.Detail
	};
}
