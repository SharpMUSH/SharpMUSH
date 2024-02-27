namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "test")]
		public static string test(params string[] contents)
		{
			return string.Join("<>", contents);
		}
	}
}
