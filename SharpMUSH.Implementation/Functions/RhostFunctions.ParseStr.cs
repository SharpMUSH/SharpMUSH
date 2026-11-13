using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// parsestr(): a string cut at a delimiter, every other piece evaluated with <c>%0</c> the piece and the pieces
	/// between kept as written, as RhostMUSH's parsestr() does for the quoted parts of speech. With a target, the
	/// string is first written as that target's speech: a say, a pose (<c>:</c>), a possessive pose (<c>;</c>) or
	/// an emit (<c>|</c>).
	/// </summary>
	/// <remarks>
	/// The evaluated pieces see <c>%0</c> the piece, <c>%1</c> the target's name, <c>%2</c> which evaluated piece
	/// it is from 0, <c>%3</c> which piece of all from 0, and <c>%4</c> the character that chose the other
	/// evaluation, if one did. Only the evaluation arguments wait to be evaluated; the rest are evaluated first.
	/// </remarks>
	[SharpFunction(Name = "parsestr", MinArgs = 2, MaxArgs = 11, Flags = FunctionFlags.NoParse,
		ParameterNames = ["string", "eval", "delimiter", "output", "type", "target", "transchar", "endchar", "transeval",
			"prefix", "emitprefix"])]
	public async ValueTask<CallState> ParseStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var raw = args.Values.Select(argument => argument.Message).ToArray();
		if (raw[0].Length == 0) return CallState.Empty;
		if (raw[1].Length == 0) return new CallState(raw[0]);

		var text = await Evaluated(0);
		if (text.ToPlainText().Length == 0) return CallState.Empty;

		var delimiter = Present(2) && (await Evaluated(2)).ToPlainText() is [var first, ..] ? first : ' ';
		string before, after;
		if (Present(3))
		{
			var output = (await Evaluated(3)).ToPlainText();
			var space = output.IndexOf(' ');
			(before, after) = output is not [' ', ..] && space >= 0 ? (output[..space], output[(space + 1)..]) : (output, output);
		}
		else
		{
			before = after = delimiter.ToString();
		}

		var oddEvaluated = Present(4) && RhostInteger((await Evaluated(4)).ToPlainText()) > 0;

		var targetName = "";
		if (Present(5))
		{
			var target = (await Evaluated(5)).ToPlainText();
			if (target is ['&', .. var name])
			{
				targetName = name;
			}
			else
			{
				var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
				if (await LocateService.Locate(parser, executor, executor, target, LocateFlags.All) is not AnySharpObject found)
				{
					await NotifyService.Notify(executor, "Permission denied.");
					return CallState.Empty;
				}

				targetName = (await new NameFormatter(AttributeService, Configuration).FormatAsync(found, NameContext.UnaccentedMoniker))
					.ToPlainText();
			}
		}

		var emitPrefix = Present(10) ? (await Evaluated(10)).ToPlainText() : "";
		var transEval = Present(8) ? raw[8] : null;
		var transChars = transEval is not null && Present(6) ? (await Evaluated(6)).ToPlainText() : "";
		var endChars = transEval is not null && Present(7) ? (await Evaluated(7)).ToPlainText() : "";
		var says = Present(9) ? (await Evaluated(9)).ToPlainText() : "says,";

		if (targetName.Length > 0)
		{
			text = AsSpeech(text, targetName, says, emitPrefix);
		}

		// A space delimiter takes a run of spaces as one, and none at either end.
		var pieces = delimiter != ' ' ? text.Split(delimiter.ToString()) : Words(text);

		var result = new List<MString>();
		var written = false;
		var evaluatedCount = 0;
		var length = 0L;
		for (var index = 0; index < pieces.Length; index++)
		{
			var piece = pieces[index];
			var evaluate = oddEvaluated ? index % 2 == 0 : index % 2 == 1;
			if (!evaluate)
			{
				if (written) result.Add(MarkupText.Plain(after));
				result.Add(piece);
				written = true;
				length += piece.Length + (index > 0 ? after.Length : 0);
				if (FunctionLimits.ExceedsOutput(parser.CurrentState, length)) return FunctionLimits.RejectOutput(parser.CurrentState);
				continue;
			}

			var plain = piece.ToPlainText();
			var chosen = plain.Length > 0 && transChars.Contains(plain[0]) ? plain[0]
				: plain.Length > 0 && endChars.Contains(plain[^1]) ? plain[^1]
				: (char?)null;
			var arguments = new Dictionary<string, CallState>
			{
				["0"] = new(piece),
				["1"] = new(targetName),
				["2"] = new(evaluatedCount),
				["3"] = new(index),
				["4"] = new(chosen?.ToString() ?? "")
			};
			evaluatedCount++;
			var code = chosen is null ? raw[1] : transEval!;
			var value = await parser.With(state => state with
			{
				Arguments = arguments,
				EnvironmentRegisters = arguments,
				SuppliedArgumentKeys = AttributeArgumentEnvironment.SuppliedKeys(arguments)
			}, scoped => ParseRawArgument(scoped, code)) ?? CallState.Empty;

			if (written) result.Add(MarkupText.Plain(before));
			result.Add(value.Message);
			written = true;
			// Each piece fits the limit by itself; together they must as well, before more are kept.
			length += value.Message.Length + (index > 0 ? before.Length : 0);
			if (FunctionLimits.ExceedsOutput(parser.CurrentState, length)) return FunctionLimits.RejectOutput(parser.CurrentState);
		}

		return new CallState(MarkupText.Concat(result.ToArray()));

		bool Present(int index) => index < raw.Length && raw[index].Length > 0;

		async ValueTask<MString> Evaluated(int index)
			=> (await ParseRawArgument(parser, raw[index]))?.Message ?? MarkupText.Empty;
	}

	/// <summary>
	/// What the target says, poses or emits, written out: a string starting with <c>:</c> is a pose and <c>;</c> a
	/// possessive pose (<c>: </c> and <c>; </c> swap the two), <c>|</c> an emit after the emit prefix, and anything
	/// else speech, its quotes closed when an odd number is left open.
	/// </summary>
	private static MString AsSpeech(MString speech, string name, string says, string emitPrefix)
	{
		var text = speech.ToPlainText();
		switch (text)
		{
			case ['"', ..]:
				var quotes = 0;
				var escaped = false;
				foreach (var c in text)
				{
					if (c is '\\' or '%') escaped = !escaped;
					if (c == '"' && !escaped) quotes++;
				}

				return Joined($"{name} {says} ", speech, quotes % 2 == 1 ? "\"" : "");
			case [':', not ' ', ..] or [':']:
				return Joined($"{name} ", WithoutLeading(speech, 1), "");
			case [';', ' ', ..]:
				return Joined($"{name} ", WithoutLeading(speech, 2), "");
			case [';', ..]:
				return Joined(name, WithoutLeading(speech, 1), "");
			case [':', ' ', ..]:
				return Joined(name, WithoutLeading(speech, 2), "");
			case ['|', ..]:
				return Joined(emitPrefix, WithoutLeading(speech, 1), "");
			default:
				return Joined($"{name} {says} \"", speech, "\"");
		}

		static MString Joined(string before, MString speech, string after)
			=> MarkupText.Concat([MarkupText.Plain(before), speech, MarkupText.Plain(after)]);
	}

	/// <summary>
	/// The speech without its first <paramref name="count"/> plain characters (the <c>:</c> or <c>; </c> that chose
	/// the form); points among them, such as a sound, stay.
	/// </summary>
	private static MString WithoutLeading(MString speech, int count)
	{
		var pieces = new List<MString>();
		var start = 0;
		for (var index = 0; index < count; index++)
		{
			var at = TextIndex(speech, index);
			if (at >= speech.Length) break;
			pieces.Add(speech.Substring(start, at - start));
			start = at + 1;
		}

		pieces.Add(speech.Substring(Math.Min(start, speech.Length)));
		return MarkupText.Concat(pieces.ToArray());
	}

	/// <summary>
	/// The words of <paramref name="text"/>: a run of spaces is one break and none count at either end, and a word
	/// with no plain text (only a point, such as a sound) is left out. One empty word when there are none.
	/// </summary>
	private static MString[] Words(MString text)
	{
		var words = text.Split(" ", StringSplitOptions.RemoveEmptyEntries);
		if (Array.Exists(words, word => word.ToPlainText().Length == 0))
			words = Array.FindAll(words, word => word.ToPlainText().Length > 0);
		return words.Length > 0 ? words : [MarkupText.Empty];
	}
}
