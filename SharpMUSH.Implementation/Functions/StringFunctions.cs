using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DotNext.Collections.Generic;
using Humanizer;
using Microsoft.FSharp.Core;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
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

	[SharpFunction(Name = "lit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Literal | FunctionFlags.NoParse)]
	public static ValueTask<CallState> Lit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(parser.CurrentState.Arguments["0"]);

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
		var sayString = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, "says, ");
		var transformObjAttr = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, "");
		var isNullObjAttr = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 4, "");
		var open = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 5, "\"");
		var close = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 6, "\"");

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
		=> ValueTask.FromResult<CallState>(new(parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value.Message)
			.Aggregate((x, y) => MModule.concat(x, y))));

	[SharpFunction(Name = "cat", Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Cat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(new(parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value.Message)
			.Aggregate((x, y) => MModule.concat(x, y, MModule.single(" ")))));

	[SharpFunction(Name = "ACCENT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Accent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ALIGN", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Align(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LALIGN", MinArgs = 2, MaxArgs = 6, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> LAlign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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

		if (ArticleRegex().Match(word).Success)
		{
			return "an";
		}

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

		if (ArticleRegex3().IsMatch(wordLower))
		{
			return "an";
		}

		return "a";
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

	[SharpFunction(Name = "CASE", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Case(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CASEALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> CaseAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CENTER", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Center(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!;
		var fill = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var rightFill = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, fill);

		if (!int.TryParse(width.ToPlainText(), out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(new CallState(Errors.ErrorPositiveInteger));
		}

		var result = MModule.center2(str, fill, rightFill, widthInt, MModule.TruncationType.Overflow);

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

	[SharpFunction(Name = "DIGEST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Digest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "EDIT", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Edit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ESCAPE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Escape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FLIP", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Flip(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message;
		var split = MModule.split("", arg0);
		return new ValueTask<CallState>(new CallState(MModule.multiple(split.Reverse())));
	}

	[SharpFunction(Name = "FOREACH", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ForEach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		//  foreach([<object>/]<attribute>, <string>[, <start>[, <end>]])

		var args = parser.CurrentState.ArgumentsOrdered;
		var objAttr = args["0"].Message;
		var str = args["1"].Message;
		var start = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ");
		var end = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, " ");
		var split = MModule.split("", str);

		var newStr = MModule.empty();

		foreach (var character in split)
		{
			// Method Pattern needs to change, attribute must be a native MString to support Lambda, 
			// so it's easier to do this split in a common code.
			// var parserEval = AttributeService!.EvaluateAttributeFunctionAsync(parser, executor, obj, attribu)

			newStr = MModule.concat(newStr, character);
		}

		throw new NotImplementedException();
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
	public static ValueTask<CallState> HMAC(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
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

	[SharpFunction(Name = "IFELSE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.NoParse)]
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

	[SharpFunction(Name = "LCSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> LowerCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var result = MModule.apply(arg0, FuncConvert.FromFunc<string, string>(x => x.ToLowerInvariant()));

		return new ValueTask<CallState>(new CallState(result));
	}

	[SharpFunction(Name = "left", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Left(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var len = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;

		if (!int.TryParse(len, out var strlen) || strlen < 0)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorPositiveInteger);
		}

		return ValueTask.FromResult<CallState>(MModule.substring(0, int.Min(strlen, str.Length), str));
	}

	[SharpFunction(Name = "ljust", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> LeftJustifyString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!.ToPlainText()!;
		var fill = Common.ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2,
			MModule.single(" "));

		if (!int.TryParse(width, out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(Errors.ErrorPositiveInteger);
		}

		return ValueTask.FromResult<CallState>(MModule.pad(str, fill, widthInt, MModule.PadType.Right,
			MModule.TruncationType.Overflow));
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

	[SharpFunction(Name = "MID", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Mid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
		var fill = Common.ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2,
			MModule.single(" "));

		if (!int.TryParse(width, out var widthInt) || widthInt < 0)
		{
			return new ValueTask<CallState>(Errors.ErrorPositiveInteger);
		}

		return ValueTask.FromResult<CallState>(MModule.pad(str, fill, widthInt, MModule.PadType.Left,
			MModule.TruncationType.Overflow));
	}

	[SharpFunction(Name = "scramble", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Scramble(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "STRIPACCENTS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StripAccents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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

	[SharpFunction(Name = "SWITCH", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Switch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SWITCHALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> SwitchAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "tr", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
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
		;
		var arg2 = parser.CurrentState.Arguments.TryGetValue(
			Configuration!.CurrentValue.Compatibility.TinyTrimFun
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
			"l" => MModule.TrimType.TrimStart,
			"r" => MModule.TrimType.TrimEnd,
			_ => MModule.TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(
			MModule.trim(arg0, arg1, trimType));
	}

	[SharpFunction(Name = "trimtiny", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> trimTiny(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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