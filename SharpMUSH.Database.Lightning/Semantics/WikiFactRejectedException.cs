using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>Facts a page was asked to state that the world's schema refuses; the write changes nothing.</summary>
internal sealed class WikiFactRejectedException(string message) : Exception(message)
{
	internal static string Describe(ValidationFailure failure) => failure.Code switch
	{
		ValidationCode.UnknownProperty => $"No property {failure.Detail} is defined.",
		ValidationCode.InvalidEnum => $"{failure.Detail} does not allow that value.",
		ValidationCode.InvalidKeyword => $"{failure.Detail} has a value that is not a keyword.",
		ValidationCode.Cardinality => $"{failure.Detail} takes one value.",
		ValidationCode.Quota => failure.Detail,
		_ => failure.Detail
	};
}
