namespace AntlrCSharp.Implementation.Definitions
{
	public static class Predicates
	{
		public static bool Truthy(string text)
			=> !string.IsNullOrEmpty(text) && !text.StartsWith("#-") && text is not "0";

		public static bool Falsey(string text)
			=> !Truthy(text);
	}
}
