using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "strdistance", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["source", "target", "ignore case", "transpositions"])]
	public ValueTask<CallState> StringDistanceFunction(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		var arguments = parser.CurrentState.Arguments;
		// RhostMUSH's third and fourth arguments: a non-zero number for each.
		var ignoreCase = arguments.TryGetValue("2", out var caseArg) && ArgHelpers.ParseInteger(caseArg.Message.ToPlainText()) != 0;
		var transpositions = arguments.TryGetValue("3", out var swapArg) && ArgHelpers.ParseInteger(swapArg.Message.ToPlainText()) != 0;
		return StringDistance.TryCalculate(arguments["0"].Message, arguments["1"].Message, ignoreCase, transpositions, out var distance)
			? ValueTask.FromResult<CallState>(distance)
			: ValueTask.FromResult<CallState>(StringDistance.WorkLimitExceeded);
	}

	[SharpFunction(Name = "soundex", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SoundEx(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var arg1 = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, "soundex").ToPlainText().ToLowerInvariant();

		return arg1 switch
		{
			"soundex" => ValueTask.FromResult<CallState>(ComputeSoundex(
				arg0.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg0[2..] : arg0)),
			"phone" => ValueTask.FromResult<CallState>(Spellfix.PhoneticHash(arg0)),
			_ => ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidHashType)
		};
	}

	[SharpFunction(Name = "soundslike", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SoundLike(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message.ToPlainText();
		var arg2 = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, "soundex").ToPlainText().ToLowerInvariant();

		return arg2 switch
		{
			"soundex" => ValueTask.FromResult<CallState>(
				ComputeSoundex(arg0.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg0[2..] : arg0) ==
				ComputeSoundex(arg1.StartsWith("ph", StringComparison.OrdinalIgnoreCase) ? "f" + arg1[2..] : arg1)
					? "1" : "0"),
			"phone" => ValueTask.FromResult<CallState>(
				Spellfix.PhoneticHash(arg0) == Spellfix.PhoneticHash(arg1) ? "1" : "0"),
			_ => ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidHashType)
		};
	}

	/// <summary>
	/// Compute the American Soundex code for a string.
	/// Standard mapping: B/F/P/V=1, C/G/J/K/Q/S/X/Z=2, D/T=3, L=4, M/N=5, R=6
	/// </summary>
	private string ComputeSoundex(string input)
	{
		if (string.IsNullOrEmpty(input)) return "0000";

		// Standard American Soundex mapping
		const string soundexMap = "01230120022455012623010202";
		// Index:                   A B C D E F G H I J K L M N O P Q R S T U V W X Y Z

		var result = new char[4];
		result[0] = char.ToUpper(input[0]);
		var lastCode = result[0] >= 'A' && result[0] <= 'Z'
			? soundexMap[result[0] - 'A']
			: '0';
		var count = 1;

		for (var i = 1; i < input.Length && count < 4; i++)
		{
			var c = char.ToUpper(input[i]);
			if (c < 'A' || c > 'Z') continue;

			var code = soundexMap[c - 'A'];
			if (code != '0' && code != lastCode)
			{
				result[count++] = code;
			}
			lastCode = code;
		}

		// Pad with zeros
		while (count < 4)
		{
			result[count++] = '0';
		}

		return new string(result);
	}

	[SharpFunction(Name = "suggest", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> Suggest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var category = args["0"].Message.ToPlainText();
		var word = args["1"].Message.ToPlainText();
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ").ToPlainText();
		var limit = 20;

		if (args.ContainsKey("3"))
		{
			// fun_suggest reads its limit with is_integer and refuses anything else with e_int
			// (src/help.c:1843-1847).
			if (!ArgHelpers.TryInteger(parser, args["3"].Message.ToPlainText(), out limit))
			{
				return new CallState(ErrorMessages.Returns.Integer);
			}

			if (limit < 1)
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}
		}

		// Get suggestion data from expanded server data
		var suggestionData = await ObjectDataService.GetExpandedServerDataAsync<SuggestionData>();
		if (suggestionData?.Categories == null || !suggestionData.Categories.ContainsKey(category))
		{
			// If category doesn't exist, return empty string
			return new CallState(string.Empty);
		}

		var vocabulary = suggestionData.Categories[category];
		if (vocabulary == null || vocabulary.Count == 0)
		{
			return new CallState(string.Empty);
		}

		return new CallState(string.Join(separator, Spellfix.Match(vocabulary, word, limit)));
	}
}
