using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
		public static Dictionary<string, Func<ConcurrentDictionary<string, CallState>, ValueTask<CallState>>> JsonFunctions = new()
	{
		{"null", NullJSON},
		{"boolean", BooleanJSON}
	};

		[SharpFunction(Name = "isjson", MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static ValueTask<CallState> IsJSON(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				try
				{
						using var jsonDoc = JsonDocument.Parse(parser.CurrentState.Arguments["0"].Message!.ToString());
						return ValueTask.FromResult(new CallState("1"));
				}
				catch (JsonException)
				{
						return ValueTask.FromResult(new CallState("0"));
				}
		}

		[SharpFunction(Name = "json", Flags = FunctionFlags.Regular)]
		public static async ValueTask<CallState> JSON(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> JsonFunctions.TryGetValue(MModule.plainText(parser.CurrentState.Arguments["0"].Message!).ToLower(), out var fun)
				? await fun(parser.CurrentState.Arguments)
				: new CallState(MModule.single("#-1 Invalid Type"));

		private static ValueTask<CallState> NullJSON(ConcurrentDictionary<string, CallState> args)
			=> args.Count > 2
				? ValueTask.FromResult(new CallState(string.Format(Errors.ErrorTooManyArguments, "json", 2, args.Count)))
				: ValueTask.FromResult(new CallState("null"));

		private static ValueTask<CallState> BooleanJSON(ConcurrentDictionary<string, CallState> args)
		{
				if (args.Count != 2)
				{
						return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, 2, args.Count)));
				}

				var entry = MModule.plainText(args["1"].Message);

				return entry switch
				{
						not "1" and not "0" and not "false" and not "true" => ValueTask.FromResult(new CallState("#-1 INVALD VALUE")),
						_ => ValueTask.FromResult(new CallState(entry is "1" or "true" ? "true" : "false"))
				};
		}

		[SharpFunction(Name = "JSON", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static ValueTask<CallState> json(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}
		[SharpFunction(Name = "JSON_MAP", MinArgs = 2, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> json_map(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}
		[SharpFunction(Name = "JSON_MOD", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> json_mod(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}
		[SharpFunction(Name = "JSON_QUERY", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> json_query(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}
		[SharpFunction(Name = "OOB", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static ValueTask<CallState> oob(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
				throw new NotImplementedException();
		}
}