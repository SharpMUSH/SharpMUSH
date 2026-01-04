using System.Drawing;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using ANSILibrary;
using DotNext.Collections.Generic;
using Humanizer;
using Microsoft.FSharp.Core;
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
using SharpMUSH.MarkupString;
using static MarkupString.MarkupImplementation;
using static ANSILibrary.ANSI;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private static readonly Dictionary<(string, string), Regex> SpeechPatternCache = new();

	[SharpFunction(Name = "after", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "substring"])]
	public static ValueTask<CallState> After(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fullString = args["0"].Message;
		var search = args["1"].Message;
		var idx = MModule.indexOf(fullString, search);

		if (idx == -1)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		var result = MModule.substring(idx, MModule.getLength(fullString) - idx, args["0"].Message);

		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "lit", MinArgs = 1, Flags = FunctionFlags.Literal | FunctionFlags.NoParse, ParameterNames = ["argument..."])]
	public static ValueTask<CallState> Lit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(MModule.single(","),
			parser.CurrentState.ArgumentsOrdered.Select(x => x.Value.Message)));
	}

	[SharpFunction(Name = "speak", MinArgs = 2, MaxArgs = 7, Flags = FunctionFlags.Regular, 
		ParameterNames = ["speaker", "string", "say-string", "transform-attr", "isnull-attr", "open", "close"])]
	public static async ValueTask<CallState> Speak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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

		// Determine message type and strip prefix using helper
		var plainSpeak = speakString.ToPlainText();
		var messageType = MessageHelpers.DetermineMessageType(plainSpeak);
		
		// Strip the prefix (including quotes)
		speakString = plainSpeak switch
		{
			[':', .. _]
				or [';', .. _]
				or ['|', .. _]
				or ['"', .. _] => MModule.substring(1, speakString.Length - 1, speakString),
			_ => speakString
		};

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var speakerIsLiteral = speaker.ToPlainText().StartsWith('&');
		var hasTransform = !string.IsNullOrWhiteSpace(transformObjAttr.ToPlainText());
		var hasNull = !string.IsNullOrWhiteSpace(isNullObjAttr.ToPlainText());
		var speakerObject = executor;
		MString speakerName;

		if (!speakerIsLiteral)
		{
			var maybeFound = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
				speaker.ToPlainText(), LocateFlags.All);
			if (maybeFound.IsError)
			{
				return maybeFound.AsError;
			}

			var found = maybeFound.AsSharpObject;

			if (await PermissionService!.Controls(executor, found))
			{
				speakerObject = found;
			}

			speakerName = MModule.single(speakerObject.Object().Name);
		}
		else
		{
			speakerName = MModule.substring(1, speaker.Length - 1, speaker);
		}

		// If not Emit, use Speakername.

		var concat = MModule.single(string.Empty);

		if (messageType is not INotifyService.NotificationType.Emit)
		{
			concat = MModule.concat(concat, speakerName);
		}

		if (messageType is INotifyService.NotificationType.Pose or INotifyService.NotificationType.Say)
		{
			concat = MModule.concat(concat, MModule.single(" "));
		}

		if (messageType is INotifyService.NotificationType.Say)
		{
			concat = MModule.concat(concat, sayString);
			concat = MModule.concat(concat, open);
		}

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
				return new CallState(Errors.ErrorObjectAttributeString);
			}

			var transformationObject = await
				LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
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
				return new CallState(Errors.ErrorObjectAttributeString);
			}

			actualNullAttribute = splitNull.AsT0.Attribute;

			var nullObject = await
				LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
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
				_ => new Regex($"{safeOpen}(?<Content>[^{safeClose}]){safeClose}")
			);

			var contents = pattern.Matches(speakString.ToPlainText());
			var markupContents = contents
				.Select(x => x.Groups["Content"]);

			foreach (var markupContent in markupContents)
			{
				var content = MModule.substring(markupContent.Index, markupContent.Length, speakString);

				if (actualNullAttribute is not null)
				{
					var nullEvaluated = await AttributeService!.EvaluateAttributeFunctionAsync(
						parser, executor, actualNullObject!, actualNullAttribute,
						new Dictionary<string, CallState>
						{
							{ "0", args["0"] },
							{ "1", new CallState(MModule.single(speakerObject.Object().DBRef.ToString())) },
							{ "2", new CallState(content) }
						});

					if (nullEvaluated.Truthy()) continue;
				}

				var evaluated = await AttributeService!.EvaluateAttributeFunctionAsync(
					parser, executor, actualTransformationObject!, actualTransformAttribute ?? string.Empty,
					new Dictionary<string, CallState>
					{
						{ "0", args["0"] },
						{ "1", new CallState(MModule.single(speakerObject.Object().DBRef.ToString())) },
						{ "2", new CallState(content) }
					});

				speakString = MModule.replace(
					speakString,
					evaluated,
					markupContent.Index,
					markupContent.Length);
			}
		}
		else
		{
			concat = MModule.concat(concat, speakString);
		}

		if (messageType is INotifyService.NotificationType.Say)
		{
			concat = MModule.concat(concat, close);
		}

		return new CallState(concat);
	}

	[SharpFunction(Name = "strinsert", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "position", "insert"])]
	public static ValueTask<CallState> StrInsert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var positionStr = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var insert = parser.CurrentState.Arguments["2"].Message!;

		if (!int.TryParse(positionStr, out var position) || position < 0)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorPositiveInteger));
		}

		// If position is greater than length, append
		if (position >= str.Length)
		{
			return ValueTask.FromResult(new CallState(MModule.concat(str, insert)));
		}

		// Insert at position
		var left = MModule.substring(0, position, str);
		var right = MModule.substring(position, str.Length - position, str);
		var result = MModule.concat(MModule.concat(left, insert), right);

		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "strreplace", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["string", "position", "character"])]
	public static ValueTask<CallState> StrReplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var startStr = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var lengthStr = parser.CurrentState.Arguments["2"].Message!.ToPlainText();
		var text = parser.CurrentState.Arguments["3"].Message!;

		if (!int.TryParse(startStr, out var start) || start < 0)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorPositiveInteger));
		}

		if (!int.TryParse(lengthStr, out var length))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorInteger));
		}

		// If start is greater than length, return original string
		if (start >= str.Length)
		{
			return ValueTask.FromResult(new CallState(str));
		}

		// Replace the section
		var result = MModule.replace(str, text, start, length);

		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "strcat", Flags = FunctionFlags.Regular, ParameterNames = ["string..."])]
	public static ValueTask<CallState> Concat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value.Message)
			.Aggregate((x, y) => MModule.concat(x, y)));

	[SharpFunction(Name = "cat", Flags = FunctionFlags.Regular, ParameterNames = ["string..."])]
	public static ValueTask<CallState> Cat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value.Message)
			.Aggregate((x, y) => MModule.concat(x, y, MModule.single(" "))));

	[SharpFunction(Name = "accent", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Accent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var template = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (str.Length != template.Length)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorArgRange));
		}

		var result = new StringBuilder();
		for (int i = 0; i < str.Length; i++)
		{
			var c = str[i];
			var t = template[i];

			var accented = ApplyAccent(c, t);
			result.Append(accented);
		}

		return ValueTask.FromResult(new CallState(result.ToString()));
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

			// No match, return original character
			_ => c
		};
	}

	[SharpFunction(Name = "align", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["widths", "col", "filler", "colsep", "rowsep"])]
	public static async ValueTask<CallState> Align(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		var widths = args["0"].Message!.ToPlainText();

		var widthSpecs = widths.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (widthSpecs.Length == 0)
		{
			return "#-1 INVALID ALIGN STRING";
		}

		var expectedColumnCount = widthSpecs.Length;
		var totalArgs = args.Count - 1; // Exclude the widths argument

		// We need at least expectedColumnCount arguments for the column data
		if (totalArgs < expectedColumnCount)
		{
			return "#-1 NOT ENOUGH COLUMNS FOR ALIGN";
		}

		// We can have at most expectedColumnCount + 3 arguments (columns + filler + colsep + rowsep)
		if (totalArgs > expectedColumnCount + 3)
		{
			return "#-1 TOO MANY COLUMNS FOR ALIGN";
		}

		// Take exactly expectedColumnCount arguments as column data
		var columnArguments = args
			.Skip(1)
			.Take(expectedColumnCount)
			.Select(x => x.Value.Message!);

		// The remaining arguments are filler, colsep, rowsep (in that order)
		var remainder = args
			.Skip(1 + expectedColumnCount)
			.Select(x => x.Value.Message!)
			.ToArray();

		return TextAlignerModule.align(widths,
			columnArguments,
			filler: remainder.Skip(0).FirstOrDefault(MModule.single(" ")),
			columnSeparator: remainder.Skip(1).FirstOrDefault(MModule.single(" ")),
			rowSeparator: remainder.Skip(2).FirstOrDefault(MModule.single("\n")));
	}

	[SharpFunction(Name = "lalign", MinArgs = 2, MaxArgs = 6, Flags = FunctionFlags.Regular, ParameterNames = ["widths", "colList", "delim", "filler", "colsep", "rowsep"])]
	public static async ValueTask<CallState> ListAlign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		var widths = args["0"].Message!.ToPlainText()!;
		var cols = args["1"].Message!;
		var colDelim = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var filler = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single(" "));
		var columnSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, MModule.single(" "));
		var rowSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 5, MModule.single("\n"));

		var widthSpecs = widths.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		
		if (widthSpecs.Length == 0)
		{
			return "#-1 INVALID ALIGN STRING";
		}

		return TextAlignerModule.align(widths, MModule.split2(colDelim, cols), filler, columnSeparator, rowSeparator);
	}

	[SharpFunction(Name = "alphamax", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["word..."])]
	public static ValueTask<CallState> AlphaMax(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.ArgumentsOrdered.Values.Select(x => x.Message!.ToPlainText());
		return ValueTask.FromResult(new CallState(list.Order().First()));
	}

	[SharpFunction(Name = "alphamin", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["word..."])]
	public static ValueTask<CallState> AlphaMin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.ArgumentsOrdered.Values.Select(x => x.Message!.ToPlainText());
		return ValueTask.FromResult(new CallState(list.OrderDescending().First()));
	}

	/// <summary>
	/// Returns the Indefinite Article ('a' or 'an') of a word.
	/// </summary>
	/// <remarks>
	/// Uses the basic implementation found here: https://stackoverflow.com/a/8044744/1894135
	/// This is very specific to English. There are many edge cases that are not covered, and better solutions may exist.
	/// </remarks>
	[SharpFunction(Name = "art", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public static async ValueTask<CallState> Art(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var nounPhrase = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var charList = new[] { 'a', 'e', 'd', 'h', 'i', 'l', 'm', 'n', 'o', 'r', 's', 'x' };
		await ValueTask.CompletedTask;

		var m = GetWord().Match(nounPhrase);

		if (!m.Success)
		{
			return "an";
		}

		var word = m.Groups[0].Value;
		var wordLower = word.ToLower();

		if (new[] { "euler", "heir", "honest", "hono" }.Any(anWord => wordLower.StartsWith(anWord)))
		{
			return "an";
		}

		if (wordLower.StartsWith("hour") && !wordLower.StartsWith("houri"))
		{
			return "an";
		}


		if (wordLower.Length == 1)
		{
			return wordLower.IndexOfAny(charList) == 0
				? "an"
				: "a";
		}

		if (ArticleRegex().IsMatch(word))
		{
			return "an";
		}

		// Todo: Turn into compiled regexs.
		if (new[] { "^e[uw]", "^onc?e\b", "^uni([^nmd]|mo)", "^u[bcfhjkqrst][aeiou]" }
		    .Any(regex => Regex.IsMatch(wordLower, regex)))
		{
			return "a";
		}

		if (ArticleRegex2().IsMatch(word))
		{
			return "a";
		}

		if (word == word.ToUpper())
		{
			return wordLower.IndexOfAny(charList) == 0
				? "an"
				: "a";
		}

		if (wordLower.IndexOfAny(['a', 'e', 'i', 'o', 'u']) == 0)
		{
			return "an";
		}

		return ArticleRegex3().IsMatch(wordLower) ? "an" : "a";
	}

	[SharpFunction(Name = "before", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string1", "string2"])]
	public static ValueTask<CallState> Before(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fullString = args["0"].Message;
		var search = args["1"].Message;
		var idx = MModule.indexOf(fullString, search);

		if (idx == -1)
		{
			return ValueTask.FromResult(new CallState(fullString));
		}

		var result = MModule.substring(0, idx, fullString);

		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "brackets", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public static ValueTask<CallState> Brackets(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var leftSquare = arg0.Count(c => c == '[');
		var rightSquare = arg0.Count(c => c == ']');
		var leftParen = arg0.Count(c => c == '(');
		var rightParen = arg0.Count(c => c == ')');
		var leftCurly = arg0.Count(c => c == '{');
		var rightCurly = arg0.Count(c => c == '}');

		return ValueTask.FromResult(
			new CallState($"{leftSquare} {rightSquare} {leftParen} {rightParen} {leftCurly} {rightCurly}"));
	}

	[SharpFunction(Name = "capstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> CapStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;

		if (arg0.Length < 1)
		{
			return new ValueTask<CallState>(CallState.Empty);
		}

		var leftSide = MModule.substring(0, 1, arg0);
		var rightSide = MModule.substring(1, arg0.Length - 1, arg0);
		var capitalized = MModule.apply(leftSide, FuncConvert.FromFunc<string, string>(x => x.ToUpperInvariant()));
		var concat = MModule.concat(capitalized, rightSide, FSharpOption<MString>.None);

		return new ValueTask<CallState>(new CallState(concat));
	}

	[SharpFunction(Name = "case", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly, 
		ParameterNames = ["expression", "case...|result...", "default"])]
	public static async ValueTask<CallState> Case(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
	public static async ValueTask<CallState> CaseAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();

		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).SkipLast(1).Pairwise();
		var defaultValue = parser.CurrentState.ArgumentsOrdered.Last();
		var list = new List<MString?>();

		foreach (var (expressionKv, listKv) in args)
		{
			var expression = await expressionKv.Value.ParsedMessage();

			if (arg0!.ToPlainText() == expression!.ToPlainText())
			{
				list.Add(await listKv.Value.ParsedMessage());
			}
		}

		return list.Count != 0
			? MModule.multiple(list)
			: await defaultValue.Value.ParsedMessage();
	}

	[SharpFunction(Name = "center", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["text", "width", "fill"])]
	public static ValueTask<CallState> Center(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!;
		var fill = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var rightFill = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, fill);

		if (!int.TryParse(width.ToPlainText(), out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(new CallState(Errors.ErrorPositiveInteger));
		}

		var result = MModule.center2(str, fill, rightFill, widthInt, MModule.TruncationType.Overflow);

		return new ValueTask<CallState>(new CallState(result));
	}

	[SharpFunction(Name = "chr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public static ValueTask<CallState> Char(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;

		if (!int.TryParse(arg0, out var charInt) || charInt < 0)
		{
			return new ValueTask<CallState>(new CallState(Errors.ErrorPositiveInteger));
		}

		try
		{
			return ValueTask.FromResult<CallState>(char.ConvertFromUtf32(charInt));
		}
		catch (ArgumentOutOfRangeException)
		{
			return new ValueTask<CallState>(new CallState(Errors.ErrorArgRange));
		}
	}

	[SharpFunction(Name = "comp", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string1", "string2"])]
	public static ValueTask<CallState> Comp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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

	private static int CompareDbRefs(string value1, string value2)
	{
		// Try to parse as dbrefs (#123 format)
		var dbref1 = ParseDbRef(value1);
		var dbref2 = ParseDbRef(value2);

		if (dbref1.HasValue && dbref2.HasValue)
		{
			return dbref1.Value.CompareTo(dbref2.Value);
		}

		// Fall back to string comparison if not valid dbrefs
		return string.Compare(value1, value2, StringComparison.Ordinal);
	}

	private static int? ParseDbRef(string value)
	{
		if (value.StartsWith("#") && int.TryParse(value[1..], out var dbref))
		{
			return dbref;
		}

		return null;
	}

	[SharpFunction(Name = "cond", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Cond(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var hasDefault = args.Count % 2 == 1;
		var pairCount = hasDefault ? (args.Count - 1) / 2 : args.Count / 2;

		for (int i = 0; i < pairCount; i++)
		{
			var conditionIndex = i * 2;
			var exprIndex = i * 2 + 1;

			var condition = await parser.FunctionParse(args[conditionIndex.ToString()].Message!);
			if (condition != null && Predicates.Truthy(condition.Message!))
			{
				var result = await parser.FunctionParse(args[exprIndex.ToString()].Message!);
				return result ?? CallState.Empty;
			}
		}

		// Return default if present
		if (hasDefault)
		{
			var result = await parser.FunctionParse(args[(args.Count - 1).ToString()].Message!);
			return result ?? CallState.Empty;
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "condall", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> CondAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		
		// Special case: if called with 3 args like condall(list, yes, no)
		// First arg is a space-separated list to check if ALL are truthy
		if (args.Count == 3)
		{
			var listArg = await parser.FunctionParse(args["0"].Message!);
			var elements = listArg!.Message!.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var allTruthy = elements.All(e => 
				!string.IsNullOrEmpty(e) && 
				e != "0" && 
				!e.StartsWith("#-1") &&
				!e.Equals("false", StringComparison.OrdinalIgnoreCase));
			
			var resultArg = allTruthy ? args["1"] : args["2"];
			var result = await parser.FunctionParse(resultArg.Message!);
			return result ?? CallState.Empty;
		}
		
		// Original multi-pair logic for other cases
		var hasDefault = args.Count % 2 == 1;
		var pairCount = hasDefault ? (args.Count - 1) / 2 : args.Count / 2;
		var results = new List<MString?>();

		for (int i = 0; i < pairCount; i++)
		{
			var conditionIndex = i * 2;
			var exprIndex = i * 2 + 1;

			var condition = await parser.FunctionParse(args[conditionIndex.ToString()].Message!);
			if (condition != null && Predicates.Truthy(condition.Message!))
			{
				var expr = await parser.FunctionParse(args[exprIndex.ToString()].Message!);
				if (expr != null)
				{
					results.Add(expr.Message);
				}
			}
		}

		// Return matched results or default
		if (results.Count > 0)
		{
			return MModule.multiple(results);
		}

		if (hasDefault)
		{
			var result = await parser.FunctionParse(args[(args.Count - 1).ToString()].Message!);
			return result ?? CallState.Empty;
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "digest", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["algorithm", "string"])]
	public static async ValueTask<CallState> Digest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!.ToUpperInvariant();
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var result)
			? result.Message!
			: null;

		if (arg1 is null && !arg0.Equals("LIST", StringComparison.InvariantCultureIgnoreCase))
		{
			return Errors.ErrorArgRange;
		}

		if (arg0.Equals("LIST", StringComparison.InvariantCultureIgnoreCase))
		{
			return string.Join(" ", CryptoHelpers.hashAlgorithms.Keys);
		}

		return CryptoHelpers.hashAlgorithms.ContainsKey(arg0)
			? CryptoHelpers.Digest(arg0, arg1!).AsT0
			: Errors.ErrorArgRange;
	}

	[SharpFunction(Name = "edit", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["string", "find", "replace"])]
	public static ValueTask<CallState> Edit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var str = args["0"].Message!.ToPlainText();

		// Process search/replace pairs
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

	[SharpFunction(Name = "escape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> Escape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = MModule.concat(MModule.single("\\"), parser.CurrentState.Arguments["0"].Message!);

		return ValueTask.FromResult<CallState>(MModule.apply(str,
			FSharpFunc<string, string>.FromConverter(x => x switch
			{
				"%" => "\\%",
				";" => "\\;",
				"[" => "\\[",
				"]" => "\\]",
				"{" => "\\{",
				"}" => "\\}",
				"\\" => @"\\",
				"(" => "\\(",
				")" => "\\)",
				"," => "\\,",
				"^" => "\\^",
				"$" => "\\$",
				_ => x
			})));
	}

	[SharpFunction(Name = "flip", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> Flip(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message;
		var split = MModule.split("", arg0);
		return new ValueTask<CallState>(new CallState(MModule.multiple(split.Reverse())));
	}

	[SharpFunction(Name = "foreach", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["varname", "list", "expression", "delim", "outdelim"])]
	public static async ValueTask<CallState> ForEach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		//  foreach([<object>/]<attribute>, <string>[, <start>[, <end>]])

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.ArgumentsOrdered;
		var objAttr = args["0"].Message;
		var str = args["1"].Message!;
		var start = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, "0").ToPlainText();
		var end = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, str.Length.ToString()).ToPlainText();

		if (!int.TryParse(start, out var startInt) || !int.TryParse(end, out var endInt))
		{
			return Errors.ErrorInteger;
		}

		if (startInt < 0 || endInt < 0)
		{
			return Errors.ErrorPositiveInteger;
		}

		// DO Object and Attribute split here.

		endInt = Math.Min(endInt, str.Length);

		var left = MModule.substring(startInt, endInt - startInt, str);
		var right = MModule.substring(endInt, str.Length - endInt, str);
		var remainder = MModule.substring(endInt - startInt, str.Length - endInt + startInt, str);

		// TODO: MModule.apply2 over the remainder to apply the attribute-function to each character.

		return MModule.multiple([left, remainder, right]);
	}

	// Escape angle brackets for HTML safety
	[SharpFunction(Name = "decomposeweb", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> DecomposeWeb(IMUSHCodeParser parser, SharpFunctionAttribute _2) 
		=> ValueTask.FromResult<CallState>(
			MModule.evaluateWith((markupType, innerText) 
				=> markupType switch
				{
					MModule.MarkupTypes.MarkedupText { Item: Ansi ansiMarkup }
						=> ReconstructWebCall(ansiMarkup.Details, WebEncodeAngleBrackets(innerText)),
					_ => WebEncodeAngleBrackets(innerText)
				}, 
				parser.CurrentState.Arguments["0"].Message!));

	[SharpFunction(Name = "decompose", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> Decompose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var input = parser.CurrentState.Arguments["0"].Message!;

		// TODO: ansi() needs to happen after the replacements, of seperately from the replacements.
		var reconstructed = MModule.evaluateWith((markupType, innerText) =>
		{
			return markupType switch
			{
				MModule.MarkupTypes.MarkedupText { Item: Ansi ansiMarkup }
					=> ReconstructAnsiCall(ansiMarkup.Details, innerText),
				_ => innerText
			};
		}, input);

		var result = reconstructed
			.Replace("\\", @"\\")
			.Replace("%", "\\%")
			.Replace(";", "\\;")
			.Replace("[", "\\[")
			.Replace("]", "\\]")
			.Replace("{", "\\{")
			.Replace("}", "\\}")
			.Replace("(", "\\(")
			.Replace(")", "\\)")
			.Replace(",", "\\,")
			.Replace("^", "\\^")
			.Replace("$", "\\$");

		result = SpacesRegex().Replace(result, m => string.Join("", Enumerable.Repeat("%b", m.Length)));

		result = result.Replace("\r", "%r").Replace("\n", "%r").Replace("\t", "%t");

		return ValueTask.FromResult(new CallState(result));
	}

	/// <summary>
	/// Reconstructs an ansi() function call from AnsiStructure and inner text
	/// </summary>
	internal static string ReconstructAnsiCall(AnsiStructure ansiDetails, string innerText)
	{
		var attributes = new List<string>();

		if (ansiDetails.Bold) attributes.Add("h");
		if (ansiDetails.Underlined) attributes.Add("u");
		if (ansiDetails.Blink) attributes.Add("f");
		if (ansiDetails.Inverted) attributes.Add("i");

		if (!ansiDetails.Foreground.Equals(AnsiColor.NoAnsi))
		{
			var colorCode = ConvertAnsiColorToCode(ansiDetails.Foreground);
			if (!string.IsNullOrEmpty(colorCode))
				attributes.Add(colorCode);
		}

		if (!ansiDetails.Background.Equals(AnsiColor.NoAnsi))
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
	private static string WebEncodeAngleBrackets(string text)
	{
		return text.Replace("<", "&lt;").Replace(">", "&gt;");
	}

	/// <summary>
	/// Reconstructs an ansi() function call from AnsiStructure and inner text
	/// </summary>
	private static string ReconstructWebCall(AnsiStructure ansiDetails, string innerText)
	{
		Color foregroundColor = Color.Empty;
		Color backgroundColor = Color.Empty;
		
		if (!ansiDetails.Foreground.Equals(AnsiColor.NoAnsi))
		{
			foregroundColor = ConvertAnsiColorToRGB(ansiDetails.Foreground);
		}

		if (!ansiDetails.Background.Equals(AnsiColor.NoAnsi))
		{
			backgroundColor = ConvertAnsiColorToRGB(ansiDetails.Background);
		}

		return
			$"<span style=\"color:{(
				foregroundColor != Color.Empty 
					? ColorTranslator.ToHtml(foregroundColor) 
					: "inherit")
			};background-color:{
				(backgroundColor != Color.Empty 
					? ColorTranslator.ToHtml(backgroundColor) 
					: "inherit")
			};text-decoration:{
				(ansiDetails.Underlined
				? "underline"
				: "inherit")
			}\">{innerText}</span>";
	}

	/// <summary>
	/// Converts AnsiColor to PennMUSH color code
	/// </summary>
	internal static string ConvertAnsiColorToCode(ANSI.AnsiColor color, bool isBackground = false)
	{
		return color switch
		{
			ANSI.AnsiColor.RGB rgb => isBackground 
				? $"/{rgb.Item.R:X2}{rgb.Item.G:X2}{rgb.Item.B:X2}"
				: $"{rgb.Item.R:X2}{rgb.Item.G:X2}{rgb.Item.B:X2}",
			AnsiColor.ANSI ansi
				=> ansi.Item switch
				{
					// Background colors use uppercase, foreground uses lowercase
					[0, 30] or [0, 40] => isBackground ? "X" : "x", // black
					[0, 31] or [0, 41] => isBackground ? "R" : "r", // red
					[0, 32] or [0, 42] => isBackground ? "G" : "g", // green
					[0, 33] or [0, 43] => isBackground ? "Y" : "y", // yellow
					[0, 34] or [0, 44] => isBackground ? "B" : "b", // blue
					[0, 35] or [0, 45] => isBackground ? "M" : "m", // magenta
					[0, 36] or [0, 46] => isBackground ? "C" : "c", // cyan
					[0, 37] or [0, 47] => isBackground ? "W" : "w", // white
					[1, 30] or [1, 40] => isBackground ? "hX" : "hx", // bright black
					[1, 31] or [1, 41] => isBackground ? "hR" : "hr", // bright red
					[1, 32] or [1, 42] => isBackground ? "hG" : "hg", // bright green
					[1, 33] or [1, 43] => isBackground ? "hY" : "hy", // bright yellow
					[1, 34] or [1, 44] => isBackground ? "hB" : "hb", // bright blue
					[1, 35] or [1, 45] => isBackground ? "hM" : "hm", // bright magenta
					[1, 36] or [1, 46] => isBackground ? "hC" : "hc", // bright cyan
					[1, 37] or [1, 47] => isBackground ? "hW" : "hw", // bright white
					[.., 90] or [.., 100] => isBackground ? "hX" : "hx", // bright black
					[.., 91] or [.., 101] => isBackground ? "hR" : "hr", // bright red
					[.., 92] or [.., 102] => isBackground ? "hG" : "hg", // bright green
					[.., 93] or [.., 103] => isBackground ? "hY" : "hy", // bright yellow
					[.., 94] or [.., 104] => isBackground ? "hB" : "hb", // bright blue
					[.., 95] or [.., 105] => isBackground ? "hM" : "hm", // bright magenta
					[.., 96] or [.., 106] => isBackground ? "hC" : "hc", // bright cyan
					[.., 97] or [.., 107] => isBackground ? "hW" : "hw", // bright white
					_ => ""
				},
			_ => ""
		};
	}

	/// <summary>
	/// Converts AnsiColor to PennMUSH color code
	/// </summary>
	private static Color ConvertAnsiColorToRGB(ANSI.AnsiColor color)
	{
		return color switch
		{
			ANSI.AnsiColor.RGB rgb => rgb.Item,
			AnsiColor.ANSI ansi
				=> ansi.Item switch
				{
					[0, 30] => Color.Black, // black
					[0, 31] => Color.DarkRed, // red  
					[0, 32] => Color.DarkGreen, // green
					[0, 33] => Color.DarkGoldenrod, // yellow
					[0, 34] => Color.DarkBlue, // blue
					[0, 35] => Color.DarkMagenta, // magenta
					[0, 36] => Color.DarkCyan, // cyan
					[0, 37] => Color.Gray, // white
					[1, 30] => Color.LightGray, // bright black
					[1, 31] => Color.Red, // bright red  
					[1, 32] => Color.Green, // bright green
					[1, 33] => Color.Yellow, // bright yellow
					[1, 34] => Color.Blue, // bright blue
					[1, 35] => Color.Magenta, // bright magenta
					[1, 36] => Color.Cyan, // bright cyan
					[1, 37] => Color.White, // bright white
					[.., 90] => Color.LightGray, // bright black
					[.., 91] => Color.Red, // bright red
					[.., 92] => Color.Green, // bright green
					[.., 93] => Color.Yellow, // bright yellow
					[.., 94] => Color.Blue, // bright blue
					[.., 95] => Color.Magenta, // bright magenta
					[.., 96] => Color.Cyan, // bright cyan
					[.., 97] => Color.White, // bright white
					_ => Color.Empty
				},
			_ => Color.Empty
		};
	}

	[SharpFunction(Name = "formdecode", MinArgs = 1, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public static ValueTask<CallState> FormDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var arg1 = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "").ToPlainText()!;
		var arg2 = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ").ToPlainText()!;

		return ValueTask.FromResult<CallState>((arg0, arg1, arg2) switch
		{
			(var str, "", var outSep)
				=> string.Join(outSep, HttpUtility.ParseQueryString(str)),
			var (str, field, outSep)
				=> string.Join(outSep, HttpUtility.ParseQueryString(str).GetValues(field) ?? []),
		});
	}

	[SharpFunction(Name = "hmac", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["algorithm", "key", "string"])]
	public static ValueTask<CallState> HashMessageAuthenticationCode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var digest = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!.ToUpperInvariant();
		var key = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;
		var text = parser.CurrentState.Arguments["2"].Message!.ToPlainText()!;
		var encoding = parser.CurrentState.Arguments.TryGetValue("3", out var encodingArg)
			? encodingArg.Message!.ToPlainText()!.ToLowerInvariant()
			: "base16";

		// Get the appropriate HMAC algorithm
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
			return ValueTask.FromResult(new CallState(Errors.ErrorArgRange));
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
	public static async ValueTask<CallState> If(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var truthy = Predicates.Truthy(parsedIfElse!);
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
	public static async ValueTask<CallState> IfElse(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var ifCase = parser.CurrentState.Arguments["1"].Message!;
		var elseCase = parser.CurrentState.Arguments["2"].Message!;
		var truthy = Predicates.Truthy(parsedIfElse!);
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
	public static ValueTask<CallState> LowerCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return new ValueTask<CallState>(
			MModule.apply(
				parser.CurrentState.Arguments["0"].Message!,
				transform: FuncConvert.FromFunc<string, string>(x => x.ToLowerInvariant())));
	}

	[SharpFunction(Name = "left", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "length"])]
	public static ValueTask<CallState> Left(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var len = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		return !int.TryParse(len, out var strlen) || strlen < 0
			? ValueTask.FromResult<CallState>(Errors.ErrorPositiveInteger)
			: ValueTask.FromResult<CallState>(MModule.substring(0, int.Min(strlen, str.Length), str));
	}

	[SharpFunction(Name = "ljust", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["text", "width", "fill"])]
	public static ValueTask<CallState> LeftJustifyString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;
		var fill = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2,
			MModule.single(" "));

		if (!int.TryParse(width, out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(Errors.ErrorPositiveInteger);
		}

		return ValueTask.FromResult<CallState>(MModule.pad(str, fill, widthInt, MModule.PadType.Right,
			MModule.TruncationType.Overflow));
	}

	[SharpFunction(Name = "lpos", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["target", "list", "delimiter"])]
	public static ValueTask<CallState> ListPositions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			string.Join(" ",
				MModule.indexesOf(
						parser.CurrentState.Arguments["0"].Message!,
						parser.CurrentState.Arguments["1"].Message!)
					.Select(x => x.ToString())));

	[SharpFunction(Name = "merge", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public static ValueTask<CallState> Merge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var string1 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var string2 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var separator = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		// Split by separator
		var parts1 = string.IsNullOrEmpty(separator)
			? new[] { string1 }
			: string1.Split(new[] { separator }, StringSplitOptions.None);
		var parts2 = string.IsNullOrEmpty(separator)
			? new[] { string2 }
			: string2.Split(new[] { separator }, StringSplitOptions.None);

		// Merge alternating parts
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
	public static ValueTask<CallState> Mid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var first = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;
		var length = parser.CurrentState.Arguments["2"].Message!.ToPlainText()!;

		if (!int.TryParse(first, out var firstInt)
		    || firstInt < 0
		    || !int.TryParse(length, out var lengthInt))
		{
			return new ValueTask<CallState>(Errors.ErrorPositiveInteger);
		}

		var strLength = str.Length;
		var midLength = lengthInt < 0 ? strLength + lengthInt : lengthInt;

		return ValueTask.FromResult<CallState>(MModule.substring(firstInt, midLength, str));
	}

	[SharpFunction(Name = "ncond", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> NCond(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var hasDefault = args.Count % 2 == 1;

		// Process pairs: (condition, expression), (condition, expression), ...
		var pairCount = hasDefault ? (args.Count - 1) / 2 : args.Count / 2;
		
		for (int i = 0; i < pairCount; i++)
		{
			var conditionIndex = i * 2;
			var exprIndex = i * 2 + 1;
			
			var condition = await parser.FunctionParse(args[conditionIndex.ToString()].Message!);
			// Return expression when condition is TRUTHY
			if (condition != null && Predicates.Truthy(condition.Message!))
			{
				var result = await parser.FunctionParse(args[exprIndex.ToString()].Message!);
				return result ?? CallState.Empty;
			}
		}

		// Return default if present
		if (hasDefault)
		{
			var result = await parser.FunctionParse(args[(args.Count - 1).ToString()].Message!);
			return result ?? CallState.Empty;
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "ncondall", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> NCondAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var hasDefault = args.Count % 2 == 1;
		var results = new List<MString?>();

		// Process pairs: (condition, expression), (condition, expression), ...
		var pairCount = hasDefault ? (args.Count - 1) / 2 : args.Count / 2;
		
		for (int i = 0; i < pairCount; i++)
		{
			var conditionIndex = i * 2;
			var exprIndex = i * 2 + 1;
			
			var condition = await parser.FunctionParse(args[conditionIndex.ToString()].Message!);
			// Check if ALL elements in the condition (space-separated list) are truthy
			if (condition != null)
			{
				var conditionText = condition.Message!.ToPlainText();
				var elements = conditionText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				var allTruthy = elements.All(e => 
					!string.IsNullOrEmpty(e) && 
					e != "0" && 
					!e.StartsWith("#-1") &&
					!e.Equals("false", StringComparison.OrdinalIgnoreCase));
				
				if (allTruthy)
				{
					var expr = await parser.FunctionParse(args[exprIndex.ToString()].Message!);
					if (expr != null)
					{
						results.Add(expr.Message);
					}
				}
			}
		}

		// Return matched results or default
		if (results.Count > 0)
		{
			return MModule.multiple(results);
		}

		if (hasDefault)
		{
			var result = await parser.FunctionParse(args[(args.Count - 1).ToString()].Message!);
			return result ?? CallState.Empty;
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "ord", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["character"])]
	public static ValueTask<CallState> CharacterOrdinance(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		return arg0.Length is > 1 or < 0
			? new ValueTask<CallState>("#-1 ARGUMENT MUST BE A SINGLE CHARACTER")
			: ValueTask.FromResult<CallState>(arg0.EnumerateRunes().First().Value);
	}

	[SharpFunction(Name = "ORDINAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public static ValueTask<CallState> Ordinal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var numberArg = parser.CurrentState.Arguments["0"].Message!;

		return !int.TryParse(numberArg.ToPlainText(), out var number)
			? new ValueTask<CallState>(new CallState(Errors.ErrorInteger))
			: new ValueTask<CallState>(new CallState(number.ToOrdinalWords()));
	}

	[SharpFunction(Name = "pos", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["target", "string"])]
	public static ValueTask<CallState> StringPosition(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments["1"].Message!;

		return new ValueTask<CallState>(MModule.indexOf(arg0, arg1) + 1);
	}

	[SharpFunction(Name = "repeat", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "count"])]
	public static ValueTask<CallState> Repeat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var repeatNumberStr = parser.CurrentState.Arguments["1"].Message!;

		if (!int.TryParse(repeatNumberStr.ToPlainText(), out var repeatNumber))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorInteger));
		}

		var repeat = MModule.repeat(str, repeatNumber, MModule.empty())!;
		return ValueTask.FromResult(new CallState(repeat));
	}

	[SharpFunction(Name = "right", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "length"])]
	public static ValueTask<CallState> Right(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var len = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		if (!int.TryParse(len, out var strlen) || strlen < 0)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorPositiveInteger);
		}

		var startPos = int.Max(0, str.Length - strlen);
		var maxLength = str.Length - startPos;

		return ValueTask.FromResult<CallState>(MModule.substring(startPos, maxLength, str));
	}

	[SharpFunction(Name = "rjust", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["text", "width", "fill"])]
	public static ValueTask<CallState> RightJustifyString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;
		var fill = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2,
			MModule.single(" "));

		if (!int.TryParse(width, out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(Errors.ErrorPositiveInteger);
		}

		return ValueTask.FromResult<CallState>(MModule.pad(str, fill, widthInt, MModule.PadType.Left,
			MModule.TruncationType.Overflow));
	}

	[SharpFunction(Name = "scramble", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> Scramble(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var split = MModule.split("", arg0).Shuffle();
		return ValueTask.FromResult<CallState>(string.Join("", split));
	}

	[SharpFunction(Name = "secure", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> Secure(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(MModule.apply(parser.CurrentState.Arguments["0"].Message!,
			FSharpFunc<string, string>.FromConverter(x => x switch
			{
				"%" or ";" or "[" or "]" or "(" or ")" or "{" or "}" or "$" or "," or "^" => " ",
				_ => x
			})));

	[SharpFunction(Name = "space", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["count"])]
	public static ValueTask<CallState> Space(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var repeatNumberStr = parser.CurrentState.Arguments["0"].Message!;

		if (!int.TryParse(repeatNumberStr.ToPlainText(), out var repeatNumber) || repeatNumber < 0)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorPositiveInteger));
		}

		var repeat = MModule.repeat(MModule.single(" "), repeatNumber, MModule.empty())!;
		return ValueTask.FromResult(new CallState(repeat));
	}

	[SharpFunction(Name = "spellnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public static ValueTask<CallState> SpellNumber(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var numberString = parser.CurrentState.Arguments["0"].Message!;

		if (!decimal.TryParse(numberString.ToPlainText(), out var repeatNumber))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorInteger));
		}

		var integral = (int)Math.Truncate(repeatNumber);
		var fractional = (int)Math.Truncate((repeatNumber - integral) * (10 ^ repeatNumber.Scale));
		var concat = fractional > 0
			? $"{integral.ToWords()} dot {fractional.ToWords()}"
			: integral.ToWords();

		return ValueTask.FromResult(new CallState(concat));
	}

	[SharpFunction(Name = "squish", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "delimiter"])]
	public static ValueTask<CallState> Squish(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1,
			MModule.single(" "));

		var arg0Plain = arg0.ToPlainText()!;

		// Not an exact match. PennMUSH is conscious of the ANSI to look for.
		// Also, this technically acts more like a replace than a true squish.
		var regex = new Regex($"{Regex.Escape(arg1.ToPlainText())}+");

		return ValueTask.FromResult<CallState>(regex.Matches(arg0Plain)
			.Reverse()
			.Aggregate(arg0, (current, match) => MModule.replace(current, arg1, match.Index, match.Length)));
	}

	private static string RemoveDiacritics(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return text;

		return new string(text
				.Normalize(NormalizationForm.FormD)
				.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
				.ToArray()
				.AsSpan())
			.Normalize(NormalizationForm.FormC);
	}

	[SharpFunction(Name = "stripaccents", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> StripAccents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// We do nothing with arg1 for SharpMUSH.
		var arg0 = parser.CurrentState.Arguments["0"].Message!;

		var func = FuncConvert.FromFunc<string, string>(RemoveDiacritics);
		return ValueTask.FromResult<CallState>(MModule.apply(arg0, func));
	}

	[SharpFunction(Name = "stripansi", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public static ValueTask<CallState> StripAnsi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!.ToPlainText());

	[SharpFunction(Name = "strlen", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> StringLen(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!.Length);

	[SharpFunction(Name = "strmatch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "pattern"])]
	public static ValueTask<CallState> StringMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var pattern = parser.CurrentState.Arguments["1"].Message!;

		var match = MModule.isWildcardMatch(str, pattern);

		return ValueTask.FromResult(new CallState(match ? "1" : "0"));
	}

	[SharpFunction(Name = "switch", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Switch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).SkipLast(1).Pairwise();
		var defaultValue = parser.CurrentState.ArgumentsOrdered.Last();

		foreach (var (expressionKv, listKv) in args)
		{
			var expression = await expressionKv.Value.ParsedMessage();

			if (MModule.isWildcardMatch(arg0, expression))
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

	[SharpFunction(Name = "switchall", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> SwitchAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).SkipLast(1).Pairwise();
		var defaultValue = parser.CurrentState.ArgumentsOrdered.Last();
		var resultList = new List<MString?>();

		foreach (var (expressionKv, listKv) in args)
		{
			var expression = await expressionKv.Value.ParsedMessage();

			if (MModule.isWildcardMatch(arg0, expression))
			{
				resultList.Add(await listKv.Value.ParsedMessage());
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
				resultList.Add(await listKv.Value.ParsedMessage());
			}
		}

		return resultList.Count != 0
			? MModule.multiple(resultList)
			: await defaultValue.Value.ParsedMessage();
	}

	[SharpFunction(Name = "tr", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "from", "to"])]
	public static ValueTask<CallState> Tr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var find = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var replace = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		// Expand ranges (e.g., a-z)
		var expandedFind = ExpandRanges(find);
		var expandedReplace = ExpandRanges(replace);

		if (expandedFind.Length != expandedReplace.Length)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorArgRange));
		}

		// Build translation map - later occurrences override earlier ones
		var translationMap = new Dictionary<char, char>();
		for (int i = 0; i < expandedFind.Length; i++)
		{
			translationMap[expandedFind[i]] = expandedReplace[i];
		}

		// Apply translation character by character
		var result = new StringBuilder(str.Length);
		foreach (var c in str)
		{
			if (translationMap.TryGetValue(c, out var replacement))
			{
				result.Append(replacement);
			}
			else
			{
				result.Append(c);
			}
		}

		return ValueTask.FromResult(new CallState(result.ToString()));
	}

	private static string ExpandRanges(string input)
	{
		if (string.IsNullOrEmpty(input)) return input;

		var result = new StringBuilder();
		for (int i = 0; i < input.Length; i++)
		{
			if (i + 2 < input.Length && input[i + 1] == '-')
			{
				// Range found
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
	public static ValueTask<CallState> Trim(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments.TryGetValue(
			Configuration!.CurrentValue.Compatibility.TinyTrimFun
				? "1"
				: "2", out var arg1Value)
			? arg1Value.Message
			: MModule.single(" ");

		var arg2 = parser.CurrentState.Arguments.TryGetValue(
			Configuration.CurrentValue.Compatibility.TinyTrimFun
				? "2"
				: "1", out var arg2Value)
			? arg2Value.Message!.ToPlainText()
			: "b";

		var trimType = arg2 switch
		{
			"l" => MModule.TrimType.TrimStart,
			"r" => MModule.TrimType.TrimEnd,
			_ => MModule.TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			MModule.trim(arg0, arg1, trimType));
	}

	[SharpFunction(Name = "trimpenn", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> TrimPenn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var arg1Value)
			? arg1Value.Message
			: MModule.single(" ");
		;
		var arg2 = parser.CurrentState.Arguments.TryGetValue("2", out var arg2Value)
			? arg2Value.Message!.ToPlainText()
			: "b";

		var trimType = arg2 switch
		{
			"l" => MModule.TrimType.TrimStart,
			"r" => MModule.TrimType.TrimEnd,
			_ => MModule.TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			MModule.trim(arg0, arg1, trimType));
	}

	[SharpFunction(Name = "trimtiny", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> TrimTiny(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments.TryGetValue("2", out var arg1Value)
			? arg1Value.Message
			: MModule.single(" ");
		;
		var arg2 = parser.CurrentState.Arguments.TryGetValue("1", out var arg2Value)
			? arg2Value.Message!.ToPlainText()
			: "b";

		var trimType = arg2 switch
		{
			"l" => MModule.TrimType.TrimStart,
			"r" => MModule.TrimType.TrimEnd,
			_ => MModule.TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			MModule.trim(arg0, arg1, trimType));
	}

	[SharpFunction(Name = "ucstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> UpperCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var result = MModule.apply(arg0, FuncConvert.FromFunc<string, string>(x => x.ToUpperInvariant()));

		return new ValueTask<CallState>(result);
	}

	[SharpFunction(Name = "urldecode", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public static ValueTask<CallState> URLDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(new CallState(WebUtility.HtmlDecode(parser.CurrentState.Arguments["0"].Message!.ToPlainText())));

	[SharpFunction(Name = "urlencode", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public static ValueTask<CallState> URLEncode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(new CallState(WebUtility.HtmlEncode(parser.CurrentState.Arguments["0"].Message!.ToPlainText())));

	[SharpFunction(Name = "wrap", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["string", "width", "osep", "isep", "indent"])]
	public static async ValueTask<CallState> Wrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var firstLineWidth = parser.CurrentState.Arguments.TryGetValue("2", out var arg2Value)
			? arg2Value.Message!.ToPlainText()
			: width;
		var lineSeparator = parser.CurrentState.Arguments.TryGetValue("3", out var arg3Value)
			? arg3Value.Message!.ToPlainText()
			: "\n";

		var strlen = str.Length;

		if (!int.TryParse(width, out var widthInt)
		    || !int.TryParse(firstLineWidth, out var firstLineInt))
		{
			return Errors.ErrorInteger;
		}

		var firstLine = MModule.substring(0, firstLineInt, str)!;

		var remainingLength = strlen - firstLine.Length;
		if (remainingLength <= 0)
		{
			return firstLine;
		}

		var list = Enumerable
			.Range(1, remainingLength / widthInt + 2)
			.Select(line => MModule.substring(line * widthInt, widthInt, str)!)
			.Prepend(firstLine);

		return string.Join(lineSeparator, list);
	}

	[SharpFunction(Name = "strdelete", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "position", "length"])]
	public static async ValueTask<CallState> StrDelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var str = parser.CurrentState.Arguments["0"].Message!;
		var first = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var len = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(first, out var index)
		    || !int.TryParse(len, out var length))
		{
			return Errors.ErrorInteger;
		}

		return MModule.remove(str, index, length);
	}

	[GeneratedRegex(@"\w+")]
	private static partial Regex GetWord();

	[GeneratedRegex("(?!FJO|[HLMNS]Y.|RY[EO]|SQU|(F[LR]?|[HL]|MN?|N|RH?|S[CHKLMNPTVW]?|X(YL)?)[AEIOU])[FHLMNRSX][A-Z]")]
	private static partial Regex ArticleRegex();

	[GeneratedRegex("^U[NK][AIEO]")]
	private static partial Regex ArticleRegex2();

	[GeneratedRegex("^y(b[lor]|cl[ea]|fere|gg|p[ios]|rou|tt)")]
	private static partial Regex ArticleRegex3();

	[GeneratedRegex("\\s+")]
	private static partial Regex SpacesRegex();
}