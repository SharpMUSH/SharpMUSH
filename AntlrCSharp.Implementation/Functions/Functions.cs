namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "add")]
		public static string add(Parser parser, params string[] contents)
		{
			var parsedValues = contents.Select(parser.FunctionParse);
			var doubles = parsedValues.Select(x => (IsDouble: double.TryParse(string.Join("",x), out var b), Double: b));
			var notDoubles = doubles.Where(x => !x.IsDouble);
			if(notDoubles.Any())
			{
				return $"#-1 The following are not valid Numbers: {string.Join(", ", notDoubles)}";
			}
			return doubles.Sum(x => x.Double).ToString();
		}
	}
}
