using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DotNext.Collections.Generic;
using Humanizer;
using MarkupString;
using Microsoft.FSharp.Collections;
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

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private static readonly Dictionary<(string, string), Regex> SpeechPatternCache = new();

	[SharpFunction(Name = "after", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "lit", MinArgs = 1, Flags = FunctionFlags.Literal | FunctionFlags.NoParse)]
	public static ValueTask<CallState> Lit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(MModule.single(","),
			parser.CurrentState.ArgumentsOrdered.Select(x => x.Value.Message)));
	}

	[SharpFunction(Name = "SPEAK", MinArgs = 2, MaxArgs = 7, Flags = FunctionFlags.Regular)]
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

		// TODO: This behavior gets re-used, so best to create a HelperFunction for this.

		var messageType = speakString.ToPlainText() switch
		{
			[':', .. _] => INotifyService.NotificationType.Pose,
			[';', .. _] => INotifyService.NotificationType.SemiPose,
			['|', .. _] => INotifyService.NotificationType.Emit,
			_ => INotifyService.NotificationType.Say
		};

		speakString = speakString.ToPlainText() switch
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

	[SharpFunction(Name = "STRINSERT", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StrInsert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "STRREPLACE", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StrReplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "strcat", Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Concat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value.Message)
			.Aggregate((x, y) => MModule.concat(x, y)));

	[SharpFunction(Name = "cat", Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Cat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value.Message)
			.Aggregate((x, y) => MModule.concat(x, y, MModule.single(" "))));

	[SharpFunction(Name = "ACCENT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Accent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "align", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Align(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		var actualColumnArgCount = args.Count - 1;
		var widths = args["0"].Message!.ToPlainText();

		var expectedColumnCount = widths.Split(' ').Length;
		var minRequiredColumnCount = actualColumnArgCount - 3;

		switch (expectedColumnCount)
		{
			case 0:
				return "#-1 INVALID ALIGN STRING";
			case var _ when expectedColumnCount > actualColumnArgCount:
				return "#-1 NOT ENOUGH COLUMNS FOR ALIGN";
			case var _ when expectedColumnCount < minRequiredColumnCount:
				return "#-1 TOO MANY COLUMNS FOR ALIGN";
		}

		var columnArguments = args
			.Skip(1)
			.SkipLast(expectedColumnCount - actualColumnArgCount)
			.Select(x => x.Value.Message!);

		var remainder = args
			.Skip(1 + expectedColumnCount).Select(x => x.Value.Message!)
			.ToArray();

		return TextAligner.align(widths,
			columnArguments,
			filler: remainder.Skip(0).FirstOrDefault(MModule.single(" ")),
			columnSeparator: remainder.Skip(1).FirstOrDefault(MModule.single(" ")),
			rowSeparator: remainder.Skip(2).FirstOrDefault(MModule.single("\n")));
	}

	[SharpFunction(Name = "lalign", MinArgs = 2, MaxArgs = 6, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ListAlign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		var widths = args["0"].Message!.ToPlainText()!;
		var cols = args["1"].Message!;
		var colDelim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(" "));
		var filler = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var columnSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single(" "));
		var rowSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, MModule.single("\n"));

		var actualColumnArgCount = args.Count - 1;
		var expectedColumnCount = widths.Split(' ').Length;
		var minRequiredColumnCount = actualColumnArgCount - 3;

		return expectedColumnCount switch
		{
			0 => "#-1 INVALID ALIGN STRING",
			_ when expectedColumnCount > actualColumnArgCount => "#-1 NOT ENOUGH COLUMNS FOR ALIGN",
			_ when expectedColumnCount < minRequiredColumnCount => "#-1 TOO MANY COLUMNS FOR ALIGN",
			_ => TextAligner.align(widths, MModule.split2(colDelim, cols), filler, columnSeparator, rowSeparator)
		};
	}

	[SharpFunction(Name = "ALPHAMAX", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> AlphaMax(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.ArgumentsOrdered.Values.Select(x => x.Message!.ToPlainText());
		return ValueTask.FromResult(new CallState(list.Order().First()));
	}

	[SharpFunction(Name = "ALPHAMIN", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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
	[SharpFunction(Name = "ART", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "BEFORE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "BRACKETS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Brackets(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CAPSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "CASE", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
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

	[SharpFunction(Name = "CASEALL", MinArgs = 3, MaxArgs = int.MaxValue,
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

	[SharpFunction(Name = "CENTER", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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

		var result = MModule.center2(str, fill, rightFill, widthInt, MarkupStringModule.TruncationType.Overflow);

		return new ValueTask<CallState>(new CallState(result));
	}

	[SharpFunction(Name = "chr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "COMP", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Comp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "COND", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Cond(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CONDALL", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> CondAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "digest", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Digest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!.ToUpperInvariant();
		var arg1 = parser.CurrentState.Arguments.TryGetValue("1", out var result)
			? result.Message!
			: null;

		if (arg1 == null && !arg0.Equals("LIST", StringComparison.InvariantCultureIgnoreCase))
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

	[SharpFunction(Name = "edit", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Edit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "escape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "flip", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Flip(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message;
		var split = MModule.split("", arg0);
		return new ValueTask<CallState>(new CallState(MModule.multiple(split.Reverse())));
	}

	[SharpFunction(Name = "foreach", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ForEach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		//  foreach([<object>/]<attribute>, <string>[, <start>[, <end>]])

		var args = parser.CurrentState.ArgumentsOrdered;
		var objAttr = args["0"].Message;
		var str = args["1"].Message!;
		var start = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, "0").ToPlainText();
		var end = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, str.Length.ToString()).ToPlainText();

		if (!int.TryParse(start, out var startInt) || !int.TryParse(end, out var endInt))
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorInteger);
		}
		
		if(startInt < 0 || endInt < 0)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorPositiveInteger);
		}

		endInt = Math.Min(endInt, str.Length);
		
		var left = MModule.substring(startInt, endInt - startInt, str);
		var right = MModule.substring(endInt, str.Length - endInt, str);
		var remainder = MModule.substring(endInt - startInt, str.Length - endInt + startInt, str);

		// TODO: MModule.apply2 over the remainder to apply the function to each character.
		// Will have to create a new apply function for this.
		
		return ValueTask.FromResult<CallState>(MModule.multiple([left, remainder, right]));
	}


	[SharpFunction(Name = "decompose", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Decompose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FORMDECODE", MinArgs = 1, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> FormDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "HMAC", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> HashMessageAuthenticationCode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		/*
		  Computes the HMAC (message authentication code) hash for <text> using the passphrase <key> and the given hash function <digest>, which can be any supported by digest(). <encoding> can be base16 (The default) or base64.

		  Example:
		  > think hmac(sha256, secret, this is some text)
		  9598fd959633f2a64a7d7e985966774aa6f334bc802e5b3301772ec8ed6eed5a
		  > think hmac(sha256, secret, this is some text, base64)
		  lZj9lZYz8qZKfX6YWWZ3SqbzNLyALlszAXcuyO1u7Vo=
  */
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "if", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
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

	[SharpFunction(Name = "ifelse", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
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

	[SharpFunction(Name = "lcstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> LowerCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return new ValueTask<CallState>(
			MModule.apply(
				parser.CurrentState.Arguments["0"].Message!,
				transform: FuncConvert.FromFunc<string, string>(x => x.ToLowerInvariant())));
	}

	[SharpFunction(Name = "left", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Left(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var len = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		return !int.TryParse(len, out var strlen) || strlen < 0
			? ValueTask.FromResult<CallState>(Errors.ErrorPositiveInteger)
			: ValueTask.FromResult<CallState>(MModule.substring(0, int.Min(strlen, str.Length), str));
	}

	[SharpFunction(Name = "ljust", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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

		return ValueTask.FromResult<CallState>(MModule.pad(str, fill, widthInt, MarkupStringModule.PadType.Right,
			MarkupStringModule.TruncationType.Overflow));
	}

	[SharpFunction(Name = "LPOS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LeftPosition(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MERGE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Merge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "mid", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
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
		var midlen = lengthInt < 0 ? strLength + lengthInt : lengthInt;

		return ValueTask.FromResult<CallState>(MModule.substring(firstInt, midlen, str));
	}

	[SharpFunction(Name = "NCOND", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> NCond(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NCONDALL", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> NCondAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ORD", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Ord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ORDINAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Ordinal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var numberArg = parser.CurrentState.Arguments["0"].Message!;

		return !int.TryParse(numberArg.ToPlainText(), out var number)
			? new ValueTask<CallState>(new CallState(Errors.ErrorInteger))
			: new ValueTask<CallState>(new CallState(number.ToOrdinalWords()));
	}

	[SharpFunction(Name = "pos", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> StringPosition(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var arg1 = parser.CurrentState.Arguments["1"].Message!;

		return new ValueTask<CallState>(MModule.indexOf(arg0, arg1) + 1);
	}

	[SharpFunction(Name = "repeat", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "right", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "rjust", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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

		return ValueTask.FromResult<CallState>(MModule.pad(str, fill, widthInt, MarkupStringModule.PadType.Left,
			MarkupStringModule.TruncationType.Overflow));
	}

	[SharpFunction(Name = "scramble", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Scramble(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var split = MModule.split("", arg0).Shuffle();
		return ValueTask.FromResult<CallState>(string.Join("", split));
	}

	[SharpFunction(Name = "secure", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Secure(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "space", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Space(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var repeatNumberStr = parser.CurrentState.Arguments["0"].Message!;

		if (!int.TryParse(repeatNumberStr.ToPlainText(), out var repeatNumber))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorInteger));
		}

		var repeat = MModule.repeat(MModule.single(" "), repeatNumber, MModule.empty())!;
		return ValueTask.FromResult(new CallState(repeat));
	}

	[SharpFunction(Name = "spellnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "SQUISH", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
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

		// Normalize the string to Unicode Normalization Form D (NFD).
		// In NFD, accented characters are decomposed into a base character and combining diacritical marks.
		text = text.Normalize(NormalizationForm.FormD);

		// Filter out the combining diacritical marks (NonSpacingMark category).
		var chars = text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();

		// Create a new string from the filtered characters and normalize it back to Form C (NFC)
		// to recompose any characters that might have been decomposed but are not diacritics.
		return new string(chars).Normalize(NormalizationForm.FormC);
	}

	[SharpFunction(Name = "STRIPACCENTS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StripAccents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// We do nothing with arg1 for SharpMUSH.
		var arg0 = parser.CurrentState.Arguments["0"].Message!;

		var func = FuncConvert.FromFunc<string, string>(RemoveDiacritics);
		return ValueTask.FromResult<CallState>(MModule.apply(arg0, func));
	}

	[SharpFunction(Name = "stripansi", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> StripAnsi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!.ToPlainText());

	[SharpFunction(Name = "strlen", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StringLen(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!.Length);

	[SharpFunction(Name = "STRMATCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StringMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "switch", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
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
		Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
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

	[SharpFunction(Name = "TR", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Tr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "trim", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
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
			Configuration!.CurrentValue.Compatibility.TinyTrimFun
				? "2"
				: "1", out var arg2Value)
			? arg2Value.Message!.ToPlainText()
			: "b";

		var trimType = arg2 switch
		{
			"l" => MarkupStringModule.TrimType.TrimStart,
			"r" => MarkupStringModule.TrimType.TrimEnd,
			_ => MarkupStringModule.TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			MModule.trim(arg0, arg1, trimType));
	}

	[SharpFunction(Name = "trimpenn", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
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
			"l" => MarkupStringModule.TrimType.TrimStart,
			"r" => MarkupStringModule.TrimType.TrimEnd,
			_ => MarkupStringModule.TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			MModule.trim(arg0, arg1, trimType));
	}

	[SharpFunction(Name = "trimtiny", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
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
			"l" => MarkupStringModule.TrimType.TrimStart,
			"r" => MarkupStringModule.TrimType.TrimEnd,
			_ => MarkupStringModule.TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			MModule.trim(arg0, arg1, trimType));
	}

	[SharpFunction(Name = "ucstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> UpperCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var result = MModule.apply(arg0, FuncConvert.FromFunc<string, string>(x => x.ToUpperInvariant()));

		return new ValueTask<CallState>(new CallState(result));
	}

	[SharpFunction(Name = "urldecode", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> URLDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(new CallState(WebUtility.HtmlDecode(parser.CurrentState.Arguments["0"].Message!.ToPlainText())));

	[SharpFunction(Name = "urlencode", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> URLEncode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(new CallState(WebUtility.HtmlEncode(parser.CurrentState.Arguments["0"].Message!.ToPlainText())));

	[SharpFunction(Name = "wrap", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "strdelete", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
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
}