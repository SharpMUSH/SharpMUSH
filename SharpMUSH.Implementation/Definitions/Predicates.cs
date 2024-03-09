namespace SharpMUSH.Implementation.Definitions
{
	public static class Predicates
	{
		public static bool Truthy(MarkupString.MarkupStringModule.MarkupString text)
		{
			var plainText = MarkupString.MarkupStringModule.plainText(text);
			return !string.IsNullOrEmpty(plainText) && !plainText.StartsWith("#-") && plainText is not "0";
		}

		public static bool Falsey(MarkupString.MarkupStringModule.MarkupString text)
			=> !Truthy(text);
	}
}
