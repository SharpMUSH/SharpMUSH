using AntlrCSharp.Implementation.Definitions;
using System.Text.Json;

namespace AntlrCSharp.Implementation.Functions
{
	/*
    json()
    json_map()
    json_query()
    json_mod()
    
    wsjson()
    oob()
  */
	public partial class Functions
	{
		public static Dictionary<string, Func<CallState[], CallState>> JsonFunctions = new()
		{
			{"null", NullJSON},
			{"boolean", BooleanJSON}
		};

		[PennFunction(Name = "isjson", MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState IsJSON(Parser parser, PennFunctionAttribute _2)
		{
			try
			{
				using var jsonDoc = JsonDocument.Parse(parser.State.Peek().Arguments[0].Message!.ToString());
				return new CallState("1");
			}
			catch (JsonException)
			{
				return new CallState("0");
			}
		}

		[PennFunction(Name = "json", Flags = FunctionFlags.Regular)]
		public static CallState JSON(Parser parser, PennFunctionAttribute _2)
			=> JsonFunctions.TryGetValue(MModule.plainText(parser.State.Peek().Arguments[0].Message!).ToLower(), out var fun)
				? fun(parser.State.Peek().Arguments)
				: new CallState(MModule.single("#-1 Invalid Type"));

		private static CallState NullJSON(CallState[] args)
			=> (args.Length > 2)
					? new CallState(string.Format(Errors.ErrorTooManyArguments, "json", 2, args.Length))
					: new CallState("null");

		private static CallState BooleanJSON(CallState[] args)
		{
			if (args.Length != 2)
			{
				return new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, 2, args.Length));
			}

			var entry = MModule.plainText(args[1].Message);

			return entry switch
			{
				not "1" or "0" or "false" or "true" => new CallState("#-1 INVALD VALUE"),
				_ => new CallState(entry is "1" or "true" ? "true" : "false")
			};
		}
	}
}
