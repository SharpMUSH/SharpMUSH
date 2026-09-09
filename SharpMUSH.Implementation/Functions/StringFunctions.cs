using DotNext.Collections.Generic;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Library.Markup;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private static readonly Dictionary<(string, string), Regex> SpeechPatternCache = new();

	[SharpFunction(Name = "after", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "substring"])]
	public ValueTask<CallState> After(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fullString = args["0"].Message ?? MarkupText.Empty;
		var search = args["1"].Message ?? MarkupText.Empty;
		var idx = fullString.IndexOf(search.ToPlainText());

		if (idx == -1)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		// after() returns everything *after* the match, excluding the delimiter itself, so start past it.
		var start = idx + search.ToPlainText().Length;
		var result = fullString.Substring(start, fullString.Length - start);

		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "lit", MinArgs = 0, Flags = FunctionFlags.Literal | FunctionFlags.NoParse, ParameterNames = ["argument..."])]
	public ValueTask<CallState> Lit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// lit() with Literal flag: args are already the raw unevaluated text (set by visitor's Literal branch).
		// With zero args, return empty string. Otherwise return the single raw argument.
		if (parser.CurrentState.ArgumentsOrdered.IsEmpty)
			return ValueTask.FromResult(CallState.Empty);

		return ValueTask.FromResult(new CallState(parser.CurrentState.Arguments["0"].Message) { PreserveSpaces = true });
	}

	[SharpFunction(Name = "speak", MinArgs = 2, MaxArgs = 7, Flags = FunctionFlags.Regular,
		ParameterNames = ["speaker", "string", "say-string", "transform-attr", "isnull-attr", "open", "close"])]
	public async ValueTask<CallState> Speak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		/*
	speak(<speaker>, <string>[, <say string>[, [<transform obj>/]<transform attr>[, [<isnull obj>/]<isnull attr>[, <open>[, <close>]]]]])

  This function is used to format speech-like constructs, and is capable of transforming text within a speech string; it is useful for implementing "language code" and the like.

  If <speaker> begins with &, the rest of the <speaker> string is treated as the speaker's name, so you can use it for NPCs or tacking on titles (such as with @chatformat). Otherwise, the name of the object <speaker> is used.

  When only <speaker> and <string> are given, this function formats <string> as if it were speech from <speaker>, as follows.

  If <string> is...  the resulting string is...
  :<pose>            <speaker's name> <pose>
  ;<pose>            <speaker's name><pose>
  |<emit>            <emit>
  <speech>           <speaker's name> says, "<speech>"

  The chat_strip_quote config option affects this function, so if <speech> starts with a leading double quote ("), it may be stripped.

  If <say string> is specified, it is used instead of "says,".
		 */
		var args = parser.CurrentState.ArgumentsOrdered;
		var speaker = args["0"].Message!; // & for direct name!
		var speakString = args["1"].Message!;
		var sayString = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, "says, ");
		var transformObjAttr = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, "");
		var isNullObjAttr = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, "");
		var open = ArgHelpers.NoParseDefaultNoParseArgument(args, 5, "\"");
		var close = ArgHelpers.NoParseDefaultNoParseArgument(args, 6, "\"");

		var plainSpeak = speakString.ToPlainText();
		var messageType = MessageHelpers.DetermineMessageType(plainSpeak);

		// Strip the prefix (including quotes)
		speakString = plainSpeak switch
		{
			[':', .. _]
				or [';', .. _]
				or ['|', .. _]
				or ['"', .. _] => speakString.Substring(1, speakString.Length - 1),
			_ => speakString
		};

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var speakerIsLiteral = speaker.ToPlainText().StartsWith('&');
		var hasTransform = !string.IsNullOrWhiteSpace(transformObjAttr.ToPlainText());
		var hasNull = !string.IsNullOrWhiteSpace(isNullObjAttr.ToPlainText());
		var speakerObject = executor;
		MString speakerName;

		if (!speakerIsLiteral)
		{
			var maybeFound = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
				speaker.ToPlainText(), LocateFlags.All);
			if (maybeFound.IsError)
			{
				return maybeFound.AsError;
			}

			var found = maybeFound.AsSharpObject;

			if (await PermissionService.Controls(executor, found))
			{
				speakerObject = found;
			}

			speakerName = MarkupText.Plain(speakerObject.Object().Name);
		}
		else
		{
			speakerName = speaker.Substring(1, speaker.Length - 1);
		}

		// If not Emit, use Speakername.
		// Build the prefix using ConcatMany to avoid O(N²) sequential concat.
		// List is allocated lazily to avoid an allocation for the Emit case (no prefix).
		List<MString>? parts = null;

		if (messageType is not INotifyService.NotificationType.Emit)
		{
			parts ??= new List<MString>(4);
			parts.Add(speakerName);
		}

		if (messageType is INotifyService.NotificationType.Pose or INotifyService.NotificationType.Say)
		{
			parts ??= new List<MString>(4);
			parts.Add(MarkupText.Space);
		}

		if (messageType is INotifyService.NotificationType.Say)
		{
			parts ??= new List<MString>(4);
			parts.Add(sayString);
			parts.Add(open);
		}

		var concat = parts is { Count: > 0 } ? MarkupText.Concat(parts) : MarkupText.Empty;

		/*
		  If <transform> is specified (an object/attribute pair or attribute, as with map() and similar functions),
		  the speech portions of <string> are passed through the transformation function.

			Speech is delimited by double-quotes (i.e., "text"), or by the specified <open> and <close> strings.
			For instance, if you wanted <<text>> to denote text to be transformed,
			you would specify <open> as << and close as >> in the function call.
			Only the portions of the string between those delimiters are transformed. If <close> is not specified,
			it defaults to <open>.

			The transformation function receives the speech text as %0, the dbref of <speaker> as %1,
			and the speech fragment number as %2.
			For non-say input strings (i.e., for an original <string> beginning with the :, ;, or | tokens),
			fragments are numbered starting with 1; otherwise,
			fragments are numbered starting with 0.
			(A fragment is a chunk of speech text within the overall original input string.)
		 */

		string? actualTransformAttribute = null;
		string? actualNullAttribute = null;
		AnySharpObject? actualTransformationObject = null;
		AnySharpObject? actualNullObject = null;

		if (hasTransform)
		{
			var splitTransform = HelperFunctions.SplitObjectAndAttr(transformObjAttr.ToPlainText());

			if (splitTransform.IsT1)
			{
				return new CallState(ErrorMessages.Returns.ObjectAttributeString);
			}

			var transformationObject = await
				LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
					executor,
					executor,
					splitTransform.AsT0.db,
					LocateFlags.All);

			if (transformationObject.IsError)
			{
				return transformationObject.AsError;
			}

			actualTransformationObject = transformationObject.AsSharpObject;
			actualTransformAttribute = splitTransform.AsT0.Attribute;
		}

		if (hasTransform && hasNull)
		{
			var splitNull = HelperFunctions.SplitObjectAndAttr(transformObjAttr.ToPlainText());

			if (splitNull.IsT1)
			{
				return new CallState(ErrorMessages.Returns.ObjectAttributeString);
			}

			actualNullAttribute = splitNull.AsT0.Attribute;

			var nullObject = await
				LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
					executor,
					executor,
					splitNull.AsT0.db,
					LocateFlags.All);

			if (nullObject.IsError)
			{
				return nullObject.AsError;
			}
		}

		if (hasTransform)
		{
			var safeOpen = Regex.Escape(open.ToPlainText());
			var safeClose = Regex.Escape(close.ToPlainText());
			var pattern = SpeechPatternCache.GetOrAdd((safeOpen, safeClose),
				_ => SoftcodeRegex.Create($"{safeOpen}(?<Content>[^{safeClose}]){safeClose}", RegexOptions.None)
			);

			var contents = pattern.Matches(speakString.ToPlainText());
			var markupContents = contents
				.Select(x => x.Groups["Content"]);

			foreach (var markupContent in markupContents)
			{
				var content = speakString.Substring(markupContent.Index, markupContent.Length);

				if (actualNullAttribute is not null)
				{
					var nullEvaluated = await AttributeService.EvaluateAttributeFunctionAsync(
						parser, executor, actualNullObject!, actualNullAttribute,
						new Dictionary<string, CallState>
						{
							{ "0", args["0"] },
							{ "1", new CallState(MarkupText.Plain(speakerObject.Object().DBRef.ToString())) },
							{ "2", new CallState(content) }
						});

					if (nullEvaluated.Truthy(parser)) continue;
				}

				var evaluated = await AttributeService.EvaluateAttributeFunctionAsync(
					parser, executor, actualTransformationObject!, actualTransformAttribute ?? string.Empty,
					new Dictionary<string, CallState>
					{
						{ "0", args["0"] },
						{ "1", new CallState(MarkupText.Plain(speakerObject.Object().DBRef.ToString())) },
						{ "2", new CallState(content) }
					});

				speakString = speakString.Replace(markupContent.Index, markupContent.Length, evaluated);
			}
		}
		else
		{
			concat = MarkupText.Concat(concat, speakString);
		}

		if (messageType is INotifyService.NotificationType.Say)
		{
			concat = MarkupText.Concat(concat, close);
		}

		return new CallState(concat);
	}

	[SharpFunction(Name = "strinsert", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "position", "insert"])]
	public ValueTask<CallState> StrInsert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var positionStr = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var insert = parser.CurrentState.Arguments["2"].Message!;

		if (!int.TryParse(positionStr, out var position) || position < 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		// If position is greater than length, append
		if (position >= str.Length)
		{
			return ValueTask.FromResult(new CallState(MarkupText.Concat(str, insert)));
		}

		return ValueTask.FromResult(new CallState(str.Insert(position, insert)));
	}

	[SharpFunction(Name = "strreplace", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["string", "position", "character"])]
	public ValueTask<CallState> StrReplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var startStr = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var lengthStr = parser.CurrentState.Arguments["2"].Message!.ToPlainText();
		var text = parser.CurrentState.Arguments["3"].Message!;

		if (!int.TryParse(startStr, out var start) || start < 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		if (!int.TryParse(lengthStr, out var length))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		// If start is greater than length, return original string
		if (start >= str.Length)
		{
			return ValueTask.FromResult(new CallState(str));
		}

		// Replace the section
		var result = str.Replace(start, length, text);

		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "strcat", Flags = FunctionFlags.Regular, ParameterNames = ["string..."])]
	public ValueTask<CallState> Concat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(MarkupText.Concat(parser.CurrentState.ArgumentsOrdered
				.Select(x => x.Value.Message ?? MarkupText.Empty)));

	[SharpFunction(Name = "cat", Flags = FunctionFlags.Regular, ParameterNames = ["string..."])]
	public ValueTask<CallState> Cat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(MarkupText.Join(MarkupText.Plain(" "), parser.CurrentState.ArgumentsOrdered.Select(x => x.Value.Message ?? MarkupText.Empty)));

	[SharpFunction(Name = "accent", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "template"])]
	public ValueTask<CallState> Accent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var template = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (str.Length != template.Length)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));
		}

		var result = string.Create(str.Length, (str, template), static (span, state) =>
		{
			var (text, accents) = state;
			for (var i = 0; i < span.Length; i++)
			{
				span[i] = ApplyAccent(text[i], accents[i]);
			}
		});

		return ValueTask.FromResult(new CallState(result));
	}

	private static char ApplyAccent(char c, char template)
	{
		// Accent mappings based on pennfunc.md ACCENTS table
		return (template, c) switch
		{
			// Grave accent (`)
			('`', 'A') => 'À',
			('`', 'E') => 'È',
			('`', 'I') => 'Ì',
			('`', 'O') => 'Ò',
			('`', 'U') => 'Ù',
			('`', 'a') => 'à',
			('`', 'e') => 'è',
			('`', 'i') => 'ì',
			('`', 'o') => 'ò',
			('`', 'u') => 'ù',

			// Acute accent (')
			('\'', 'A') => 'Á',
			('\'', 'E') => 'É',
			('\'', 'I') => 'Í',
			('\'', 'O') => 'Ó',
			('\'', 'U') => 'Ú',
			('\'', 'Y') => 'Ý',
			('\'', 'a') => 'á',
			('\'', 'e') => 'é',
			('\'', 'i') => 'í',
			('\'', 'o') => 'ó',
			('\'', 'u') => 'ú',
			('\'', 'y') => 'ý',

			// Tilde (~)
			('~', 'A') => 'Ã',
			('~', 'N') => 'Ñ',
			('~', 'O') => 'Õ',
			('~', 'a') => 'ã',
			('~', 'n') => 'ñ',
			('~', 'o') => 'õ',

			// Circumflex (^)
			('^', 'A') => 'Â',
			('^', 'E') => 'Ê',
			('^', 'I') => 'Î',
			('^', 'O') => 'Ô',
			('^', 'U') => 'Û',
			('^', 'a') => 'â',
			('^', 'e') => 'ê',
			('^', 'i') => 'î',
			('^', 'o') => 'ô',
			('^', 'u') => 'û',

			// Umlaut/Diaeresis (:)
			(':', 'A') => 'Ä',
			(':', 'E') => 'Ë',
			(':', 'I') => 'Ï',
			(':', 'O') => 'Ö',
			(':', 'U') => 'Ü',
			(':', 'a') => 'ä',
			(':', 'e') => 'ë',
			(':', 'i') => 'ï',
			(':', 'o') => 'ö',
			(':', 'u') => 'ü',
			(':', 'y') => 'ÿ',

			// Ring (o)
			('o', 'A') => 'Å',
			('o', 'a') => 'å',

			// Cedilla (,)
			(',', 'C') => 'Ç',
			(',', 'c') => 'ç',

			// Special characters
			('u', '?') => '¿',
			('u', '!') => '¡',
			('"', '<') => '«',
			('"', '>') => '»',
			('B', 's') => 'ß',
			('|', 'P') => 'Þ',
			('|', 'p') => 'þ',
			('-', 'D') => 'Ð',
			('&', 'o') => 'ð',

			_ => c
		};
	}

	[SharpFunction(Name = "align", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["widths", "col", "filler", "colsep", "rowsep"])]
	public async ValueTask<CallState> Align(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		var widths = args["0"].Message!.ToPlainText();

		var widthSpecs = widths.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (widthSpecs.Length == 0)
		{
			return ErrorMessages.Returns.InvalidAlignString;
		}

		var expectedColumnCount = widthSpecs.Length;
		var totalArgs = args.Count - 1;

		if (totalArgs < expectedColumnCount)
		{
			return ErrorMessages.Returns.NotEnoughColumnsForAlign;
		}

		// We can have at most expectedColumnCount + 3 arguments (columns + filler + colsep + rowsep)
		if (totalArgs > expectedColumnCount + 3)
		{
			return ErrorMessages.Returns.TooManyColumnsForAlign;
		}

		var columnArguments = args
			.Skip(1)
			.Take(expectedColumnCount)
			.Select(x => x.Value.Message!);

		// The remaining arguments are filler, colsep, rowsep (in that order)
		var remainder = args
			.Skip(1 + expectedColumnCount)
			.Select(x => x.Value.Message!)
			.ToArray();

		return TextAligner.Align(widths,
			columnArguments,
			filler: remainder.ElementAtOrDefault(0) ?? MarkupText.Space,
			columnSeparator: remainder.ElementAtOrDefault(1) ?? MarkupText.Space,
			rowSeparator: remainder.ElementAtOrDefault(2) ?? MarkupText.NewLine);
	}

	[SharpFunction(Name = "lalign", MinArgs = 2, MaxArgs = 6, Flags = FunctionFlags.Regular, ParameterNames = ["widths", "colList", "delim", "filler", "colsep", "rowsep"])]
	public async ValueTask<CallState> ListAlign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		var widths = args["0"].Message!.ToPlainText();
		var cols = args["1"].Message!;
		var colDelim = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		var filler = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Space);
		var columnSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, MarkupText.Space);
		var rowSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 5, MarkupText.NewLine);

		var widthSpecs = widths.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		if (widthSpecs.Length == 0)
		{
			return ErrorMessages.Returns.InvalidAlignString;
		}

		return TextAligner.Align(widths, cols.Split(colDelim), filler, columnSeparator, rowSeparator);
	}

	[SharpFunction(Name = "alphamax", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["word..."])]
	public ValueTask<CallState> AlphaMax(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.ArgumentsOrdered.Values.Select(x => x.Message!.ToPlainText());
		return ValueTask.FromResult(new CallState(list.Max() ?? string.Empty));
	}

	[SharpFunction(Name = "alphamin", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["word..."])]
	public ValueTask<CallState> AlphaMin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.ArgumentsOrdered.Values.Select(x => x.Message!.ToPlainText());
		return ValueTask.FromResult(new CallState(list.Min() ?? string.Empty));
	}

	/// <summary>
	/// Returns the Indefinite Article ('a' or 'an') of a word.
	/// </summary>
	/// <remarks>
	/// Uses the basic implementation found here: https://stackoverflow.com/a/8044744/1894135
	/// This is very specific to English. There are many edge cases that are not covered, and better solutions may exist.
	/// </remarks>
	[SharpFunction(Name = "art", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public async ValueTask<CallState> Art(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var nounPhrase = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		await ValueTask.CompletedTask;

		var m = GetWord().Match(nounPhrase);

		if (!m.Success)
		{
			return "an";
		}

		var word = m.Groups[0].Value;
		var wordLower = word.ToLower();

		if (AnWordPrefixes.Any(anWord => wordLower.StartsWith(anWord)))
		{
			return "an";
		}

		if (wordLower.StartsWith("hour") && !wordLower.StartsWith("houri"))
		{
			return "an";
		}


		if (wordLower.Length == 1)
		{
			return AnLetters.Contains(wordLower[0])
				? "an"
				: "a";
		}

		if (ArticleRegex().IsMatch(word))
		{
			return "an";
		}

		if (ArticleEuwRegex().IsMatch(wordLower)
				|| ArticleOnceRegex().IsMatch(wordLower)
				|| ArticleUniRegex().IsMatch(wordLower)
				|| ArticleUConsonantRegex().IsMatch(wordLower))
		{
			return "a";
		}

		if (ArticleRegex2().IsMatch(word))
		{
			return "a";
		}

		if (word == word.ToUpper())
		{
			return AnLetters.Contains(wordLower[0])
				? "an"
				: "a";
		}

		if (wordLower[0] is 'a' or 'e' or 'i' or 'o' or 'u')
		{
			return "an";
		}

		return ArticleRegex3().IsMatch(wordLower) ? "an" : "a";
	}

	/// <summary>Letters that take "an" when read as a letter (a single letter, or an initialism).</summary>
	private const string AnLetters = "aedhilmnorsx";

	private static readonly string[] AnWordPrefixes = ["euler", "heir", "honest", "hono"];

	[SharpFunction(Name = "before", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string1", "string2"])]
	public ValueTask<CallState> Before(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fullString = args["0"].Message ?? MarkupText.Empty;
		var search = args["1"].Message ?? MarkupText.Empty;
		var idx = fullString.IndexOf(search.ToPlainText());

		if (idx == -1)
		{
			return ValueTask.FromResult(new CallState(fullString));
		}

		var result = fullString.Substring(0, idx);

		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "brackets", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> Brackets(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		int leftSquare = 0, rightSquare = 0, leftParen = 0, rightParen = 0, leftCurly = 0, rightCurly = 0;
		foreach (var c in arg0)
		{
			switch (c)
			{
				case '[': leftSquare++; break;
				case ']': rightSquare++; break;
				case '(': leftParen++; break;
				case ')': rightParen++; break;
				case '{': leftCurly++; break;
				case '}': rightCurly++; break;
			}
		}

		return ValueTask.FromResult(
			new CallState($"{leftSquare} {rightSquare} {leftParen} {rightParen} {leftCurly} {rightCurly}"));
	}

	[SharpFunction(Name = "capstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> CapStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;

		if (arg0.Length < 1)
		{
			return new ValueTask<CallState>(CallState.Empty);
		}

		var leftSide = arg0.Substring(0, 1);
		var rightSide = arg0.Substring(1, arg0.Length - 1);
		var capitalized = leftSide.Apply(x => x.ToUpperInvariant());
		var concat = MarkupText.Concat(capitalized, rightSide);

		return new ValueTask<CallState>(new CallState(concat));
	}

	// No parity requirement: PennMUSH's fun_switch (which backs CASE/CASEALL/SWITCH) checks only
	// minargs, so both case(str,pat,res) and case(str,pat,res,default) are legal — and the trailing
	// default makes the common form even-numbered.
	[SharpFunction(Name = "case", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse,
		ParameterNames = ["expression", "case...|result...", "default"])]
	public async ValueTask<CallState> Case(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).SkipLast(1).Pairwise();
		var defaultValue = parser.CurrentState.ArgumentsOrdered.Last();

		foreach (var (expressionKv, listKv) in args)
		{
			var expression = await expressionKv.Value.ParsedMessage();

			if (arg0!.ToPlainText() == expression!.ToPlainText())
			{
				return await listKv.Value.ParsedMessage();
			}
		}

		return await defaultValue.Value.ParsedMessage();
	}

	[SharpFunction(Name = "caseall", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse, ParameterNames = ["string", "expression...|list...", "default"])]
	public async ValueTask<CallState> CaseAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();

		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).SkipLast(1).Pairwise();
		var defaultValue = parser.CurrentState.ArgumentsOrdered.Last();
		var list = new List<MString>();

		foreach (var (expressionKv, listKv) in args)
		{
			var expression = await expressionKv.Value.ParsedMessage();

			if (arg0!.ToPlainText() == expression!.ToPlainText())
			{
				list.Add(await listKv.Value.ParsedMessage() ?? MarkupText.Empty);
			}
		}

		return list.Count != 0
			? MarkupText.Concat(list)
			: await defaultValue.Value.ParsedMessage();
	}

	[SharpFunction(Name = "center", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["text", "width", "fill"])]
	public ValueTask<CallState> Center(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!;
		var fill = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		var rightFill = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, fill);

		if (!int.TryParse(width.ToPlainText(), out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		var result = str.Center(fill, rightFill, widthInt, TruncationType.Overflow);

		return new ValueTask<CallState>(new CallState(result));
	}

	[SharpFunction(Name = "chr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public ValueTask<CallState> Char(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (!int.TryParse(arg0, out var charInt) || charInt < 0)
		{
			return new ValueTask<CallState>(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		try
		{
			return ValueTask.FromResult<CallState>(char.ConvertFromUtf32(charInt));
		}
		catch (ArgumentOutOfRangeException)
		{
			return new ValueTask<CallState>(new CallState(ErrorMessages.Returns.ArgRange));
		}
	}

	[SharpFunction(Name = "comp", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string1", "string2"])]
	public ValueTask<CallState> Comp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var value1 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var value2 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var type = parser.CurrentState.Arguments.TryGetValue("2", out var typeArg)
			? typeArg.Message!.ToPlainText()?.ToUpperInvariant() ?? "A"
			: "A";

		int result = type switch
		{
			"I" => string.Compare(value1, value2, StringComparison.OrdinalIgnoreCase),
			"N" when int.TryParse(value1, out var int1) && int.TryParse(value2, out var int2) => int1.CompareTo(int2),
			"F" when decimal.TryParse(value1, out var dec1) && decimal.TryParse(value2, out var dec2) => dec1.CompareTo(dec2),
			"D" => CompareDbRefs(value1, value2),
			_ => string.Compare(value1, value2, StringComparison.Ordinal)
		};

		return ValueTask.FromResult(new CallState(result == 0 ? "0" : result < 0 ? "-1" : "1"));
	}

	private int CompareDbRefs(string value1, string value2)
	{
		// Try to parse as dbrefs (#123 or objid #123:timestamp format)
		var dbref1 = HelperFunctions.ParseDbRef(value1);
		var dbref2 = HelperFunctions.ParseDbRef(value2);

		if (dbref1.IsSome() && dbref2.IsSome())
		{
			return dbref1.AsValue().Number.CompareTo(dbref2.AsValue().Number);
		}

		// Fall back to string comparison if not valid dbrefs
		return string.Compare(value1, value2, StringComparison.Ordinal);
	}

	private static async ValueTask<CallState> EvaluateConditional(IMUSHCodeParser parser, bool negate, bool all)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var pairCount = args.Count / 2;
		var results = new List<MString>();
		for (var i = 0; i < pairCount; i++)
		{
			var condition = await parser.FunctionParse(args[(i * 2).ToString()].Message!);
			if (condition?.Message.Truthy(parser) == !negate)
			{
				var result = await parser.FunctionParse(args[(i * 2 + 1).ToString()].Message!);
				if (!all) return result ?? CallState.Empty;
				// Keep an empty selected result: the default runs only if no condition matched.
				results.Add(result?.Message ?? MarkupText.Empty);
			}
		}
		if (results.Count > 0) return MarkupText.Concat(results);
		return args.Count % 2 == 1
			? await parser.FunctionParse(args[(args.Count - 1).ToString()].Message!) ?? CallState.Empty
			: CallState.Empty;
	}

	[SharpFunction(Name = "cond", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["expression...|result...", "default"])]
	public ValueTask<CallState> Cond(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateConditional(parser, negate: false, all: false);

	[SharpFunction(Name = "condall", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["expression...|result...", "default"])]
	public ValueTask<CallState> CondAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateConditional(parser, negate: false, all: true);

	[SharpFunction(Name = "digest", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["algorithm", "string"])]
	public async ValueTask<CallState> Digest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText().ToUpperInvariant();
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var result)
			? result.Message!
			: null;

		if (arg1 is null && !arg0.Equals("LIST", StringComparison.InvariantCultureIgnoreCase))
		{
			return ErrorMessages.Returns.ArgRange;
		}

		if (arg0.Equals("LIST", StringComparison.InvariantCultureIgnoreCase))
		{
			return string.Join(" ", CryptoHelpers.hashAlgorithms.Keys);
		}

		return CryptoHelpers.hashAlgorithms.ContainsKey(arg0)
			? CryptoHelpers.Digest(arg0, arg1!).AsT0
			: ErrorMessages.Returns.ArgRange;
	}

	[SharpFunction(Name = "edit", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["string", "find", "replace"])]
	public ValueTask<CallState> Edit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var str = args["0"].Message!.ToPlainText();

		for (int i = 1; i < args.Count - 1; i += 2)
		{
			var search = args[i.ToString()].Message!.ToPlainText();
			var replace = args[(i + 1).ToString()].Message!.ToPlainText();

			if (search == "^")
			{
				// Prepend
				str = replace + str;
			}
			else if (search == "$")
			{
				// Append
				str = str + replace;
			}
			else if (string.IsNullOrEmpty(search))
			{
				// Insert between every character
				var result = new StringBuilder();
				result.Append(replace);
				foreach (var c in str)
				{
					result.Append(c);
					result.Append(replace);
				}

				str = result.ToString();
			}
			else
			{
				// Replace all occurrences
				str = str.Replace(search, replace);
			}
		}

		return ValueTask.FromResult(new CallState(str));
	}

	/// <summary>
	/// PennMUSH's <c>escaped_chars</c> table (src/tables.c): the characters the parser gives meaning
	/// to, which escape() backslashes and secure() blanks.
	/// </summary>
	[GeneratedRegex(@"[$%(),;\[\\\]^{}]")]
	private static partial Regex SoftcodeSpecial();

	/// <summary>
	/// <paramref name="text"/> with a backslash before every <see cref="SoftcodeSpecial"/> character;
	/// the text comes back unchanged when it holds none of them. This is for decompose(), which has
	/// flattened its markup into ansi() calls before it gets here; escape() edits the marked-up text.
	/// </summary>
	private static string EscapeSoftcode(string text) => SoftcodeSpecial().Replace(text, @"\$0");

	/// <summary>
	/// fun_escape (src/funstr.c): a leading backslash, then the text with every special escaped
	/// except one standing first — the leading backslash already protects it.
	/// </summary>
	[SharpFunction(Name = "escape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> Escape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var text = arg0.ToPlainText();
		if (text.Length == 0)
		{
			return ValueTask.FromResult(CallState.Empty);
		}

		// Each backslash is an insertion into the argument, so the markup around every special stays.
		var backslash = MarkupText.Plain("\\");
		var edits = new List<MarkupString.Edit>();
		foreach (var special in SoftcodeSpecial().EnumerateMatches(text, 1))
		{
			edits.Add(new MarkupString.Edit(special.Index, 0, backslash));
		}

		var escaped = edits.Count == 0 ? arg0 : arg0.Splice(CollectionsMarshal.AsSpan(edits));
		return ValueTask.FromResult<CallState>(MarkupText.Concat(backslash, escaped));
	}

	/// <summary>
	/// The text one grapheme cluster at a time, each piece keeping its markup. Character-level
	/// rearrangement (flip, scramble) has to move whole clusters: splitting on UTF-16 code units
	/// tears surrogate pairs and separates combining marks from what they combine with.
	/// </summary>
	private static MString[] SplitIntoGraphemes(MString text) => text.EnumerateGraphemes().ToArray();

	[SharpFunction(Name = "flip", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> Flip(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message;
		var pieces = SplitIntoGraphemes(arg0 ?? MarkupText.Empty);
		Array.Reverse(pieces);
		return new ValueTask<CallState>(new CallState(MarkupText.Concat(pieces)));
	}

	[SharpFunction(Name = "foreach", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.NoParse, ParameterNames = ["list", "pattern", "delimiter", "output-separator"])]
	public async ValueTask<CallState> ForEach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var listArg = (await parser.CurrentState.Arguments["0"].ParsedMessage())!;

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MushText.SplitList(delim, listArg);
		var wrappedIteration = new IterationWrapper<MString>
		{ Value = MarkupText.Empty, Break = false, NoBreak = false, Iteration = 0 };
		var result = new List<MString>();

		// Replace ## with %iL in the pattern for PennMUSH backward compatibility
		var patternArg = parser.CurrentState.Arguments["1"];
		var modifiedPattern = patternArg.Message!.Text.Contains("##")
			? patternArg.Message.ReplaceAll("##", MarkupText.Plain("%iL"))
			: null;

		parser.CurrentState.IterationRegisters.Push(wrappedIteration);

		foreach (var item in list)
		{
			wrappedIteration.Value = item!;
			wrappedIteration.Iteration++;
			var parsed = modifiedPattern != null
				? (await parser.FunctionParse(modifiedPattern))?.Message
				: await patternArg.ParsedMessage();
			result.Add(parsed!);

			if (wrappedIteration.Break)
			{
				break;
			}
		}

		parser.CurrentState.IterationRegisters.TryPop(out _);

		return new CallState(MarkupText.Join(sep, result));
	}

	[SharpFunction(Name = "decomposeweb", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> DecomposeWeb(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			MarkupWalker.EvaluateWith((markupType, innerText)
				=> markupType switch
				{
					Ansi ansiMarkup
						=> ReconstructWebCall(ansiMarkup.Style, WebEncodeAngleBrackets(innerText)),
					_ => WebEncodeAngleBrackets(innerText)
				}, parser.CurrentState.Arguments["0"].Message!));

	[SharpFunction(Name = "decompose", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> Decompose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var input = parser.CurrentState.Arguments["0"].Message!;

		// TODO: ANSI reconstruction needs to happen after text replacements to preserve
		// proper nesting structure. Current implementation may produce incorrect output when ANSI codes
		// interact with special character replacements.
		var reconstructed = MarkupWalker.EvaluateWith((markupType, innerText) =>
		{
			return markupType switch
			{
				Ansi ansiMarkup
					=> ReconstructAnsiCall(ansiMarkup.Style, innerText),
				_ => innerText
			};
		}, input);

		var result = EscapeSoftcode(reconstructed);

		// PennMUSH decompose space algorithm (from escape_marked_str in markup.c):
		// - 5+ consecutive spaces → [space(N)]
		// - 1-4 spaces: alternating literal-space / %b pattern
		//   - At start of string (dospace=1): first space → %b, then alternating space/%b
		//   - After non-space (dospace=0): alternating space/%b pairs
		//   - Trailing spaces: last space always becomes %b
		bool dospaceGlobal = result.Length > 0 && result[0] == ' ';
		result = SpacesRegex().Replace(result, m =>
		{
			int spaces = m.Length;
			if (spaces >= 5)
			{
				return $"[space({spaces})]";
			}

			var sb = new System.Text.StringBuilder();
			bool dospace = dospaceGlobal && m.Index == 0;

			// Check if this is a trailing run (at end of string)
			bool isTrailing = m.Index + m.Length == result.Length;

			if (isTrailing)
			{
				spaces--; // reserve last for final %b
				if (spaces > 0 && dospace) { spaces--; sb.Append("%b"); }
				while (spaces > 0) { sb.Append(' '); spaces--; if (spaces > 0) { spaces--; sb.Append("%b"); } }
				sb.Append("%b"); // final %b for trailing
			}
			else
			{
				if (dospace) { spaces--; sb.Append("%b"); }
				while (spaces > 0) { sb.Append(' '); spaces--; if (spaces > 0) { spaces--; sb.Append("%b"); } }
			}
			return sb.ToString();
		});

		result = result.Replace("\r", "%r").Replace("\n", "%r").Replace("\t", "%t");

		return ValueTask.FromResult(new CallState(result));
	}

	/// <summary>
	/// Reconstructs an ansi() function call from AnsiStyle and inner text
	/// </summary>
	internal static string ReconstructAnsiCall(AnsiStyle ansiDetails, string innerText)
	{
		var attributes = new List<string>();

		// Build formatting prefix (h for bold, u for underline, f for blink, i for invert)
		var formatPrefix = "";
		if (ansiDetails.Bold) formatPrefix += "h";
		if (ansiDetails.Underlined) formatPrefix += "u";
		if (ansiDetails.Blink) formatPrefix += "f";
		if (ansiDetails.Inverted) formatPrefix += "i";

		// Add foreground color (with formatting prefix if any)
		if (ansiDetails.Foreground is not null)
		{
			var colorCode = ConvertAnsiColorToCode(ansiDetails.Foreground);
			if (!string.IsNullOrEmpty(colorCode))
			{
				// If there's a formatting prefix and the color is a single character,
				// combine them (e.g., "ub" instead of "u,b")
				if (!string.IsNullOrEmpty(formatPrefix) && colorCode.Length == 1)
				{
					attributes.Add(formatPrefix + colorCode);
					formatPrefix = ""; // Clear format prefix since it's been used
				}
				else
				{
					// Add formatting prefix as separate attribute if not combined
					if (!string.IsNullOrEmpty(formatPrefix))
					{
						attributes.Add(formatPrefix);
						formatPrefix = "";
					}
					attributes.Add(colorCode);
				}
			}
			else if (!string.IsNullOrEmpty(formatPrefix))
			{
				// No valid color code, add format prefix anyway
				attributes.Add(formatPrefix);
				formatPrefix = "";
			}
		}
		else if (!string.IsNullOrEmpty(formatPrefix))
		{
			// No foreground color, add format prefix as standalone attribute
			attributes.Add(formatPrefix);
			formatPrefix = "";
		}

		// Add background color
		if (ansiDetails.Background is not null)
		{
			var colorCode = ConvertAnsiColorToCode(ansiDetails.Background, isBackground: true);
			if (!string.IsNullOrEmpty(colorCode))
				attributes.Add(colorCode);
		}

		if (attributes.Count > 0)
		{
			var attributeString = string.Join(",", attributes);
			return $"ansi({attributeString},{innerText})";
		}

		return innerText;
	}

	/// <summary>
	/// Encodes angle brackets for HTML/Web safety
	/// </summary>
	private string WebEncodeAngleBrackets(string text)
	{
		return text.Replace("<", "&lt;").Replace(">", "&gt;");
	}

	/// <summary>
	/// Reconstructs an ansi() function call from AnsiStyle and inner text
	/// </summary>
	private string ReconstructWebCall(AnsiStyle ansiDetails, string innerText)
	{
		Color foregroundColor = Color.Empty;
		Color backgroundColor = Color.Empty;

		if (ansiDetails.Foreground is not null)
		{
			foregroundColor = ConvertAnsiColorToRGB(ansiDetails.Foreground);
		}

		if (ansiDetails.Background is not null)
		{
			backgroundColor = ConvertAnsiColorToRGB(ansiDetails.Background);
		}

		return
			$"<span style=\"color:{(
				foregroundColor != Color.Empty
					? ColorTranslator.ToHtml(foregroundColor)
					: "inherit")};background-color:{(backgroundColor != Color.Empty
					? ColorTranslator.ToHtml(backgroundColor)
					: "inherit")};text-decoration:{(ansiDetails.Underlined
				? "underline"
				: "inherit")}\">{innerText}</span>";
	}

	/// <summary>The <c>ansi()</c> letter for each standard palette index, foreground and background.</summary>
	private const string ForegroundLetters = "xrgybmcw";
	private const string BackgroundLetters = "XRGYBMCW";

	/// <summary>
	/// Converts an <see cref="AnsiColor"/> back to the PennMUSH <c>ansi()</c> code that produces it.
	/// The terminal default has no code — it is the absence of one — so it converts to nothing.
	/// </summary>
	internal static string ConvertAnsiColorToCode(AnsiColor? color, bool isBackground = false) => color switch
	{
		null => string.Empty,
		AnsiColor.Default => isBackground ? "D" : "d",
		// The leading '#' is what makes this an ansi() hex code; without it the code came back as a
		// letter sequence ("FF0000" reads as bright white, bright magenta, …), so decompose() did not
		// round-trip through ansi(). Lower case to match the syntax help and ansi()'s own output.
		AnsiColor.Rgb rgb => isBackground
			? $"/#{rgb.R:x2}{rgb.G:x2}{rgb.B:x2}"
			: $"#{rgb.R:x2}{rgb.G:x2}{rgb.B:x2}",
		AnsiColor.Standard standard =>
			(standard.Bright ? "h" : string.Empty)
			+ (isBackground ? BackgroundLetters[standard.Index] : ForegroundLetters[standard.Index]),
		AnsiColor.Xterm xterm => isBackground ? $"/+xterm{xterm.Index}" : $"+xterm{xterm.Index}",
		// AnsiColor is a closed hierarchy (Default/Standard/Xterm/Rgb, private constructor); the
		// compiler cannot see that, so this arm exists only to satisfy exhaustiveness. Reaching it
		// means a fifth case was added to AnsiColor without updating this switch.
		_ => throw new UnreachableException($"Unhandled {nameof(AnsiColor)} subtype {color.GetType()}.")
	};

	/// <summary>
	/// Resolves an <see cref="AnsiColor"/> to 24-bit RGB for the web renderer.
	/// <see cref="Color.Empty"/> when the colour is unset or the terminal default, neither of which
	/// has a value the server knows.
	/// </summary>
	private static Color ConvertAnsiColorToRGB(AnsiColor? color) =>
		color?.ToRgb() is { } rgb ? Color.FromArgb(rgb.R, rgb.G, rgb.B) : Color.Empty;


	[SharpFunction(Name = "formdecode", MinArgs = 1, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> FormDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "").ToPlainText();
		var arg2 = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ").ToPlainText();

		return ValueTask.FromResult<CallState>((arg0, arg1, arg2) switch
		{
			// No paramname: the list of parameter names, duplicates included (help sharphttp:
			// "name,hobby,like,like"). NOTE: joining the NameValueCollection itself bound the
			// params-object[] Join overload and returned ToString() — the re-encoded query string.
			(var str, "", var outSep)
				=> string.Join(outSep, str
					.Split('&', StringSplitOptions.RemoveEmptyEntries)
					.Select(pair => HttpUtility.UrlDecode(pair.Split('=', 2)[0]))),
			var (str, field, outSep)
				=> string.Join(outSep, HttpUtility.ParseQueryString(str).GetValues(field) ?? []),
		});
	}

	/// <summary>
	/// formq(&lt;string&gt;[, &lt;prefix&gt;]) — decodes a form-encoded string (HTTP query string or
	/// form-urlencoded body) and sets one q-register per parameter, named
	/// &lt;prefix&gt;&lt;NORMALIZED-NAME&gt; (default prefix FORM., so name → %q&lt;form.name&gt;).
	/// Decoding matches formdecode() (%-unescaping, + as space). Array parameters collapse into
	/// one %r-joined register, whichever way the client spells them: repeated names
	/// (like=a&amp;like=b) and PHP/Rails-style brackets (like[]=a&amp;like[]=b) both produce
	/// %q&lt;form.like&gt; = a%rb — mirroring the %q&lt;hdr.*&gt; duplicate-header convention. Bare
	/// tokens (?flag with no =) become registers with an empty value. Returns the space-separated
	/// list of normalized parameter names (without the prefix), mirroring %q&lt;headers&gt;.
	/// SharpMUSH extension — no PennMUSH equivalent. See help formq / help sharphttp.
	/// </summary>
	[SharpFunction(Name = "formq", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string", "prefix"])]
	public ValueTask<CallState> FormQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var formString = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var prefix = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "FORM.").ToPlainText().ToUpperInvariant();

		// Form -> dictionary translation: accumulate values per normalized name, preserving
		// arrival order of both names and values. ParseQueryString does the wire decoding
		// (%-unescaping, + as space, duplicate keys); the dictionary fold above it merges the
		// equivalent array spellings (repeated names and trailing-[] names) into one entry.
		var parsed = HttpUtility.ParseQueryString(formString);
		var values = new Dictionary<string, List<string>>();
		var names = new List<string>();

		void Accumulate(string rawName, string value)
		{
			// PHP/Rails array marker: a single trailing "[]" denotes an array parameter.
			var trimmed = rawName.EndsWith("[]", StringComparison.Ordinal) ? rawName[..^2] : rawName;
			var normalized = RegisterNames.NormalizeSegment(trimmed);
			if (normalized.Length == 0)
			{
				return;
			}

			if (!values.TryGetValue(normalized, out var list))
			{
				list = [];
				values[normalized] = list;
				names.Add(normalized);
			}

			list.Add(value);
		}

		foreach (var key in parsed.AllKeys)
		{
			if (key is null)
			{
				// Bare tokens ("?flag" with no '='): ParseQueryString files each under the null
				// key with the token itself as the value. Register them as empty-valued flags.
				foreach (var bare in parsed.GetValues(null) ?? [])
				{
					Accumulate(bare, string.Empty);
				}
			}
			else
			{
				foreach (var value in parsed.GetValues(key) ?? [])
				{
					Accumulate(key, value);
				}
			}
		}

		var allValid = true;
		foreach (var name in names)
		{
			allValid &= parser.CurrentState.AddRegister(
				$"{prefix}{name}", MarkupText.Plain(string.Join('\n', values[name])));
		}

		return ValueTask.FromResult<CallState>(allValid
			? new CallState(string.Join(' ', names))
			: new CallState(ErrorMessages.Returns.BadRegName));
	}

	[SharpFunction(Name = "hmac", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["algorithm", "key", "string"])]
	public ValueTask<CallState> HashMessageAuthenticationCode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var digest = parser.CurrentState.Arguments["0"].Message!.ToPlainText().ToUpperInvariant();
		var key = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var text = parser.CurrentState.Arguments["2"].Message!.ToPlainText();
		var encoding = parser.CurrentState.Arguments.TryGetValue("3", out var encodingArg)
			? encodingArg.Message!.ToPlainText().ToLowerInvariant()
			: "base16";

		HMAC? hmac = digest switch
		{
			"MD5" => new HMACMD5(Encoding.UTF8.GetBytes(key)),
			"SHA1" => new HMACSHA1(Encoding.UTF8.GetBytes(key)),
			"SHA256" => new HMACSHA256(Encoding.UTF8.GetBytes(key)),
			"SHA384" => new HMACSHA384(Encoding.UTF8.GetBytes(key)),
			"SHA512" => new HMACSHA512(Encoding.UTF8.GetBytes(key)),
			_ => null
		};

		if (hmac == null)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));
		}

		using (hmac)
		{
			var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(text));

			var result = encoding switch
			{
				"base64" => Convert.ToBase64String(hash),
				_ => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
			};

			return ValueTask.FromResult(new CallState(result));
		}
	}

	[SharpFunction(Name = "if", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse, ParameterNames = ["boolean", "true-value", "false-value"])]
	public async ValueTask<CallState> If(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var truthy = parsedIfElse!.Truthy(parser);
		var result = CallState.Empty;

		if (truthy)
		{
			result = await parser.FunctionParse(parser.CurrentState.Arguments["1"].Message!);
		}
		else if (parser.CurrentState.Arguments.TryGetValue("2", out var arg2))
		{
			result = await parser.FunctionParse(arg2.Message!);
		}

		return result!;
	}

	[SharpFunction(Name = "ifelse", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.NoParse, ParameterNames = ["expression"])]
	public async ValueTask<CallState> IfElse(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var ifCase = parser.CurrentState.Arguments["1"].Message!;
		var elseCase = parser.CurrentState.Arguments["2"].Message!;
		var truthy = parsedIfElse!.Truthy(parser);
		CallState? result;

		if (truthy)
		{
			result = await parser.FunctionParse(ifCase);
		}
		else
		{
			result = await parser.FunctionParse(elseCase);
		}

		return result!;
	}

	[SharpFunction(Name = "lcstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> LowerCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return new ValueTask<CallState>(
			parser.CurrentState.Arguments["0"].Message!.Apply(transform: x => x.ToLowerInvariant()));
	}

	[SharpFunction(Name = "left", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "length"])]
	public ValueTask<CallState> Left(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var len = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return !int.TryParse(len, out var strlen) || strlen < 0
			? ValueTask.FromResult<CallState>(ErrorMessages.Returns.PositiveInteger)
			: ValueTask.FromResult<CallState>(str.Substring(0, int.Min(strlen, str.Length)));
	}

	[SharpFunction(Name = "ljust", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["text", "width", "fill", "truncate"])]
	public ValueTask<CallState> LeftJustifyString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var fill = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2,
			MarkupText.Space);
		var truncate = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, MarkupText.Plain("")).ToPlainText();

		if (!int.TryParse(width, out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.PositiveInteger);
		}

		var truncType = truncate == "1" ? TruncationType.Truncate : TruncationType.Overflow;
		return ValueTask.FromResult<CallState>(str.Pad(fill, widthInt, PadType.Right, truncType));
	}

	[SharpFunction(Name = "lpos", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string", "character"])]
	public ValueTask<CallState> ListPositions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			string.Join(" ",
				parser.CurrentState.Arguments["0"].Message!.IndexesOf(parser.CurrentState.Arguments["1"].Message!.Text)
					.Select(x => x.ToString())));

	[SharpFunction(Name = "merge", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public ValueTask<CallState> Merge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var string1 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var string2 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var separator = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		var parts1 = string.IsNullOrEmpty(separator)
			? new[] { string1 }
			: string1.Split(new[] { separator }, StringSplitOptions.None);
		var parts2 = string.IsNullOrEmpty(separator)
			? new[] { string2 }
			: string2.Split(new[] { separator }, StringSplitOptions.None);

		var result = new StringBuilder();
		var maxCount = Math.Max(parts1.Length, parts2.Length);

		for (int i = 0; i < maxCount; i++)
		{
			if (i < parts1.Length && !string.IsNullOrEmpty(parts1[i]))
			{
				result.Append(parts1[i]);
			}

			if (i < parts2.Length && !string.IsNullOrEmpty(parts2[i]))
			{
				result.Append(parts2[i]);
			}

			if (i < maxCount - 1)
			{
				result.Append(' ');
			}
		}

		return ValueTask.FromResult(new CallState(result.ToString()));
	}

	[SharpFunction(Name = "mid", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "first", "length"])]
	public ValueTask<CallState> Mid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var first = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var length = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(first, out var firstInt)
				|| firstInt < 0
				|| !int.TryParse(length, out var lengthInt))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.PositiveInteger);
		}

		var strLength = str.Length;
		var midLength = lengthInt < 0 ? strLength + lengthInt : lengthInt;

		return ValueTask.FromResult<CallState>(str.Substring(firstInt, midLength));
	}

	[SharpFunction(Name = "ncond", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["expression...|result...", "default"])]
	public ValueTask<CallState> NCond(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateConditional(parser, negate: true, all: false);

	[SharpFunction(Name = "ncondall", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["expression...|result...", "default"])]
	public ValueTask<CallState> NCondAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateConditional(parser, negate: true, all: true);

	[SharpFunction(Name = "ord", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["character"])]
	public ValueTask<CallState> CharacterOrdinance(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return arg0.Length is > 1 or < 0
			? new ValueTask<CallState>(ErrorMessages.Returns.SingleCharArgument)
			: ValueTask.FromResult<CallState>(arg0.EnumerateRunes().First().Value);
	}

	[SharpFunction(Name = "ORDINAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public ValueTask<CallState> Ordinal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var numberArg = parser.CurrentState.Arguments["0"].Message!;

		return !int.TryParse(numberArg.ToPlainText(), out var number)
			? new ValueTask<CallState>(new CallState(ErrorMessages.Returns.Integer))
			: new ValueTask<CallState>(new CallState(number.ToOrdinalWords()));
	}

	[SharpFunction(Name = "pos", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["target", "string"])]
	public ValueTask<CallState> StringPosition(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments["1"].Message!;

		return new ValueTask<CallState>(arg0.IndexOf(arg1.ToPlainText()) + 1);
	}

	[SharpFunction(Name = "repeat", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "count"])]
	public ValueTask<CallState> Repeat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var repeatNumberStr = parser.CurrentState.Arguments["1"].Message!;

		if (!int.TryParse(repeatNumberStr.ToPlainText(), out var repeatNumber))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		var repeat = str.Repeat(repeatNumber);
		return ValueTask.FromResult(new CallState(repeat));
	}

	[SharpFunction(Name = "right", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "length"])]
	public ValueTask<CallState> Right(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var len = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (!int.TryParse(len, out var strlen) || strlen < 0)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.PositiveInteger);
		}

		var startPos = int.Max(0, str.Length - strlen);
		var maxLength = str.Length - startPos;

		return ValueTask.FromResult<CallState>(str.Substring(startPos, maxLength));
	}

	[SharpFunction(Name = "rjust", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["text", "width", "fill", "truncate"])]
	public ValueTask<CallState> RightJustifyString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var fill = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2,
			MarkupText.Space);
		var truncate = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, MarkupText.Plain("")).ToPlainText();

		if (!int.TryParse(width, out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.PositiveInteger);
		}

		var truncType = truncate == "1" ? TruncationType.Truncate : TruncationType.Overflow;
		return ValueTask.FromResult<CallState>(str.Pad(fill, widthInt, PadType.Left, truncType));
	}

	[SharpFunction(Name = "scramble", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> Scramble(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var shuffled = SplitIntoGraphemes(arg0).Shuffle();
		return ValueTask.FromResult<CallState>(MarkupText.Concat(shuffled));
	}

	/// <summary>fun_secure (src/funstr.c): every <see cref="SoftcodeSpecial"/> character becomes a space.</summary>
	[SharpFunction(Name = "secure", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> Secure(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!
			.Apply(text => SoftcodeSpecial().Replace(text, " ")));

	[SharpFunction(Name = "space", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["count"])]
	public ValueTask<CallState> Space(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var repeatNumberStr = parser.CurrentState.Arguments["0"].Message!;

		if (!int.TryParse(repeatNumberStr.ToPlainText(), out var repeatNumber) || repeatNumber < 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		var repeat = MarkupText.Space.Repeat(repeatNumber);
		return ValueTask.FromResult(new CallState(repeat));
	}

	[SharpFunction(Name = "spellnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public ValueTask<CallState> SpellNumber(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var numberString = parser.CurrentState.Arguments["0"].Message!;

		if (!decimal.TryParse(numberString.ToPlainText(), out var repeatNumber))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		var integral = (int)Math.Truncate(repeatNumber);
		var fractional = (int)Math.Truncate((repeatNumber - integral) * (10 ^ repeatNumber.Scale));
		var concat = fractional > 0
			? $"{integral.ToWords()} dot {fractional.ToWords()}"
			: integral.ToWords();

		return ValueTask.FromResult(new CallState(concat));
	}

	[SharpFunction(Name = "squish", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "delimiter"])]
	public ValueTask<CallState> Squish(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1,
			MarkupText.Space);

		var text = arg0.ToPlainText();
		var delimiter = arg1.ToPlainText();
		if (delimiter.Length == 0)
		{
			return ValueTask.FromResult<CallState>(arg0);
		}

		// fun_squish (src/funstr.c): a run of delimiters at either end goes, and every other run of two
		// or more collapses to one. The non-empty pieces of the split are the words; the gap between
		// two of them is a run, and one longer than the delimiter holds more than one copy.
		var words = new List<(int Start, int End)>();
		foreach (var range in text.AsSpan().Split(delimiter))
		{
			var (offset, length) = range.GetOffsetAndLength(text.Length);
			if (length > 0)
			{
				words.Add((offset, offset + length));
			}
		}

		if (words.Count == 0)
		{
			return ValueTask.FromResult<CallState>(text.Length == 0
				? arg0
				: arg0.Splice([new MarkupString.Edit(0, text.Length, MarkupText.Empty)]));
		}

		// Spliced together so the markup around each run is kept.
		var edits = new List<MarkupString.Edit>();
		if (words[0].Start > 0)
		{
			edits.Add(new MarkupString.Edit(0, words[0].Start, MarkupText.Empty));
		}

		edits.AddRange(words.Zip(words.Skip(1))
			.Where(gap => gap.Second.Start - gap.First.End > delimiter.Length)
			.Select(gap => new MarkupString.Edit(gap.First.End, gap.Second.Start - gap.First.End, arg1)));

		if (words[^1].End < text.Length)
		{
			edits.Add(new MarkupString.Edit(words[^1].End, text.Length - words[^1].End, MarkupText.Empty));
		}

		return ValueTask.FromResult<CallState>(edits.Count == 0
			? arg0
			: arg0.Splice(CollectionsMarshal.AsSpan(edits)));
	}

	private static string RemoveDiacritics(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return text;

		return string.Concat(text
				.Normalize(NormalizationForm.FormD)
				.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
			.Normalize(NormalizationForm.FormC);
	}

	[SharpFunction(Name = "stripaccents", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> StripAccents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// We do nothing with arg1 for SharpMUSH.
		var arg0 = parser.CurrentState.Arguments["0"].Message!;

		var func = (Func<string, string>)RemoveDiacritics;
		return ValueTask.FromResult<CallState>(arg0.Apply(func));
	}

	[SharpFunction(Name = "stripansi", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> StripAnsi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!.ToPlainText());

	[SharpFunction(Name = "strlen", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> StringLen(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		// Display cells, not UTF-16 code units: softcode measures a string to lay it out against
		// something else, and a combining mark or a wide character makes those two numbers differ
		// wildly — "Text Editor" under a pile of diacritics is 66 code units and 11 columns.
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!.DisplayWidth);

	[SharpFunction(Name = "strmatch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "pattern"])]
	public ValueTask<CallState> StringMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var pattern = parser.CurrentState.Arguments["1"].Message!;

		var match = MushText.IsWildcardMatch(str, pattern);

		return ValueTask.FromResult(new CallState(match ? "1" : "0"));
	}

	[SharpFunction(Name = "switch", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse, ParameterNames = ["string", "expression...|list...", "default"])]
	public async ValueTask<CallState> Switch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).SkipLast(1).Pairwise();
		var defaultValue = parser.CurrentState.ArgumentsOrdered.Last();

		parser.CurrentState.SwitchStack.Push(arg0!);

		try
		{
			foreach (var (expressionKv, listKv) in args)
			{
				var expression = await expressionKv.Value.ParsedMessage();

				if (MushText.IsWildcardMatch(arg0 ?? MarkupText.Empty, expression ?? MarkupText.Empty))
				{
					return await listKv.Value.ParsedMessage();
				}

				if (!expression!.ToPlainText().StartsWith('>') && !expression.ToPlainText().StartsWith('<'))
				{
					continue;
				}

				var gt = expression.ToPlainText()[0] == '>';

				if (!decimal.TryParse(expression.ToPlainText()[1..], out var decimalExpression)
					|| !decimal.TryParse(arg0!.ToPlainText(), out var arg0AsDecimal))
				{
					continue;
				}

				if (gt
					? decimalExpression > arg0AsDecimal
					: decimalExpression < arg0AsDecimal)
				{
					return await listKv.Value.ParsedMessage();
				}
			}

			return await defaultValue.Value.ParsedMessage();
		}
		finally
		{
			parser.CurrentState.SwitchStack.TryPop(out _);
		}
	}

	[SharpFunction(Name = "switchall", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse, ParameterNames = ["string", "expression...|list...", "default"])]
	public async ValueTask<CallState> SwitchAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).SkipLast(1).Pairwise();
		var defaultValue = parser.CurrentState.ArgumentsOrdered.Last();
		var resultList = new List<MString>();

		parser.CurrentState.SwitchStack.Push(arg0!);

		try
		{
			foreach (var (expressionKv, listKv) in args)
			{
				var expression = await expressionKv.Value.ParsedMessage();

				if (MushText.IsWildcardMatch(arg0 ?? MarkupText.Empty, expression ?? MarkupText.Empty))
				{
					resultList.Add(await listKv.Value.ParsedMessage() ?? MarkupText.Empty);
					continue;
				}

				if (!expression!.ToPlainText().StartsWith('>') && !expression.ToPlainText().StartsWith('<'))
				{
					continue;
				}

				var gt = expression.ToPlainText()[0] == '>';

				if (!decimal.TryParse(expression.ToPlainText()[1..], out var decimalExpression)
					|| !decimal.TryParse(arg0!.ToPlainText(), out var arg0AsDecimal))
				{
					continue;
				}

				if (gt
					? decimalExpression > arg0AsDecimal
					: decimalExpression < arg0AsDecimal)
				{
					resultList.Add(await listKv.Value.ParsedMessage() ?? MarkupText.Empty);
				}
			}

			return resultList.Count != 0
				? MarkupText.Concat(resultList)
				: await defaultValue.Value.ParsedMessage();
		}
		finally
		{
			parser.CurrentState.SwitchStack.TryPop(out _);
		}
	}

	[SharpFunction(Name = "tr", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "from", "to"])]
	public ValueTask<CallState> Tr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var find = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var replace = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		// Expand ranges (e.g., a-z)
		var expandedFind = ExpandRanges(find);
		var expandedReplace = ExpandRanges(replace);

		if (expandedFind.Length != expandedReplace.Length)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.StringLengthsMustBeEqual));
		}

		// Build translation map - later occurrences override earlier ones
		var translationMap = new Dictionary<char, char>();
		for (int i = 0; i < expandedFind.Length; i++)
		{
			translationMap[expandedFind[i]] = expandedReplace[i];
		}

		var result = string.Create(str.Length, (str, translationMap), static (span, state) =>
		{
			var (source, map) = state;
			for (var i = 0; i < span.Length; i++)
			{
				span[i] = map.TryGetValue(source[i], out var replacement) ? replacement : source[i];
			}
		});

		return ValueTask.FromResult(new CallState(result));
	}

	private static string ExpandRanges(string input)
	{
		if (string.IsNullOrEmpty(input)) return input;

		var result = new StringBuilder();
		for (int i = 0; i < input.Length; i++)
		{
			if (i + 2 < input.Length && input[i + 1] == '-')
			{
				char start = input[i];
				char end = input[i + 2];
				for (char c = start; c <= end; c++)
				{
					result.Append(c);
				}

				i += 2; // Skip the '-' and end character
			}
			else
			{
				result.Append(input[i]);
			}
		}

		return result.ToString();
	}

	[SharpFunction(Name = "trim", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "characters", "trim-style"])]
	public ValueTask<CallState> Trim(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var tinyTrim = parser.ServiceProvider.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
			.CurrentValue.Compatibility.TinyTrimFun;
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments.TryGetValue(
			tinyTrim
				? "2"
				: "1", out var arg1Value)
			? arg1Value.Message
			: MarkupText.Space;

		var arg2 = parser.CurrentState.Arguments.TryGetValue(
			tinyTrim
				? "1"
				: "2", out var arg2Value)
			? arg2Value.Message!.ToPlainText()
			: "b";

		var trimType = arg2.ToLowerInvariant() switch
		{
			"l" => TrimType.TrimStart,
			"r" => TrimType.TrimEnd,
			_ => TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			arg0.Trim(trimType, (arg1 ?? MarkupText.Empty).ToPlainText()));
	}

	[SharpFunction(Name = "trimpenn", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> TrimPenn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var arg1Value)
			? arg1Value.Message
			: MarkupText.Space;
		var arg2 = parser.CurrentState.Arguments.TryGetValue("2", out var arg2Value)
			? arg2Value.Message!.ToPlainText()
			: "b";

		var trimType = arg2.ToLowerInvariant() switch
		{
			"l" => TrimType.TrimStart,
			"r" => TrimType.TrimEnd,
			_ => TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			arg0.Trim(trimType, (arg1 ?? MarkupText.Empty).ToPlainText()));
	}

	[SharpFunction(Name = "trimtiny", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> TrimTiny(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments.TryGetValue("2", out var arg1Value)
			? arg1Value.Message
			: MarkupText.Space;
		var arg2 = parser.CurrentState.Arguments.TryGetValue("1", out var arg2Value)
			? arg2Value.Message!.ToPlainText()
			: "b";

		var trimType = arg2.ToLowerInvariant() switch
		{
			"l" => TrimType.TrimStart,
			"r" => TrimType.TrimEnd,
			_ => TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			arg0.Trim(trimType, (arg1 ?? MarkupText.Empty).ToPlainText()));
	}

	[SharpFunction(Name = "ucstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> UpperCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var result = arg0.Apply(x => x.ToUpperInvariant());

		return new ValueTask<CallState>(result);
	}

	[SharpFunction(Name = "urldecode", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> URLDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(new CallState(PercentDecode(parser.CurrentState.Arguments["0"].Message!.ToPlainText())));

	[SharpFunction(Name = "urlencode", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> URLEncode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(new CallState(Uri.EscapeDataString(parser.CurrentState.Arguments["0"].Message!.ToPlainText())));

	/// <summary>
	/// Percent-decodes a string the way PennMUSH's <c>urldecode()</c> does (via libcurl's
	/// <c>curl_easy_unescape</c>): only <c>%XX</c> escapes are decoded — a literal <c>+</c> is
	/// left untouched (unlike form decoding) — and any decoded byte that is not printable ASCII
	/// (0x20–0x7E) is replaced with <c>?</c>, matching Penn's per-byte <c>isprint</c> filter.
	/// </summary>
	private static string PercentDecode(string input)
	{
		var byteCount = Encoding.UTF8.GetByteCount(input);
		Span<byte> src = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
		Encoding.UTF8.GetBytes(input, src);

		// Decoding never grows the text, so it fits a buffer the size of the input. The walk is by
		// index because each byte is read together with the two after it.
		Span<char> decoded = byteCount <= 512 ? stackalloc char[byteCount] : new char[byteCount];
		var written = 0;
		for (var i = 0; i < src.Length; i++)
		{
			var b = src[i];
			if (b == (byte)'%' && i + 2 < src.Length
				&& Uri.IsHexDigit((char)src[i + 1]) && Uri.IsHexDigit((char)src[i + 2]))
			{
				b = (byte)((Uri.FromHex((char)src[i + 1]) << 4) | Uri.FromHex((char)src[i + 2]));
				i += 2;
			}

			decoded[written++] = b is >= 0x20 and <= 0x7E ? (char)b : '?';
		}

		return new string(decoded[..written]);
	}

	[SharpFunction(Name = "wrap", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["string", "width", "first line width", "line separator"])]
	public async ValueTask<CallState> Wrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		var str = args["0"].Message!;
		var width = args["1"].Message!.ToPlainText();
		var firstLineWidth = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Plain(width)).ToPlainText();
		var lineSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.NewLine);

		if (!int.TryParse(width, out var widthInt) || !int.TryParse(firstLineWidth, out var firstLineInt))
		{
			return ErrorMessages.Returns.Integer;
		}

		if (widthInt <= 0 || firstLineInt <= 0)
		{
			return ErrorMessages.Returns.PositiveInteger;
		}

		return MarkupText.Join(lineSeparator, WrapLines(str, widthInt, firstLineInt));
	}

	/// <summary>
	/// Word-wraps <paramref name="text"/> to <paramref name="width"/> display cells, with a first
	/// line of <paramref name="firstLineWidth"/> cells.
	/// </summary>
	/// <remarks>
	/// The two widths are done as two passes rather than one, because the first line is wrapped
	/// against a width the rest of the text never sees. The break between them consumed exactly
	/// one space, which is what the second pass has to step over to pick up where the first
	/// stopped.
	/// </remarks>
	private static MarkupText[] WrapLines(MarkupText text, int width, int firstLineWidth)
	{
		if (firstLineWidth == width) return text.WrapLines(width, WrapMode.Word);

		var head = text.WrapLines(firstLineWidth, WrapMode.Word);
		if (head.Length <= 1) return head;

		var consumed = head[0].Length;
		if (consumed < text.Length && text.Text[consumed] == ' ') consumed++;

		return [head[0], .. text.Substring(consumed).WrapLines(width, WrapMode.Word)];
	}

	[SharpFunction(Name = "strdelete", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "position", "length"])]
	public async ValueTask<CallState> StrDelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var str = parser.CurrentState.Arguments["0"].Message!;
		var first = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var len = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(first, out var index)
				|| !int.TryParse(len, out var length))
		{
			return ErrorMessages.Returns.Integer;
		}

		return str.Remove(index, length);
	}

	[SharpFunction(Name = "DELETE", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "position", "delimiter", "output-separator"])]
	public ValueTask<CallState> Delete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return ListDelete(parser, _2);
	}

	[SharpFunction(Name = "INSERT", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "position", "new-item", "delim"])]
	public ValueTask<CallState> Insert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return ListInsert(parser, _2);
	}

	[SharpFunction(Name = "LCSTR2", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> LCStr2(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return new ValueTask<CallState>(new CallState(str.ToLowerInvariant()));
	}

	[SharpFunction(Name = "UCSTR2", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> UCStr2(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return new ValueTask<CallState>(new CallState(str.ToUpperInvariant()));
	}

	[SharpFunction(Name = "SHA0", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular,
		ParameterNames = ["text"])]
	public ValueTask<CallState> SHA0(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// SHA-0 is deprecated and not supported in modern .NET/OpenSSL
		// Return error message per PennMUSH documentation
		return new ValueTask<CallState>(new CallState(ErrorMessages.Returns.ErrorNotSupported));
	}

	[GeneratedRegex(@"\w+")]
	private static partial Regex GetWord();

	[GeneratedRegex("(?!FJO|[HLMNS]Y.|RY[EO]|SQU|(F[LR]?|[HL]|MN?|N|RH?|S[CHKLMNPTVW]?|X(YL)?)[AEIOU])[FHLMNRSX][A-Z]")]
	private static partial Regex ArticleRegex();

	[GeneratedRegex("^U[NK][AIEO]")]
	private static partial Regex ArticleRegex2();

	[GeneratedRegex("^y(b[lor]|cl[ea]|fere|gg|p[ios]|rou|tt)")]
	private static partial Regex ArticleRegex3();

	[GeneratedRegex(" +")]
	private static partial Regex SpacesRegex();

	[GeneratedRegex("^e[uw]")]
	private static partial Regex ArticleEuwRegex();

	[GeneratedRegex(@"^onc?e\b")]
	private static partial Regex ArticleOnceRegex();

	[GeneratedRegex("^uni([^nmd]|mo)")]
	private static partial Regex ArticleUniRegex();

	[GeneratedRegex("^u[bcfhjkqrst][aeiou]")]
	private static partial Regex ArticleUConsonantRegex();
}