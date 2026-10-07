using SharpMUSH.Library.Models;
using System.Collections.Concurrent;
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
using DotNext;
using SharpMUSH.Library.Common;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private static readonly ConcurrentDictionary<(string Open, string Close), Regex> SpeechPatternCache = new();

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

	[SharpFunction(Name = "lit", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Literal | FunctionFlags.NoParse, ParameterNames = ["argument..."])]
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
		var hadErrors = false;
		var hasTransform = !string.IsNullOrWhiteSpace(transformObjAttr.ToPlainText());
		var hasNull = !string.IsNullOrWhiteSpace(isNullObjAttr.ToPlainText());
		var speakerObject = executor;
		MString speakerName;

		if (!speakerIsLiteral)
		{
			var maybeFound = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
				speaker.ToPlainText(), LocateFlags.All);
			switch (maybeFound)
			{
				case Error<CallState> error:
					return error.Value;
				case AnySharpObject found when await PermissionService.Controls(executor, found):
					speakerObject = found;
					break;
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
			if (HelperFunctions.SplitObjectAndAttr(transformObjAttr.ToPlainText()) is not { } splitTransform)
			{
				return new CallState(ErrorMessages.Returns.ObjectAttributeString);
			}

			var transformationObject = await
				LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
					executor,
					executor,
					splitTransform.Object,
					LocateFlags.All);

			switch (transformationObject)
			{
				case Error<CallState> error:
					return error.Value;
				case AnySharpObject found:
					actualTransformationObject = found;
					break;
			}

			actualTransformAttribute = splitTransform.Attribute;
		}

		if (hasTransform && hasNull)
		{
			if (HelperFunctions.SplitObjectAndAttr(isNullObjAttr.ToPlainText()) is not { } splitNull)
			{
				return new CallState(ErrorMessages.Returns.ObjectAttributeString);
			}

			actualNullAttribute = splitNull.Attribute;

			var nullObject = await
				LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
					executor,
					executor,
					splitNull.Object,
					LocateFlags.All);

			switch (nullObject)
			{
				case Error<CallState> error:
					return error.Value;
				case AnySharpObject found:
					actualNullObject = found;
					break;
			}
		}

		if (hasTransform)
		{
			var safeOpen = Regex.Escape(open.ToPlainText());
			var safeClose = Regex.Escape(close.ToPlainText());
			var pattern = SpeechPatternCache.GetOrAdd((safeOpen, safeClose),
				static key => SoftcodeRegex.Create($"{key.Open}(?<Content>[^{key.Close}]){key.Close}", RegexOptions.None)
			);

			var contents = pattern.Matches(speakString.ToPlainText());
			var markupContents = contents
				.Select(x => x.Groups["Content"]);

			foreach (var markupContent in markupContents)
			{
				var content = speakString.Substring(markupContent.Index, markupContent.Length);

				if (actualNullAttribute is not null)
				{
					var nullEvaluated = await AttributeService.EvaluateAttributeFunctionResultAsync(
						parser, executor, actualNullObject!, actualNullAttribute,
						new Dictionary<string, CallState>
						{
							{ "0", args["0"] },
							{ "1", new CallState(MarkupText.Plain(speakerObject.Object().DBRef.ToString())) },
							{ "2", new CallState(content) }
						});

					hadErrors |= nullEvaluated.HadErrors;
					if (nullEvaluated.Message.Truthy(parser)) continue;
				}

				var evaluated = await AttributeService.EvaluateAttributeFunctionResultAsync(
					parser, executor, actualTransformationObject!, actualTransformAttribute ?? string.Empty,
					new Dictionary<string, CallState>
					{
						{ "0", args["0"] },
						{ "1", new CallState(MarkupText.Plain(speakerObject.Object().DBRef.ToString())) },
						{ "2", new CallState(content) }
					});

				hadErrors |= evaluated.HadErrors;
				speakString = speakString.Replace(markupContent.Index, markupContent.Length, evaluated.Message ?? MarkupText.Empty);
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

		return new CallState(concat) { HadErrors = hadErrors };
	}

	/// <summary>
	/// Deletes <c>&lt;len&gt;</c> characters from <c>&lt;string&gt;</c> starting at the zero-based
	/// <c>&lt;first&gt;</c>. PennMUSH aliases <c>delete()</c> onto this (<c>src/function.c:335</c>);
	/// the list-flavoured deletion is <c>ldelete()</c>.
	/// </summary>
	/// <remarks>
	/// The range handling is <c>fun_delete</c>'s (<c>src/funstr.c:345</c>): a non-integer argument is
	/// <c>#-1 ARGUMENTS MUST BE INTEGERS</c>, a negative position is <c>#-1 OUT OF RANGE</c>, and a
	/// position past the end, a zero length or a negative length all answer the string untouched.
	/// The negative length is PennMUSH's code rather than its help: <c>fun_delete</c> shifts the
	/// position but leaves the count negative, and <c>ansi_string_delete</c> (<c>src/markup.c:2301</c>)
	/// returns early on <c>count &lt; 1</c>. The 1.8.8 oracle answers the input unchanged, so that is
	/// what this reproduces.
	/// </remarks>
	[SharpFunction(Name = "strdelete", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "position", "length"])]
	public ValueTask<CallState> StrDelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var first = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var len = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!ArgHelpers.TryInteger(parser, first, out var index)
				|| !ArgHelpers.TryInteger(parser, len, out var length))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Integers);
		}

		if (index < 0)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.OutOfRange);
		}

		return ValueTask.FromResult<CallState>(index >= str.Length || length < 1
			? str
			: str.Remove(index, Math.Min(length, str.Length - index)));
	}

	[SharpFunction(Name = "strinsert", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "position", "insert"])]
	public ValueTask<CallState> StrInsert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var positionStr = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var insert = parser.CurrentState.Arguments["2"].Message!;

		if (!ArgHelpers.TryInteger(parser, positionStr, out var position) || position < 0)
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

		if (!ArgHelpers.TryInteger(parser, startStr, out var start) || start < 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		if (!ArgHelpers.TryInteger(parser, lengthStr, out var length))
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

	[SharpFunction(Name = "strcat", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["string..."])]
	public ValueTask<CallState> Concat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var values = parser.CurrentState.ArgumentsOrdered.Values.Select(x => x.Message ?? MarkupText.Empty);
		return ValueTask.FromResult(FunctionLimits.ExceedsCombinedOutput(parser.CurrentState, values)
			? FunctionLimits.RejectOutput(parser.CurrentState) : new CallState(MarkupText.Concat(values)));
	}

	[SharpFunction(Name = "cat", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["string..."])]
	public ValueTask<CallState> Cat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var values = parser.CurrentState.ArgumentsOrdered.Values.Select(x => x.Message ?? MarkupText.Empty);
		return ValueTask.FromResult(FunctionLimits.ExceedsCombinedOutput(parser.CurrentState, values, 1)
			? FunctionLimits.RejectOutput(parser.CurrentState) : new CallState(MarkupText.Join(MarkupText.Space, values)));
	}

	[SharpFunction(Name = "accent", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "template"])]
	public ValueTask<CallState> Accent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var template = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (str.Length != template.Length)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));
		}

		var result = SharpMUSH.Library.Markup.AccentTemplate.Apply(str, template);

		return ValueTask.FromResult(new CallState(result));
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

		// Columns are laid out by their own spacing, which is what Preformatted says: a Pueblo client
		// reads the stream as HTML, where runs of spaces collapse and a proportional font ignores the
		// widths, and the portal gets a <pre> rather than leaning on the page's stylesheet. A terminal
		// is unaffected — it already lays text out this way.
		return MarkupText.Preformatted(TextAligner.Align(widths,
			columnArguments,
			filler: remainder.ElementAtOrDefault(0) ?? MarkupText.Space,
			columnSeparator: remainder.ElementAtOrDefault(1) ?? MarkupText.Space,
			rowSeparator: remainder.ElementAtOrDefault(2) ?? MarkupText.NewLine));
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

		return MarkupText.Preformatted(
			TextAligner.Align(widths, cols.Split(colDelim), filler, columnSeparator, rowSeparator));
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
	/// PennMUSH's <c>fun_art</c> (<c>src/funstr.c</c>): "an" when the argument starts with a vowel,
	/// "a" otherwise, empty argument included. It looks at the first character only, so
	/// <c>art(hour)</c> is "a" and <c>art(unicorn)</c> is "an".
	/// </summary>
	[SharpFunction(Name = "art", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> Art(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var text = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return ValueTask.FromResult<CallState>(text.Length > 0 && char.ToLowerInvariant(text[0]) is 'a' or 'e' or 'i' or 'o' or 'u'
			? "an"
			: "a");
	}

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
	public ValueTask<CallState> Case(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> SwitchInternal(parser, all: false, exact: true);

	[SharpFunction(Name = "caseall", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse, ParameterNames = ["string", "expression...|list...", "default"])]
	public ValueTask<CallState> CaseAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> SwitchInternal(parser, all: true, exact: true);

	[SharpFunction(Name = "center", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["text", "width", "fill"])]
	public ValueTask<CallState> Center(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!;
		var fill = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		var rightFill = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, fill);

		if (!ArgHelpers.TryUnsignedInteger(parser, width.ToPlainText(), out var widthInt))
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

		if (!ArgHelpers.TryStrictUnsignedInteger(arg0, out var charInt))
		{
			return new ValueTask<CallState>(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		// PennMUSH refuses control characters (C0, DEL and C1) with #-1 UNPRINTABLE CHARACTER
		// (fun_chr, src/funstr.c). Code points above 255 are a deliberate Unicode extension.
		if (charInt <= char.MaxValue && char.IsControl((char)charInt))
		{
			return new ValueTask<CallState>(new CallState(ErrorMessages.Returns.UnprintableCharacter));
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
		var type = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, "A").ToPlainText().ToUpperInvariant();

		// fun_comp compares N only as strict integers and F only as strict numbers, refusing anything
		// else with e_ints and e_nums (src/funstr.c:475-490) instead of comparing it as text.
		if (type == "N" && !(ArgHelpers.TryStrictInteger(value1, out int _) && ArgHelpers.TryStrictInteger(value2, out int _)))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integers));
		}

		if (type == "F" && !(NumericEvaluation.Strict.TryDouble(value1, out _) && NumericEvaluation.Strict.TryDouble(value2, out _)))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		int result = type switch
		{
			"I" => string.Compare(value1, value2, StringComparison.OrdinalIgnoreCase),
			"N" when ArgHelpers.TryStrictInteger(value1, out int int1) && ArgHelpers.TryStrictInteger(value2, out int int2) => int1.CompareTo(int2),
			"F" when NumericEvaluation.Strict.TryDouble(value1, out var real1) && NumericEvaluation.Strict.TryDouble(value2, out var real2) => real1.CompareTo(real2),
			"D" => CompareDbRefs(value1, value2),
			_ => string.Compare(value1, value2, StringComparison.Ordinal)
		};

		return ValueTask.FromResult(new CallState(result == 0 ? "0" : result < 0 ? "-1" : "1"));
	}

	private int CompareDbRefs(string value1, string value2)
	{
		// Try to parse as dbrefs (#123 or objid #123:timestamp format)
		if (HelperFunctions.ParseDbRef(value1) is DBRef dbref1 && HelperFunctions.ParseDbRef(value2) is DBRef dbref2)
		{
			return dbref1.Number.CompareTo(dbref2.Number);
		}

		// Fall back to string comparison if not valid dbrefs
		return string.Compare(value1, value2, StringComparison.Ordinal);
	}

	private static async ValueTask<CallState> EvaluateConditional(IMUSHCodeParser parser, bool negate, bool all)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var pairCount = args.Count / 2;
		var results = new List<MString>();
		var hadErrors = false;
		for (var i = 0; i < pairCount; i++)
		{
			var condition = await parser.FunctionParse(args[(i * 2).ToString()].Message!);
			hadErrors |= condition?.HadErrors == true;
			if (condition?.Message.Truthy(parser) == !negate)
			{
				var result = await parser.FunctionParse(args[(i * 2 + 1).ToString()].Message!);
				hadErrors |= result?.HadErrors == true;
				if (!all) return (result ?? CallState.Empty) with { HadErrors = hadErrors };
				// Keep an empty selected result: the default runs only if no condition matched.
				results.Add(result?.Message ?? MarkupText.Empty);
			}
		}
		if (results.Count > 0) return new CallState(MarkupText.Concat(results)) { HadErrors = hadErrors };
		var fallback = args.Count % 2 == 1
			? await parser.FunctionParse(args[(args.Count - 1).ToString()].Message!) ?? CallState.Empty
			: CallState.Empty;
		return fallback with { HadErrors = hadErrors || fallback.HadErrors };
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

		return CryptoHelpers.Digest(arg0, arg1!) is string digest
			? digest
			: ErrorMessages.Returns.ArgRange;
	}

	[SharpFunction(Name = "SHA0", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular,
		ParameterNames = ["text"])]
	public ValueTask<CallState> SHA0(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// SHA-0 is deprecated and not supported in modern .NET/OpenSSL
		// Return error message per PennMUSH documentation
		return new ValueTask<CallState>(new CallState(ErrorMessages.Returns.ErrorNotSupported));
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
		foreach (var special in SoftcodeDecomposer.SoftcodeSpecial().EnumerateMatches(text, 1))
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

	/// <summary>
	/// map() over the characters of a string rather than over the words of a list: the ufun runs
	/// once per grapheme cluster with the character as <c>%0</c> and its zero-based position in the
	/// string as <c>%1</c>, and the results are concatenated with nothing between them.
	/// <c>&lt;start&gt;</c> and <c>&lt;end&gt;</c> bracket the transformed span — what lies outside
	/// it is copied through untouched and the markers themselves are dropped (PennMUSH
	/// <c>fun_foreach</c>, funstr.c:1123).
	/// </summary>
	[SharpFunction(Name = "foreach", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "string", "start", "end"])]
	public async ValueTask<CallState> ForEach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		// Penn checks both markers before it fetches the ufun, and an argument that is present but
		// empty is a space rather than "no marker at all" (delim_check, function.c:248).
		if (!TryForEachMarker(args, "2", out var start) || !TryForEachMarker(args, "3", out var end))
		{
			return new CallState(ErrorMessages.Returns.SeparatorMustBeOneChar);
		}

		var text = args["1"].Message ?? MarkupText.Empty;
		var rawAttrArg = args["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			return await ForEachTransformAsync(text, start, end, (character, position) =>
				AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor, rawAttrArg,
					ForEachEnvironment(character, position)));
		}

		return await AttributeService.FetchAttributeFunctionAsync(parser, executor, rawAttrStr) switch
		{
			AttributeFunction function => await ForEachTransformAsync(text, start, end, (character, position) =>
				AttributeService.CallAttributeFunctionAsync(parser.Push(parser.CurrentState with
				{
					Arguments = ForEachEnvironment(character, position),
					EnvironmentRegisters = ForEachEnvironment(character, position)
				}), function)),
			CallState refusal => refusal,
		};
	}

	private static Dictionary<string, CallState> ForEachEnvironment(MString character, int position)
		=> new() { ["0"] = new CallState(character), ["1"] = new CallState(position) };

	/// <summary>
	/// A <c>foreach()</c> marker argument: absent gives <see langword="null"/>, present but empty
	/// gives a space, and anything longer than one character is refused.
	/// </summary>
	private static bool TryForEachMarker(IReadOnlyDictionary<string, CallState> args, string index, out string? marker)
	{
		marker = null;
		if (!args.TryGetValue(index, out var argument)) return true;

		var value = argument.Message ?? MarkupText.Empty;
		if (value.Length == 0)
		{
			marker = " ";
			return true;
		}

		var graphemes = SplitIntoGraphemes(value);
		if (graphemes.Length != 1) return false;

		marker = graphemes[0].ToPlainText();
		return true;
	}

	private static async ValueTask<CallState> ForEachTransformAsync(MString text, string? start, string? end,
		Func<MString, int, ValueTask<CallState>> call)
	{
		var graphemes = SplitIntoGraphemes(text);
		var errors = new ListEvaluationErrors();
		var pieces = new List<MString>();
		var position = 0;

		if (start is not null)
		{
			var opening = Array.FindIndex(graphemes, grapheme => grapheme.ToPlainText() == start);
			// No opening marker anywhere means the string is handed back untransformed.
			if (opening < 0) return new CallState(text);
			pieces.Add(MarkupText.Concat(graphemes[..opening]));
			position = opening + 1;
		}

		while (position < graphemes.Length)
		{
			if (end is not null && graphemes[position].ToPlainText() == end)
			{
				position++;
				break;
			}

			pieces.Add(errors.Record(await call(graphemes[position], position)));
			position++;
		}

		if (position < graphemes.Length) pieces.Add(MarkupText.Concat(graphemes[position..]));

		return errors.Complete(new CallState(MarkupText.Concat(pieces)));
	}

	/// <summary>
	/// The string as HTML: MarkupString's HTML renderer writes its markup (colour, bold, links, tags,
	/// the shared vocabulary) and encodes the text, so the result can be placed in a page as is.
	/// </summary>
	[SharpFunction(Name = "decomposeweb", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> DecomposeWeb(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(parser.CurrentState.Arguments["0"].Message!.Render(MarkupFormat.Html)));

	[SharpFunction(Name = "decompose", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> Decompose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(SoftcodeDecomposer.Decompose(parser.CurrentState.Arguments["0"].Message!)));

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
		var encoding = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, "base16").ToPlainText().ToLowerInvariant();

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
		var parsedIfElse = await parser.CurrentState.Arguments["0"].GetParsedResultAsync();
		var truthy = parsedIfElse.Message.Truthy(parser);
		var result = CallState.Empty;

		if (truthy)
		{
			result = await parser.FunctionParse(parser.CurrentState.Arguments["1"].Message!);
		}
		else if (parser.CurrentState.Arguments.TryGetValue("2", out var arg2))
		{
			result = await parser.FunctionParse(arg2.Message!);
		}

		return (result ?? CallState.Empty) with { HadErrors = result?.HadErrors == true || parsedIfElse.HadErrors };
	}

	/// <summary>PennMUSH registers <c>ifelse</c> as <c>fun_if</c> with three required arguments (<c>src/function.c:504</c>).</summary>
	[SharpFunction(Name = "ifelse", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.NoParse, ParameterNames = ["expression"])]
	public ValueTask<CallState> IfElse(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> If(parser, _2);

	[SharpFunction(Name = "lcstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> LowerCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return new ValueTask<CallState>(
			parser.CurrentState.Arguments["0"].Message!.Apply(transform: x => x.ToLowerInvariant()));
	}

	[SharpFunction(Name = "LCSTR2", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> LCStr2(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return new ValueTask<CallState>(new CallState(str.ToLowerInvariant()));
	}

	[SharpFunction(Name = "left", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "length"])]
	public ValueTask<CallState> Left(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var len = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		// fun_left: e_int, then e_range for a negative length (src/funstr.c:303-312).
		return ArgHelpers.TryInteger(parser, len, out var strlen) switch
		{
			false => ValueTask.FromResult<CallState>(ErrorMessages.Returns.Integer),
			true when strlen < 0 => ValueTask.FromResult<CallState>(ErrorMessages.Returns.OutOfRange),
			true => ValueTask.FromResult<CallState>(str.Substring(0, int.Min(strlen, str.Length)))
		};
	}

	[SharpFunction(Name = "ljust", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["text", "width", "fill", "truncate"])]
	public ValueTask<CallState> LeftJustifyString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var width = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var fill = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2,
			MarkupText.Space);
		var truncate = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, MarkupText.Plain("")).ToPlainText();

		if (!ArgHelpers.TryUnsignedInteger(parser, width, out var widthInt))
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

		// fun_mid (pennmush src/funstr.c:266-295): both numbers must be integers, a negative start is out of
		// range, and a negative length counts back from the start. safe_ansi_string clips the slice to the string.
		if (!ArgHelpers.TryInteger(parser, first, out var position)
				|| !ArgHelpers.TryInteger(parser, length, out var count))
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.Integers);
		}

		if (position < 0)
		{
			return new ValueTask<CallState>(ErrorMessages.Returns.OutOfRange);
		}

		if (count < 0)
		{
			position = Math.Max(position + count + 1, 0);
			count = -count;
		}

		if (position >= str.Length || count < 1)
		{
			return ValueTask.FromResult<CallState>(MString.Empty);
		}

		return ValueTask.FromResult<CallState>(str.Substring(position, Math.Min(count, str.Length - position)));
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

	/// <summary>
	/// PennMUSH's <c>fun_spellnum</c> called as <c>ORDINAL</c> (<c>src/funmath.c</c>): the number is spelled
	/// the way <c>spellnum()</c> spells it (<c>ordinal(100)</c> is "one hundredth", <c>ordinal(-1)</c>
	/// "negative first") and its last word is made ordinal. Anything but digits is not a number; a
	/// decimal point is a number that is not an integer.
	/// </summary>
	[SharpFunction(Name = "ORDINAL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public ValueTask<CallState> Ordinal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var number = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim(' ');
		var minus = number.StartsWith('-');
		if (minus || number.StartsWith('+'))
		{
			number = number[1..];
		}

		number = number.TrimStart('0');

		foreach (var c in number)
		{
			if (c == '.')
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
			}

			if (!char.IsAsciiDigit(c))
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Number));
			}
		}

		// Penn pads to whole groups of three before its 999,999,999,999,999 limit.
		var padded = number.PadLeft((number.Length + 2) / 3 * 3, '0');
		if (padded.Length > 15)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.OutOfRange));
		}

		var spelled = number.Length == 0
			? "zero"
			: (minus ? "negative " : "") + SpellDigits(padded);

		return ValueTask.FromResult(new CallState(Ordinalize(spelled)));
	}

	/// <summary>
	/// PennMUSH's <c>do_spellnum</c> (<c>src/funmath.c</c>): digits, a multiple of three long, spelled
	/// out in groups of three with no "and".
	/// </summary>
	private static string SpellDigits(string digits)
	{
		string[] bigOnes = ["", "thousand", "million", "billion", "trillion"];
		string[] singles = ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
		string[] special = ["ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
		string[] tens = ["", " ", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

		var spelled = new StringBuilder();
		var group = digits.Length / 3;

		for (var offset = 0; group > 0; offset += 3)
		{
			group--;
			var x0 = digits[offset] - '0';
			var x1 = digits[offset + 1] - '0';
			var x2 = digits[offset + 2] - '0';

			if (x0 != 0)
			{
				if (spelled.Length > 0) spelled.Append(' ');
				spelled.Append(singles[x0]).Append(" hundred");
			}

			if (x1 == 1)
			{
				if (spelled.Length > 0) spelled.Append(' ');
				spelled.Append(special[x2]);
			}
			else if (x1 != 0 || x2 != 0)
			{
				if (spelled.Length > 0) spelled.Append(' ');
				if (x1 != 0)
				{
					spelled.Append(tens[x1]);
					if (x2 != 0) spelled.Append('-');
				}

				spelled.Append(singles[x2]);
			}

			if (group > 0 && (x0 != 0 || x1 != 0 || x2 != 0))
			{
				if (spelled.Length > 0) spelled.Append(' ');
				spelled.Append(bigOnes[group]);
			}
		}

		return spelled.ToString();
	}

	private static readonly string[] OrdinalSingles = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "twelve"];
	private static readonly string[] OrdinalSinglesTh = ["first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "twelfth"];

	/// <summary>
	/// PennMUSH's <c>do_ordinalize</c> (<c>src/funmath.c</c>): the last word of a spelled number, made ordinal.
	/// </summary>
	private static string Ordinalize(string spelled)
	{
		for (var i = 0; i < OrdinalSingles.Length; i++)
		{
			if (spelled.EndsWith(OrdinalSingles[i], StringComparison.OrdinalIgnoreCase))
			{
				return spelled[..^OrdinalSingles[i].Length] + OrdinalSinglesTh[i];
			}
		}

		return spelled.EndsWith('y')
			? spelled[..^1] + "ieth"
			: spelled + "th";
	}

	/// <summary>
	/// PennMUSH's <c>fun_pos</c> (<c>src/funstr.c</c>): where the first argument first appears in the
	/// second, counting from 1, or <c>#-1</c> when it does not. An empty first argument is at 1.
	/// </summary>
	[SharpFunction(Name = "pos", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["needle", "haystack"])]
	public ValueTask<CallState> StringPosition(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var needle = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var haystack = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var index = haystack.IndexOf(needle, StringComparison.Ordinal);

		return ValueTask.FromResult<CallState>(index < 0 ? ErrorMessages.Returns.Nothing : (index + 1).ToString());
	}

	[SharpFunction(Name = "repeat", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "count"])]
	public ValueTask<CallState> Repeat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var repeatNumberStr = parser.CurrentState.Arguments["1"].Message!;

		if (!ArgHelpers.TryInteger(parser, repeatNumberStr.ToPlainText(), out var repeatNumber))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		if (FunctionLimits.ExceedsOutput(parser.CurrentState, (long)str.Length * repeatNumber))
			return ValueTask.FromResult(FunctionLimits.RejectOutput(parser.CurrentState));
		if (str.Length == 0) return ValueTask.FromResult(CallState.Empty);
		var repeat = str.Repeat(repeatNumber);
		return ValueTask.FromResult(new CallState(repeat));
	}

	[SharpFunction(Name = "right", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "length"])]
	public ValueTask<CallState> Right(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!;
		var len = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		// fun_right: e_int, then e_range for a negative length (src/funstr.c:325-334).
		if (!ArgHelpers.TryInteger(parser, len, out var strlen))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Integer);
		}

		if (strlen < 0)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.OutOfRange);
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

		if (!ArgHelpers.TryUnsignedInteger(parser, width, out var widthInt))
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

	/// <summary>fun_secure (src/funstr.c): every <see cref="SoftcodeDecomposer.SoftcodeSpecial"/> character becomes a space.</summary>
	[SharpFunction(Name = "secure", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> Secure(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments["0"].Message!
			.Apply(text => SoftcodeDecomposer.SoftcodeSpecial().Replace(text, " ")));

	[SharpFunction(Name = "space", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["count"])]
	public ValueTask<CallState> Space(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var repeatNumberStr = parser.CurrentState.Arguments["0"].Message!;

		if (!ArgHelpers.TryStrictUnsignedInteger(repeatNumberStr.ToPlainText(), out var repeatNumber))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		if (FunctionLimits.ExceedsOutput(parser.CurrentState, repeatNumber))
			return ValueTask.FromResult(FunctionLimits.RejectOutput(parser.CurrentState));
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

	[SharpFunction(Name = "strlen", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["string", "count-controls"])]
	public ValueTask<CallState> StringLen(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Display cells, not UTF-16 code units: softcode measures a string to lay it out against
		// something else, and a combining mark or a wide character makes those two numbers differ
		// wildly — "Text Editor" under a pile of diacritics is 66 code units and 11 columns.
		// A control character (tab, newline) is 0 columns wide but one character to PennMUSH
		// (ansi_strlen, src/markup.c), so by default it counts here: strlen(%t) is 1. A false
		// second argument opts out and measures controls as the 0 columns they occupy.
		var countControls = !parser.CurrentState.Arguments.TryGetValue("1", out var arg1)
			|| arg1.Message.Truthy(parser);

		return ValueTask.FromResult<CallState>(
			parser.CurrentState.Arguments["0"].Message!.GetDisplayWidth(
				countControls ? ControlCharacterWidth.One : ControlCharacterWidth.Zero));
	}

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
	public ValueTask<CallState> Switch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> SwitchInternal(parser, all: false, exact: false);

	[SharpFunction(Name = "switchall", MinArgs = 3, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.NoParse, ParameterNames = ["string", "expression...|list...", "default"])]
	public ValueTask<CallState> SwitchAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> SwitchInternal(parser, all: true, exact: false);

	/// <summary>
	/// PennMUSH's <c>fun_switch</c> (<c>src/funmisc.c</c>), behind switch(), switchall(), case() and
	/// caseall(). The subject is the switch text (<c>%$0</c>, <c>stext()</c>). Arguments pair as (pattern,
	/// list), with a default only when one is left over, and a list is evaluated only when its own
	/// pattern matched.
	/// </summary>
	/// <param name="exact">
	/// case(): a case-sensitive string comparison, and no regexp context of its own. Otherwise a glob,
	/// whose captures (one per <c>*</c> or <c>?</c>, numbered from 0) are a regexp context that
	/// <c>$0</c>-<c>$9</c> read while the matched list is evaluated.
	/// </param>
	private static async ValueTask<CallState> SwitchInternal(IMUSHCodeParser parser, bool all, bool exact)
	{
		var hadErrors = false;
		async ValueTask<MString?> EvaluateArgument(CallState argument)
		{
			var result = await argument.GetParsedResultAsync();
			hadErrors |= result.HadErrors;
			return result.Message;
		}

		var arg0 = await EvaluateArgument(parser.CurrentState.Arguments["0"]) ?? MarkupText.Empty;
		// (pattern, list) pairs, and a default only when an argument is left over.
		var cases = parser.CurrentState.ArgumentsOrdered.Skip(1).Select(kv => kv.Value).Chunk(2).ToList();
		var defaultValue = cases is [.., [var lone]] ? lone : null;
		var resultList = new List<MString>();

		var captures = exact ? null : new RegexpCaptureFrame(parser.CurrentState.CurrentEvaluation);
		if (captures is not null) parser.CurrentState.RegexRegisters.Push(captures);
		parser.CurrentState.SwitchStack.Push(arg0);

		try
		{
			foreach (var (pattern, list) in cases.Where(pair => pair.Length == 2).Select(pair => (pair[0], pair[1])))
			{
				var expression = await EvaluateArgument(pattern) ?? MarkupText.Empty;
				var matches = captures is null
					? arg0.ToPlainText() == expression.ToPlainText()
					: SwitchPatterns.Matches(arg0, expression.ToPlainText(), regexp: false, captures);
				if (!matches)
				{
					continue;
				}

				var matched = await EvaluateArgument(list) ?? MarkupText.Empty;
				if (!all)
				{
					return new CallState(matched) { HadErrors = hadErrors };
				}

				resultList.Add(matched);
			}

			var result = resultList.Count != 0
				? MarkupText.Concat(resultList)
				: defaultValue is null ? MarkupText.Empty : await EvaluateArgument(defaultValue) ?? MarkupText.Empty;
			return new CallState(result) { HadErrors = hadErrors };
		}
		finally
		{
			parser.CurrentState.SwitchStack.TryPop(out _);
			if (captures is not null) parser.CurrentState.RegexRegisters.TryPop(out _);
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
		=> parser.ServiceProvider.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
			.CurrentValue.Compatibility.TinyTrimFun
			? TrimTiny(parser, _2)
			: TrimPenn(parser, _2);

	[SharpFunction(Name = "trimpenn", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> TrimPenn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> TrimWith(parser, charactersItem: 1, styleItem: 2);

	[SharpFunction(Name = "trimtiny", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> TrimTiny(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> TrimWith(parser, charactersItem: 2, styleItem: 1);

	/// <summary>
	/// The trim family's one body: PennMUSH's trim(&lt;string&gt;, &lt;characters&gt;, &lt;style&gt;) and
	/// TinyMUSH's trim(&lt;string&gt;, &lt;style&gt;, &lt;characters&gt;) differ only in which argument is which.
	/// The characters default to a space and the style to "b"; a style other than "l" or "r" trims both ends.
	/// </summary>
	private static ValueTask<CallState> TrimWith(IMUSHCodeParser parser, int charactersItem, int styleItem)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var characters = ArgHelpers.NoParseDefaultNoParseArgument(args, charactersItem, MarkupText.Space);
		var trimType = ArgHelpers.NoParseDefaultNoParseArgument(args, styleItem, "b").ToPlainText().ToLowerInvariant() switch
		{
			"l" => TrimType.TrimStart,
			"r" => TrimType.TrimEnd,
			_ => TrimType.TrimBoth,
		};

		return ValueTask.FromResult<CallState>(args["0"].Message!.Trim(trimType, characters.ToPlainText()));
	}

	[SharpFunction(Name = "ucstr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> UpperCaseString(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var result = arg0.Apply(x => x.ToUpperInvariant());

		return new ValueTask<CallState>(result);
	}

	[SharpFunction(Name = "UCSTR2", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> UCStr2(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return new ValueTask<CallState>(new CallState(str.ToUpperInvariant()));
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
		var lineSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.NewLine);

		// fun_wrap (src/funstr.c:1644-1669): an empty text is answered as it is, before the widths are
		// read; each width goes through int_check (72 by default, the first line defaulting to the
		// width, and 0 for it meaning the width); and a width below 2 is too small.
		if (str.ToPlainText().Length == 0)
		{
			return str;
		}

		if (!ArgHelpers.TryIntCheck(parser, args["1"].Message!.ToPlainText(), 72, out var widthInt)
			|| !ArgHelpers.TryIntCheck(parser, args.TryGetValue("2", out var firstArg) ? firstArg.Message?.ToPlainText() ?? "" : null,
				widthInt, out var firstLineInt))
		{
			return ErrorMessages.Returns.Integer;
		}

		if (firstLineInt == 0)
		{
			firstLineInt = widthInt;
		}

		if (widthInt < 2 || firstLineInt < 2)
		{
			return ErrorMessages.Returns.WidthTooSmall;
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

	[SharpFunction(Name = "@@", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public ValueTask<CallState> AtAt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(string.Empty));

	/// <summary>
	/// The truthy candidates joined by the trailing delimiter.
	/// </summary>
	/// <remarks>
	/// <c>allof()</c> is <see cref="WhichOfAllAsync"/> with PennMUSH's <c>isbool</c> flag on;
	/// <see cref="StringAllOf"/> is the same body with it off.
	/// </remarks>
	[SharpFunction(Name = "allof", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["value..."])]
	public ValueTask<CallState> AllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> WhichOfAllAsync(parser, isBool: true);

	/// <summary>
	/// <c>do_whichof</c> with <c>allof</c>'s <c>all</c> set (<c>src/funmisc.c:1390-1420</c>): the
	/// trailing argument is the output delimiter and is parsed first, then every candidate is parsed
	/// left to right and the ones that count are joined by it.
	/// </summary>
	/// <remarks>
	/// PennMUSH registers <c>ALLOF</c> and <c>STRALLOF</c> on the one <c>fun_allof</c>
	/// (<c>src/function.c:765</c>) and branches on <c>called_as</c>'s <c>isbool</c> flag, which is
	/// the whole of the difference: a candidate counts because it is true, or because it is
	/// non-empty. Written out twice here, the two bodies could drift in the twenty lines they share
	/// and only differ in one expression.
	/// </remarks>
	/// <param name="isBool">PennMUSH's <c>isbool</c>: keep the truthy candidates rather than the non-empty ones.</param>
	private static async ValueTask<CallState> WhichOfAllAsync(IMUSHCodeParser parser, bool isBool)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		// With 0 or 1 argument there is no delimiter, so there are no candidates either.
		if (args.Count < 2)
		{
			return CallState.Empty;
		}

		var delimParsed = await parser.FunctionParse(args[(args.Count - 1).ToString()].Message!);
		var delimiter = delimParsed?.Message ?? MarkupText.Empty;
		var hadErrors = delimParsed?.HadErrors == true;

		var kept = new List<MString>();
		for (var i = 0; i < args.Count - 1; i++)
		{
			var parsed = await parser.FunctionParse(args[i.ToString()].Message!);
			hadErrors |= parsed?.HadErrors == true;
			var value = parsed?.Message ?? MarkupText.Empty;
			if (isBool ? value.Truthy(parser) : value.Length > 0) kept.Add(value);
		}

		return new CallState(MarkupText.Join(delimiter, kept)) { HadErrors = hadErrors };
	}

	// PennMUSH registers ANSI as 2, -2 (function.c:365): the negative maximum means the text is not
	// comma-split, so ansi(h,a,b) colours "a,b". SharpMUSH splits every call and colours the second
	// argument, which is two arguments' worth of contract, so the second is where it stops: a third
	// argument is refused by name rather than evaluated and discarded.
	[SharpFunction(Name = "ansi", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["codes", "string"])]
	public ValueTask<CallState> ANSI(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		return ValueTask.FromResult(new CallState(MarkupText.Wrap(AnsiCodes(args["0"].Message!.ToPlainText()), args["1"].Message ?? MarkupText.Empty)));
	}

	/// <summary>
	/// ansi() codes as a layer. The codes are read by MarkupString's AnsiCodeParser, the one parser of
	/// them; the game supplies only its colour names (colors.json).
	/// </summary>
	private AnsiMarkup AnsiCodes(string codes)
	{
		var colors = ColorConfiguration?.CurrentValue;
		return AnsiCodeParser.Parse(codes,
			name => colors is not null && colors.ColorsByName.TryGetValue(name, out var color)
				? ColorTranslator.FromHtml(color.rgb).ToAnsiColor()
				: null);
	}

	[SharpFunction(Name = "null", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> Null(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(CallState.Empty);

	/// <summary>
	/// <c>render(&lt;string&gt;, &lt;formats&gt;)</c> — PennMUSH's <c>fun_render</c>: turn a string's
	/// markup into wire format for something outside the game, a bot or a web page.
	///
	/// <para>What stood here was an objeval: it located an object, checked Controls and evaluated the
	/// second argument as that object — which is what <c>objeval()</c> already does, under a name the
	/// helpfile gave to something else entirely.</para>
	/// </summary>
	/// <remarks>
	/// PennMUSH's <c>markup</c> flag asks for whatever the other flags did not handle to survive as
	/// internal markup tags. SharpMUSH holds markup as layers over the text rather than as inline
	/// tags, and renders a whole string to one format, so there is nothing for the flag to leave
	/// behind: it is accepted and changes nothing. See <c>pennmush-compatibility.md</c>.
	/// </remarks>
	[SharpFunction(Name = "render", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "formats"])]
	public async ValueTask<CallState> Render(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var text = args["0"].Message ?? MarkupText.Empty;

		var ansi = false;
		var html = false;
		var noAccents = false;

		foreach (var format in args["1"].Message!.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			// PennMUSH prefix-matches "noaccents" alone; the other three are spelled in full.
			if (format.Equals("ansi", StringComparison.OrdinalIgnoreCase)) ansi = true;
			else if (format.Equals("html", StringComparison.OrdinalIgnoreCase)) html = true;
			else if ("noaccents".StartsWith(format, StringComparison.OrdinalIgnoreCase)) noAccents = true;
			else if (format.Equals("markup", StringComparison.OrdinalIgnoreCase)) { }
			else return new CallState(ErrorMessages.Returns.InvalidSecondArgument);
		}

		// Raw colour codes are a spoofing tool wherever the result is echoed back into the game.
		if (ansi && !await PermissionService.CanNoSpoof(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var rendered = (html, ansi) switch
		{
			(true, _) => text.Render(MarkupFormat.Html),
			(_, true) => text.Render(MarkupFormat.Ansi),
			_ => text.ToPlainText()
		};

		return new CallState(noAccents ? RemoveDiacritics(rendered) : rendered);
	}

	[SharpFunction(Name = "s", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public async ValueTask<CallState> S(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> (await parser.FunctionParse(parser.CurrentState.Arguments.Last().Value.Message!))!;
}
