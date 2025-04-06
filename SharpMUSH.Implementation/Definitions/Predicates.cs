namespace SharpMUSH.Implementation.Definitions;

public static class Predicates
{
	public static bool Truthy(MString text)
	{
		var plainText = MModule.plainText(text);
		return !string.IsNullOrEmpty(plainText) && !plainText.StartsWith("#-") && plainText is not "0";
	}

	public static bool Falsy(MString text)
		=> !Truthy(text);
}

public static class PredicateExtensions
{
	public static bool Truthy(this MString? text) => Predicates.Truthy(text ?? MModule.empty());
	public static bool Falsey(this MString? text) => Predicates.Falsy(text ?? MModule.empty());
}