using Antlr4.Runtime.Misc;
using AntlrCSharp.Implementation.Definitions;
using System;
using System.Data;
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
			{"null",(a) => ""}
		};

		[PennFunction(Name = "isjson", MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState IsJSON(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			try
			{
				using var jsonDoc = JsonDocument.Parse(args[0].Message!.ToString());
				return new CallState("1");
			}
			catch (JsonException)
			{
				return new CallState("0");
			}
		}

		[PennFunction(Name = "json", Flags = FunctionFlags.Regular)]
		public static CallState JSON(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> JsonFunctions.TryGetValue(MModule.plainText(args[0].Message!).ToLower(), out var fun)
				? fun(args)
				: new CallState(MModule.single("#-1 Invalid Type"));

		private static CallState NullJSON(CallState[] args)
			=> (args.Length > 1)
					? new CallState(string.Format(Errors.ErrorTooManyArguments, "json", 1, args.Length))
					: new CallState("null");
	}
}
