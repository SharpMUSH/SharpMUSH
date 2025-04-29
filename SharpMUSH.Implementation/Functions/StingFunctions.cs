using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Functions;

public static partial class Functions
{
	[SharpFunction(Name = "after", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> After(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fullString = args["0"].Message;
		var search = args["1"].Message;
		var idx = MModule.indexOf(fullString, search);
		var result = MModule.substring(idx, MModule.getLength(fullString) - idx, args["0"].Message);
		
		return ValueTask.FromResult(new CallState(result));
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
		var args = parser.CurrentState.Arguments;
		var speaker = args["0"].Message!; // & for direct name!
		var speakString = args["1"].Message!;
		var sayString = NoParseDefaultNoParseArgument(args, 2, "says, ");
		var transformObjAttr = NoParseDefaultNoParseArgument(args, 3, "");
		var isNullObjAttr = NoParseDefaultNoParseArgument(args, 4, "");
		var open = NoParseDefaultNoParseArgument(args, 5, "\"");
		var close = NoParseDefaultNoParseArgument(args, 6, "\"");

		// TODO: This behavior gets re-used, so best to create a HelperFunction for this.

		var messageType = speakString.ToPlainText() switch
		{
			[':', .. _] => ':', 
			[';', .. _] => ';', 
			['|', .. _] => '|', 
			_ => '"'   
		};

		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var speakerIsLiteral = speaker.ToPlainText().StartsWith('&');
		var speakerObject = executor;
		var speakerName = MModule.empty();
		if (!speakerIsLiteral)
		{
			var maybeFound = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, speaker.ToPlainText(), LocateFlags.All);
			if (maybeFound.IsError)
			{
				return maybeFound.AsError;
			}

			var found = maybeFound.AsSharpObject;

			if (await parser.PermissionService.Controls(executor, found))
			{
				speakerObject = found;
			}

			speakerName = MModule.single(speakerObject.Object().Name);
		}
		else
		{
			speakerName = MModule.substring(1,speaker.Length-1,speaker);
		}

				
		
		throw new NotImplementedException();
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

	[SharpFunction(Name = "ALPHAMAX", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> AlphaMax(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ALPHAMIN", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> AlphaMin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ART", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Art(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "BEFORE", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Before(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "BRACKETS", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Brackets(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CAPSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> CapStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "CHR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Chr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FOREACH", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ForEach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
	

	[SharpFunction(Name = "decompose", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Decompose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FORMDECODE", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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
	public static ValueTask<CallState> IfElse(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LCSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> LCStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LEFT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Left(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LJUST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> LJust(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LPOS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LPos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "POS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Pos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REPEAT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Repeat(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "RIGHT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Right(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "RJUST", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RJust(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SCRAMBLE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Scramble(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SECURE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Secure(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SPACE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Space(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SPELLNUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> SpellNum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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

	[SharpFunction(Name = "STRIPANSI", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> StripAnsi(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "STRLEN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StrLen(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "STRMATCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StrMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SWITCH", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Switch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SWITCHALL", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> Switchall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TR", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Tr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TRIM", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Trim(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TRIMPENN", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> TrimPenn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TRIMTINY", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> trimTiny(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "UCSTR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> UCStr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "URLDECODE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> URLDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "URLENCODE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> URLEncode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WRAP", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Wrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "STRDELETE", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StrDelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TEXTSEARCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}