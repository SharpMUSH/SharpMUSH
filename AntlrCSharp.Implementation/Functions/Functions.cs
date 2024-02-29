using AntlrCSharp.Implementation.Constants;
using System.Linq;

namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "add")]
		public static CallState add(Parser parser, PennMUSHParser.FunctionContext context, params CallState[] contents)
		{
			var parsedValues = contents.Select(x => parser.FunctionParse(x?.Message ?? string.Empty));
			var doubles = parsedValues.Select(x => 
				(
					IsDouble: double.TryParse(string.Join("", x?.Message), out var b), 
					Double: b, 
					Original: x
				));

			var notDoubles = doubles.Where(x => !x.IsDouble).Select(x => x.Original);

			if(notDoubles.Any())
			{
				return new CallState(Message: Errors.ErrorNumbers, context.Depth());
			}
			else
			{
				return new CallState(Message: doubles.Sum(x => x.Double).ToString(), context.Depth());
			}
		}
	}
}
