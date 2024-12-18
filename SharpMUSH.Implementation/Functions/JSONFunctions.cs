using MoreLinq;
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
		{"boolean", BooleanJSON},
		{"string", StringJSON },
		{"markupstring", StringJSON }, // TODO: In PennMUSH, this uses their internal markup instead. This currently has no meaning for us yet.
		{"number", NumberJSON },
		{"array", ArrayJSON },
		{"object", ObjectJSON }
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

	[SharpFunction(Name = "json", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> JSON(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> JsonFunctions.TryGetValue(MModule.plainText(parser.CurrentState.Arguments["0"].Message!).ToLower(), out var jsonFunction)
			? await jsonFunction(parser.CurrentState.Arguments)
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

	private static ValueTask<CallState> StringJSON(ConcurrentDictionary<string, CallState> args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = args["1"].Message;

		return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(entry!.ToString())));
	}

	private static ValueTask<CallState> NumberJSON(ConcurrentDictionary<string, CallState> args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = MModule.plainText(args["1"].Message);
		if (!decimal.TryParse(entry, out var value))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumber));
		}

		return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(value)));
	}

	private static ValueTask<CallState> ArrayJSON(ConcurrentDictionary<string, CallState> args)
	{
		if (args.Count < 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, int.MaxValue, args.Count)));
		}

		try
		{
			var sortedArgs = args.AsReadOnly()
													 .OrderBy(x => int.Parse(x.Key))
													 .Select(x => JsonDocument.Parse(x.Value.Message!.ToString()).RootElement)
													 .Skip(1);

			return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(sortedArgs)));
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json")));
		}
	}

	private static ValueTask<CallState> ObjectJSON(ConcurrentDictionary<string, CallState> args)
	{
		if (args.Count < 3)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, int.MaxValue, args.Count)));
		}

		if (args.Count % 2 == 0)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorGotEvenArgs, "json")));
		}

		var sortedArgs = args.AsReadOnly().OrderBy(x => int.Parse(x.Key)).Select(x => x.Value.Message!.ToString()).Skip(1);
		var chunkedArgs = sortedArgs.Chunk(2);
		var duplicateKeys = chunkedArgs.Select(x => x[0]).Duplicates();

		if (duplicateKeys.Any())
		{
			return ValueTask.FromResult(new CallState($"#-1 DUPLICATE KEYS: {string.Join(", ", duplicateKeys)}"));
		}

		try
		{
			var dictionary = chunkedArgs.ToDictionary(x => x.First(), x => JsonDocument.Parse(x.Last()).RootElement);
			return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(dictionary)));
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json")));
		}
	}

	[SharpFunction(Name = "json_map", MinArgs = 2, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> json_map(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "json_mod", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> json_mod(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "json_query", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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