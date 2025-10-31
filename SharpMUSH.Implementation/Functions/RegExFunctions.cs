using System.Text.RegularExpressions;
using DotNext.Collections.Generic;
using Microsoft.FSharp.Core;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "regmatch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regmatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegMatchInternal(parser, false);
	}

	[SharpFunction(Name = "regmatchi", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regmatchi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegMatchInternal(parser, true);
	}

	[SharpFunction(Name = "regrab", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regrab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrabInternal(parser, false, false);
	}

	[SharpFunction(Name = "regraball", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regraball(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrabInternal(parser, false, true);
	}

	[SharpFunction(Name = "regraballi", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regraballi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrabInternal(parser, true, true);
	}

	[SharpFunction(Name = "regrabi", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regrabi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrabInternal(parser, true, false);
	}

	[SharpFunction(Name = "reglmatch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> reglmatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, false, false);
	}

	[SharpFunction(Name = "reglmatchi", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> reglmatchi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, true, false);
	}

	[SharpFunction(Name = "reglmatchall", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> reglmatchall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, false, true);
	}

	[SharpFunction(Name = "regmatchalli", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> regmatchalli(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, true, true);
	}

	[SharpFunction(Name = "reswitch", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> reswitch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegSwitchInternal(parser, false, false);
	}

	[SharpFunction(Name = "reswitchall", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> reswitchall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegSwitchInternal(parser, false, true);
	}

	[SharpFunction(Name = "reswitchalli", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> reswitchalli(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegSwitchInternal(parser, true, true);
	}

	[SharpFunction(Name = "reswitchi", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> reswitchi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegSwitchInternal(parser, true, false);
	}

	/// <summary>
	/// Internal helper for regmatch and regmatchi.
	/// </summary>
	private static ValueTask<CallState> RegMatchInternal(IMUSHCodeParser parser, bool caseInsensitive)
	{
		var args = parser.CurrentState.Arguments;
		var str = args["0"].Message!.ToPlainText();
		var pattern = args["1"].Message!.ToPlainText();
		
		try
		{
			var options = RegexOptions.None;
			if (caseInsensitive)
			{
				options |= RegexOptions.IgnoreCase;
			}
			
			var regex = new Regex(pattern, options);
			var match = regex.Match(str);
			
			// Check if the entire string matches (not just a substring)
			var isFullMatch = match.Success && match.Index == 0 && match.Length == str.Length;
			
			// Handle register list if provided
			if (args.ContainsKey("2"))
			{
				var registerList = args["2"].Message!.ToPlainText();
				if (!string.IsNullOrWhiteSpace(registerList))
				{
					SetRegistersFromMatch(parser, match, registerList);
				}
			}
			
			return ValueTask.FromResult(new CallState(isFullMatch ? "1" : "0"));
		}
		catch (ArgumentException ex)
		{
			return ValueTask.FromResult(new CallState($"#-1 REGEXP ERROR: {ex.Message}"));
		}
	}

	/// <summary>
	/// Helper to set registers from a regex match.
	/// </summary>
	private static void SetRegistersFromMatch(IMUSHCodeParser parser, Match match, string registerList)
	{
		if (!match.Success) return;
		
		var registers = registerList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		
		for (int i = 0; i < registers.Length; i++)
		{
			var reg = registers[i];
			var parts = reg.Split(':');
			
			string captureIndexOrName;
			string qRegister;
			
			if (parts.Length == 2)
			{
				// X:Y format - X is capture, Y is q-register
				captureIndexOrName = parts[0];
				qRegister = parts[1];
			}
			else
			{
				// Just Y format - use position-based capture
				// First element (i=0) gets full match, second (i=1) gets first capture, etc.
				captureIndexOrName = i.ToString();
				qRegister = parts[0];
			}
			
			// Get the capture value
			string value = "";
			if (int.TryParse(captureIndexOrName, out int captureIndex))
			{
				// Numeric capture
				if (captureIndex < match.Groups.Count)
				{
					value = match.Groups[captureIndex].Value;
				}
			}
			else
			{
				// Named capture
				var group = match.Groups[captureIndexOrName];
				if (group.Success)
				{
					value = group.Value;
				}
			}
			
			// Set the q-register
			parser.CurrentState.AddRegister(qRegister, MModule.single(value));
		}
	}

	/// <summary>
	/// Internal helper for regrab, regrabi, regraball, regraballi.
	/// </summary>
	private static ValueTask<CallState> RegGrabInternal(IMUSHCodeParser parser, bool caseInsensitive, bool all)
	{
		var args = parser.CurrentState.Arguments;
		var list = args["0"].Message!;
		var pattern = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var outputSep = all && args.ContainsKey("3") 
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, delimiter.ToPlainText()) 
			: delimiter;
		
		try
		{
			var options = RegexOptions.None;
			if (caseInsensitive)
			{
				options |= RegexOptions.IgnoreCase;
			}
			
			var regex = new Regex(pattern, options);
			var splitList = MModule.split2(delimiter, list) ?? [];
			
			if (all)
			{
				var matches = splitList.Where(x => regex.IsMatch(x.ToPlainText()));
				return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(outputSep, matches));
			}
			else
			{
				var firstMatch = splitList.FirstOrDefault(x => regex.IsMatch(x.ToPlainText()));
				return ValueTask.FromResult<CallState>(firstMatch ?? MModule.empty());
			}
		}
		catch (ArgumentException ex)
		{
			return ValueTask.FromResult(new CallState($"#-1 REGEXP ERROR: {ex.Message}"));
		}
	}

	/// <summary>
	/// Internal helper for reglmatch, reglmatchi, reglmatchall, regmatchalli.
	/// </summary>
	private static ValueTask<CallState> RegLMatchInternal(IMUSHCodeParser parser, bool caseInsensitive, bool all)
	{
		var args = parser.CurrentState.Arguments;
		var list = args["0"].Message!;
		var pattern = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var outputSep = all && args.ContainsKey("3")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, delimiter.ToPlainText())
			: delimiter;
		
		try
		{
			var options = RegexOptions.None;
			if (caseInsensitive)
			{
				options |= RegexOptions.IgnoreCase;
			}
			
			var regex = new Regex(pattern, options);
			var splitList = MModule.split2(delimiter, list) ?? [];
			
			if (all)
			{
				// Return positions of all matches (1-indexed)
				var positions = splitList
					.Select((item, index) => new { item, index })
					.Where(x => regex.IsMatch(x.item.ToPlainText()))
					.Select(x => MModule.single((x.index + 1).ToString()));
				
				return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(outputSep, positions));
			}
			else
			{
				// Return position of first match (1-indexed), or 0 if no match
				var position = splitList
					.Select((item, index) => new { item, index })
					.FirstOrDefault(x => regex.IsMatch(x.item.ToPlainText()));
				
				return ValueTask.FromResult(new CallState(position != null ? (position.index + 1).ToString() : "0"));
			}
		}
		catch (ArgumentException ex)
		{
			return ValueTask.FromResult(new CallState($"#-1 REGEXP ERROR: {ex.Message}"));
		}
	}

	/// <summary>
	/// Internal helper for reswitch, reswitchi, reswitchall, reswitchalli.
	/// </summary>
	private static async ValueTask<CallState> RegSwitchInternal(IMUSHCodeParser parser, bool caseInsensitive, bool all)
	{
		var arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var str = arg0!.ToPlainText();
		var orderedArgs = parser.CurrentState.ArgumentsOrdered.Skip(1).ToList();
		
		// Check if we have a default (odd number of remaining args)
		var hasDefault = orderedArgs.Count % 2 == 1;
		KeyValuePair<string, CallState>? defaultValue = hasDefault ? orderedArgs.Last() : (KeyValuePair<string, CallState>?)null;
		var patternListPairs = hasDefault ? orderedArgs.SkipLast(1).ToList() : orderedArgs;
		
		var results = new List<MString>();
		var options = RegexOptions.None;
		if (caseInsensitive)
		{
			options |= RegexOptions.IgnoreCase;
		}
		
		// Process pattern/list pairs manually (every 2 elements)
		for (int i = 0; i < patternListPairs.Count - 1; i += 2)
		{
			var patternKv = patternListPairs[i];
			var listKv = patternListPairs[i + 1];
			
			var pattern = await patternKv.Value.ParsedMessage();
			var patternStr = pattern!.ToPlainText();
			
			try
			{
				var regex = new Regex(patternStr, options);
				var match = regex.Match(str!);
				
				if (match.Success)
				{
					// Replace #$ with the original string and $N with captures
					var list = listKv.Value.Message!.ToPlainText();
					list = list.Replace("#$", str);
					
					// Replace numbered captures ($0, $1, etc.)
					for (int j = 0; j < match.Groups.Count; j++)
					{
						list = list.Replace($"${j}", match.Groups[j].Value);
					}
					
					// Replace named captures ($<name>)
					foreach (var groupName in regex.GetGroupNames())
					{
						if (!int.TryParse(groupName, out _))
						{
							var group = match.Groups[groupName];
							if (group.Success)
							{
								list = list.Replace($"$<{groupName}>", group.Value);
							}
						}
					}
					
					// Parse and evaluate the list
					var evaluated = (await parser.FunctionParse(MModule.single(list))) ?? new CallState(MModule.empty());
					var evaluatedMsg = evaluated.Message ?? MModule.empty();
					results.Add(evaluatedMsg);
					
					if (!all)
					{
						// Return first match for reswitch/reswitchi
						return new CallState(evaluatedMsg);
					}
				}
			}
			catch (ArgumentException)
			{
				// Invalid regex - skip this pattern
				continue;
			}
		}
		
		// If we got here and have results (for reswitchall/reswitchalli), return them
		if (results.Any())
		{
			return new CallState(MModule.multipleWithDelimiter(MModule.single(" "), results));
		}
		
		// No matches - return default if available
		if (defaultValue != null)
		{
			var defaultEvaluated = await defaultValue.Value.Value.ParsedMessage();
			return new CallState(defaultEvaluated!);
		}
		
		return new CallState(string.Empty);
	}
}