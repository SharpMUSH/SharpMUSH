using SharpMUSH.Library.Markup;
using DotNext.Collections.Generic;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "regmatch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "pattern", "registers"])]
	public ValueTask<CallState> regmatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegMatchInternal(parser, false);
	}

	[SharpFunction(Name = "regmatchi", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "pattern", "registers"])]
	public ValueTask<CallState> regmatchi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegMatchInternal(parser, true);
	}

	[SharpFunction(Name = "regrab", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> regrab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrabInternal(parser, false, false);
	}

	[SharpFunction(Name = "regraball", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> regraball(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrabInternal(parser, false, true);
	}

	[SharpFunction(Name = "regraballi", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> regraballi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrabInternal(parser, true, true);
	}

	[SharpFunction(Name = "regrabi", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> regrabi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegGrabInternal(parser, true, false);
	}

	[SharpFunction(Name = "reglmatch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> reglmatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, false, false);
	}

	[SharpFunction(Name = "reglmatchi", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> reglmatchi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, true, false);
	}

	[SharpFunction(Name = "reglmatchall", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> reglmatchall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, false, true);
	}

	[SharpFunction(Name = "reglmatchalli", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> reglmatchalli(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, true, true);
	}

	[SharpFunction(Name = "regmatchalli", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["string", "pattern", "registers"])]
	public ValueTask<CallState> regmatchalli(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return RegLMatchInternal(parser, true, true);
	}

	[SharpFunction(Name = "reswitch", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse,
		ParameterNames = ["text", "pattern...|result...", "default"])]
	public async ValueTask<CallState> reswitch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegSwitchInternal(parser, false, false);
	}

	[SharpFunction(Name = "reswitchall", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse,
		ParameterNames = ["text", "pattern...|result...", "default"])]
	public async ValueTask<CallState> reswitchall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegSwitchInternal(parser, false, true);
	}

	[SharpFunction(Name = "reswitchalli", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse,
		ParameterNames = ["text", "pattern...|result...", "default"])]
	public async ValueTask<CallState> reswitchalli(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegSwitchInternal(parser, true, true);
	}

	[SharpFunction(Name = "reswitchi", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse,
		ParameterNames = ["text", "pattern...|result...", "default"])]
	public async ValueTask<CallState> reswitchi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegSwitchInternal(parser, true, false);
	}

	/// <summary>
	/// Internal helper for regmatch and regmatchi.
	/// </summary>
	private ValueTask<CallState> RegMatchInternal(IMUSHCodeParser parser, bool caseInsensitive)
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

			var regex = SoftcodeRegex.Create(pattern, options);

			// An unanchored search, as in PennMUSH: fun_regmatch runs pcre2_match at offset 0 with
			// re_match_flags (0, never anchored) and returns `subpatterns >= 0` — src/funlist.c:2897,
			// and quick_regexp_match at src/wild.c:610 for the two-argument form. A successful
			// substring match returns 1; the pattern has to anchor itself to require the whole string.
			var match = regex.Match(str);

			// PennMUSH writes the boolean first and appends a bad-register report after it, so the
			// order here matches: safe_integer(...) then the register loop (src/funlist.c:2899).
			var result = match.Success ? "1" : "0";

			if (args.ContainsKey("2"))
			{
				var registerList = args["2"].Message!.ToPlainText();
				if (!string.IsNullOrWhiteSpace(registerList))
				{
					result += SetRegistersFromMatch(parser, match, registerList);
				}
			}

			return ValueTask.FromResult(new CallState(result));
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			// A player's pattern that cannot finish within SoftcodeRegex.MatchTimeout: an answer, not a crash.
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegexpTimeout));
		}
		catch (ArgumentException)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegexpInvalid));
		}
	}

	/// <summary>
	/// Helper to set registers from a regex match.
	/// </summary>
	/// <remarks>
	/// Every requested destination is written, including when the match failed. PennMUSH initialises
	/// each one to the empty string before filling it — src/funlist.c:2906, "Initialize every
	/// q-register used to ''" — and leaves it empty when there was no match (src/funlist.c:2947), so
	/// a failed regmatch clears what it was asked to fill instead of leaving a stale value behind.
	/// </remarks>
	/// <returns>
	/// The text to append to the function's result: empty normally, or one
	/// <see cref="ErrorMessages.Returns.BadRegName"/> per destination that cannot name a register,
	/// as PennMUSH appends e_badregname for each (src/funlist.c:2942).
	/// </returns>
	private string SetRegistersFromMatch(IMUSHCodeParser parser, Match match, string registerList)
	{
		var registers = registerList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var unusableNames = 0;

		for (var i = 0; i < registers.Length; i++)
		{
			// Split at the first colon only, as PennMUSH's strchr does: "0:x:y" names capture 0 and
			// the register "x:y", which is then rejected — rather than being read as positional and
			// silently clobbering the unrelated register named by its first segment.
			var parts = registers[i].Split(':', 2);

			// X:Y names the capture explicitly; a bare Y takes the capture at its own position in the
			// list, so the first element gets the whole match, the second the first capture, and so on.
			var captureIndexOrName = parts.Length == 2 ? parts[0] : i.ToString();

			// AddRegister only accepts [A-Z0-9_.-], so the name has to be uppercased or a destination
			// written in lowercase is silently dropped. Invariant, not ToUpper: PennMUSH uppercases
			// with ASCII strupper_r (src/parse.c:1406), whereas a Turkish-locale ToUpper turns "hit"
			// into "HİT" (U+0130), which AddRegister would then reject.
			var qRegister = (parts.Length == 2 ? parts[1] : parts[0]).ToUpperInvariant();

			// A bare "-" is an explicit discard. pi_regs_valid_key rejects it (src/parse.c:1407) and
			// fun_regmatch suppresses the error for that one spelling alone (src/funlist.c:2941), so
			// it must not be written either — AddRegister's pattern would otherwise accept it.
			if (qRegister == "-")
			{
				continue;
			}

			if (!parser.CurrentState.AddRegister(qRegister, MarkupText.Plain(CaptureValue(match, captureIndexOrName))))
			{
				unusableNames++;
			}
		}

		// Every report is the same string, so count them and build the result once rather than
		// concatenating inside the loop.
		return unusableNames == 0
			? string.Empty
			: string.Concat(Enumerable.Repeat(ErrorMessages.Returns.BadRegName, unusableNames));
	}

	/// <summary>
	/// The text of one capture of a match: the empty string when the match failed, when the capture
	/// does not exist, or when the group took no part in the match.
	/// </summary>
	private static string CaptureValue(Match match, string captureIndexOrName)
	{
		if (!match.Success)
		{
			return string.Empty;
		}

		// Both GroupCollection indexers answer a miss with an unsuccessful Group rather than throwing
		// — the numeric one range-checks through a uint cast, so a negative index misses too, and the
		// named one yields an empty group for a name the pattern does not define. That is the empty
		// string PennMUSH also produces for an out-of-range subpattern.
		var group = int.TryParse(captureIndexOrName, out var captureIndex)
			? match.Groups[captureIndex]
			: match.Groups[captureIndexOrName];

		return group.Success ? group.Value : string.Empty;
	}

	/// <summary>
	/// Internal helper for regrab, regrabi, regraball, regraballi.
	/// </summary>
	private ValueTask<CallState> RegGrabInternal(IMUSHCodeParser parser, bool caseInsensitive, bool all)
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

			var regex = SoftcodeRegex.Create(pattern, options);
			var splitList = MushText.SplitList(delimiter, list) ?? [];

			if (all)
			{
				var matches = splitList.Where(x => regex.IsMatch(x.ToPlainText()));
				return ValueTask.FromResult<CallState>(MarkupText.Join(outputSep, matches));
			}
			else
			{
				var firstMatch = splitList.FirstOrDefault(x => regex.IsMatch(x.ToPlainText()));
				return ValueTask.FromResult<CallState>(firstMatch ?? MarkupText.Empty);
			}
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			// A player's pattern that cannot finish within SoftcodeRegex.MatchTimeout: an answer, not a crash.
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegexpTimeout));
		}
		catch (ArgumentException)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegexpInvalid));
		}
	}

	/// <summary>
	/// Internal helper for reglmatch, reglmatchi, reglmatchall, regmatchalli.
	/// </summary>
	private ValueTask<CallState> RegLMatchInternal(IMUSHCodeParser parser, bool caseInsensitive, bool all)
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

			var regex = SoftcodeRegex.Create(pattern, options);
			var splitList = MushText.SplitList(delimiter, list) ?? [];

			if (all)
			{
				// Return positions of all matches (1-indexed)
				var positions = splitList
					.Select((item, index) => (item, index))
					.Where(x => regex.IsMatch(x.item.ToPlainText()))
					.Select(x => MarkupText.Plain((x.index + 1).ToString()));

				return ValueTask.FromResult<CallState>(MarkupText.Join(outputSep, positions));
			}
			else
			{
				// Return position of first match (1-indexed), or 0 if no match
				var position = Array.FindIndex(splitList, x => regex.IsMatch(x.ToPlainText()));

				return ValueTask.FromResult(new CallState(position + 1));
			}
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			// A player's pattern that cannot finish within SoftcodeRegex.MatchTimeout: an answer, not a crash.
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegexpTimeout));
		}
		catch (ArgumentException)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegexpInvalid));
		}
	}

	/// <summary>
	/// Internal helper for reswitch, reswitchi, reswitchall, reswitchalli: PennMUSH's <c>fun_reswitch</c>
	/// (<c>src/funmisc.c</c>).
	/// </summary>
	/// <remarks>
	/// A matched body is evaluated inside a regexp capture context, which <c>$&lt;digit&gt;</c> and
	/// <c>$&lt;name&gt;</c> read as it runs. The captures are never pasted into the body: a capture is
	/// the player's text, and evaluating it would run that text as softcode.
	/// </remarks>
	private async ValueTask<CallState> RegSwitchInternal(IMUSHCodeParser parser, bool caseInsensitive, bool all)
	{
		var input = await parser.CurrentState.Arguments["0"].GetParsedResultAsync();
		var hadErrors = input.HadErrors;
		var subject = input.Message ?? MarkupText.Empty;
		var str = subject.ToPlainText();
		var orderedArgs = parser.CurrentState.ArgumentsOrdered.Skip(1).ToList();

		// Check if we have a default (odd number of remaining args)
		var hasDefault = orderedArgs.Count % 2 == 1;
		KeyValuePair<string, CallState>? defaultValue = hasDefault ? orderedArgs[^1] : null;
		var pairCount = hasDefault ? orderedArgs.Count - 1 : orderedArgs.Count;

		var results = new List<MString>();
		var options = RegexOptions.None;
		if (caseInsensitive)
		{
			options |= RegexOptions.IgnoreCase;
		}

		var captures = new RegexpCaptureFrame(parser.CurrentState.CurrentEvaluation);
		parser.CurrentState.RegexRegisters.Push(captures);
		parser.CurrentState.SwitchStack.Push(subject);

		try
		{
			for (int i = 0; i < pairCount - 1; i += 2)
			{
				var patternResult = await orderedArgs[i].Value.GetParsedResultAsync();
				hadErrors |= patternResult.HadErrors;
				var patternStr = (patternResult.Message ?? MarkupText.Empty).ToPlainText();

				Regex regex;
				Match match;
				try
				{
					regex = SoftcodeRegex.Create(patternStr, options);
					match = regex.Match(str);
				}
				catch (ArgumentException)
				{
					// Invalid regex - skip this pattern
					continue;
				}
				catch (RegexMatchTimeoutException)
				{
					return new CallState(ErrorMessages.Returns.RegexpTimeout) { HadErrors = hadErrors };
				}

				if (!match.Success)
				{
					continue;
				}

				captures.Fill(regex, match, subject);
				var evaluated = await EvaluateSwitchBody(parser, orderedArgs[i + 1].Value, subject);
				hadErrors |= evaluated.HadErrors;
				var evaluatedMsg = evaluated.Message ?? MarkupText.Empty;
				results.Add(evaluatedMsg);

				if (!all)
				{
					return new CallState(evaluatedMsg) { HadErrors = hadErrors };
				}
			}

			if (results.Count > 0)
			{
				// PennMUSH concatenates results with no separator (like appending to a buffer)
				return new CallState(MarkupText.Concat(results)) { HadErrors = hadErrors };
			}

			if (defaultValue != null)
			{
				var defaultEvaluated = await EvaluateSwitchBody(parser, defaultValue.Value.Value, subject);
				return new CallState(defaultEvaluated.Message ?? MarkupText.Empty)
				{ HadErrors = hadErrors || defaultEvaluated.HadErrors };
			}

			return new CallState(MarkupText.Empty) { HadErrors = hadErrors };
		}
		finally
		{
			parser.CurrentState.SwitchStack.TryPop(out _);
			parser.CurrentState.RegexRegisters.TryPop(out _);
		}
	}

	/// <summary>
	/// A reswitch body or default. PennMUSH replaces <c>#$</c> with the subject text before evaluating
	/// either (<c>replace_string("#$", mstr, ...)</c>), so a body that uses it is re-parsed from its text;
	/// every other body is evaluated as it was parsed.
	/// </summary>
	private static async ValueTask<CallState> EvaluateSwitchBody(IMUSHCodeParser parser, CallState body, MString subject)
	{
		var text = (body.Message ?? MarkupText.Empty).ToPlainText();
		if (!text.Contains("#$", StringComparison.Ordinal))
		{
			return await body.GetParsedResultAsync();
		}

		return await parser.FunctionParse(MarkupText.Plain(text.Replace("#$", subject.ToPlainText())))
			?? new CallState(MarkupText.Empty);
	}

	[SharpFunction(Name = "REGREPLACE", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "pattern", "replacement", "flags"])]
	public ValueTask<CallState> RegReplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var pattern = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var replacement = parser.CurrentState.Arguments["2"].Message!.ToPlainText();
		var flags = parser.CurrentState.Arguments.TryGetValue("3", out var flagsArg)
			? flagsArg.Message!.ToPlainText().ToLowerInvariant()
			: "";

		try
		{
			var regexOptions = RegexOptions.None;

			if (flags.Contains('i'))
			{
				regexOptions |= RegexOptions.IgnoreCase;
			}

			// 'g' flag for global replace is default behavior of Regex.Replace
			// So we don't need special handling for it

			var result = Regex.Replace(str, pattern, replacement, regexOptions);
			return ValueTask.FromResult<CallState>(result);
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidRegex);
		}
	}
}
