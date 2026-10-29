using System.Globalization;
using System.Text;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

/// <summary>Functions RhostMUSH games rely on that PennMUSH has no counterpart for.</summary>
public partial class Functions
{
	private static readonly string[] ObjectTypeNames = ["player", "thing", "room", "exit", "other"];

	/// <summary>keeptype(): the elements of a list whose object type is one of those named.</summary>
	[SharpFunction(Name = "keeptype", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "types", "delimiter", "output-separator", "form"])]
	public ValueTask<CallState> KeepType(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FilterByType(parser, keep: true);

	/// <summary>remtype(): the elements of a list whose object type is none of those named.</summary>
	[SharpFunction(Name = "remtype", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "types", "delimiter", "output-separator", "form"])]
	public ValueTask<CallState> RemType(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FilterByType(parser, keep: false);

	private async ValueTask<CallState> FilterByType(IMUSHCodeParser parser, bool keep)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list = args["0"].Message;
		var delimiter = ArgHelpers.Delimiter(args, 2);
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Space);
		var form = args.TryGetValue("4", out var formArg) ? ArgHelpers.ParseInteger(formArg.Message.ToPlainText()) : 0;

		// Each word names a type by any part of its start; with none recognised the list is unchanged.
		var types = args["1"].Message.ToPlainText()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(word => ObjectTypeNames.FirstOrDefault(type => type.StartsWith(word, StringComparison.OrdinalIgnoreCase)))
			.OfType<string>()
			.ToHashSet();
		if (types.Count == 0) return list;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var kept = new List<MString>();
		foreach (var element in MushText.SplitList(delimiter, list))
		{
			var found = await LocateService.Locate(parser, executor, executor, element.ToPlainText().Trim(), LocateFlags.All);
			var (type, written) = found switch
			{
				AnySharpObject obj => (obj.TypeString().ToLowerInvariant(), form switch
				{
					1 => MarkupText.Plain($"#{obj.Object().DBRef.Number}"),
					2 => MarkupText.Plain(obj.Object().DBRef.ToString()),
					_ => element
				}),
				_ => ("other", element)
			};

			if (types.Contains(type) == keep) kept.Add(written);
		}

		return MarkupText.Join(separator, kept);
	}

	/// <summary>keepflags(): the objects in a list that have all (<c>&amp;</c>) or any (<c>|</c>) of some flag letters.</summary>
	[SharpFunction(Name = "keepflags", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "flags", "delimiter", "output-separator", "form"])]
	public ValueTask<CallState> KeepFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FilterByFlags(parser, keep: true);

	/// <summary>remflags(): the objects in a list that lack all (<c>&amp;</c>) or any (<c>|</c>) of some flag letters.</summary>
	[SharpFunction(Name = "remflags", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "flags", "delimiter", "output-separator", "form"])]
	public ValueTask<CallState> RemFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FilterByFlags(parser, keep: false);

	private async ValueTask<CallState> FilterByFlags(IMUSHCodeParser parser, bool keep)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var flags = args["1"].Message.ToPlainText().Trim();
		if (flags is not ['&' or '|', ..])
		{
			return new CallState(ErrorMessages.Returns.FlagMatchNeedsOperator);
		}

		var delimiter = ArgHelpers.Delimiter(args, 2);
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delimiter);
		var objid = args.TryGetValue("4", out var formArg) && ArgHelpers.ParseInteger(formArg.Message.ToPlainText()) == 1;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var kept = new List<MString>();
		foreach (var element in MushText.SplitList(delimiter, args["0"].Message))
		{
			// An element that names nothing is reported, as a lookup is, and left out either way.
			if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, element.ToPlainText().Trim(),
					LocateFlags.All) is not AnySharpObject obj)
			{
				continue;
			}

			var matched = await FlagLetterCheck(obj, flags[1..], orMode: flags[0] == '|') == true;
			if (matched == keep)
			{
				kept.Add(MarkupText.Plain(objid ? obj.Object().DBRef.ToString() : $"#{obj.Object().DBRef.Number}"));
			}
		}

		return MarkupText.Join(separator, kept);
	}

	/// <summary>
	/// strfunc(): calls a function with the elements of a list as its arguments. Each element is passed as
	/// it stands, so nothing in it is evaluated again, and the spaces at its ends are kept.
	/// </summary>
	[SharpFunction(Name = "strfunc", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["function", "list", "delimiter"])]
	public async ValueTask<CallState> StrFunc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args["0"].Message.ToPlainText().Trim();
		if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '`' or '-' or '.'))
		{
			return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant()));
		}

		var arguments = MushText.SplitList(ArgHelpers.Delimiter(args, 2), args["1"].Message)
			.Select(element => KeepEdgeSpaces(EscapeSoftcode(element.ToPlainText(), _ => true)));
		return await parser.FunctionParse(MarkupText.Plain($"{name}({string.Join(",", arguments)})")) ?? CallState.Empty;
	}

	/// <summary>Spaces at either end of an argument as <c>%b</c>, which the parser does not trim as it does a space.</summary>
	private static string KeepEdgeSpaces(string argument)
	{
		var trimmed = argument.Trim(' ');
		if (trimmed.Length == argument.Length) return argument;

		var leading = argument.Length - argument.TrimStart(' ').Length;
		var trailing = trimmed.Length == 0 ? 0 : argument.Length - argument.TrimEnd(' ').Length;
		return string.Concat(Enumerable.Repeat("%b", leading)) + trimmed + string.Concat(Enumerable.Repeat("%b", trailing));
	}

	/// <summary>A backslash before each character the parser gives meaning to, when <paramref name="escapes"/> says so.</summary>
	private static string EscapeSoftcode(string text, Func<int, bool> escapes)
	{
		var escaped = new StringBuilder(text.Length * 2);
		var next = 0;
		foreach (var special in SoftcodeDecomposer.SoftcodeSpecial().EnumerateMatches(text))
		{
			escaped.Append(text, next, special.Index - next);
			if (escapes(special.Index)) escaped.Append('\\');
			next = special.Index;
		}

		return escaped.Append(text, next, text.Length - next).ToString();
	}

	/// <summary>
	/// escapex(): escape(), leaving alone the characters listed and the kinds of <c>%</c>-substitution the
	/// letters <c>a 8 s d p x</c> name; <c>f</c> drops the leading backslash and <c>r</c> escapes only what
	/// the others would leave alone.
	/// </summary>
	[SharpFunction(Name = "escapex", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["string", "omissions", "substitutions"])]
	public ValueTask<CallState> EscapeX(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = args["0"].Message.WithoutDataMarkup().ToPlainText();
		var omissions = args.TryGetValue("1", out var omitArg) ? omitArg.Message.ToPlainText() : string.Empty;
		var substitutions = args.TryGetValue("2", out var subArg)
			? subArg.Message.ToPlainText().Where(c => c is not (' ' or ',')).Select(char.ToLowerInvariant).ToHashSet()
			: [];
		if (text.Length == 0) return ValueTask.FromResult(CallState.Empty);

		var reverse = omissions.Contains('r');
		var substitutionKinds = new StringBuilder();
		foreach (var letter in omissions)
		{
			substitutionKinds.Append(letter switch
			{
				'a' => "cx",
				'8' => "<",
				's' => "btr",
				'd' => "#!@l",
				'p' => "npaso",
				'x' => string.Concat(substitutions),
				_ => string.Empty
			});
		}

		var omittedSubstitutions = substitutionKinds.ToString().ToHashSet();
		var omittedCharacters = omissions.Where(c => c is not ('f' or 'r' or 'a' or '8' or 's' or 'd' or 'p' or 'x')).ToHashSet();

		bool Omitted(int index)
			=> omittedCharacters.Contains(text[index])
				|| text[index] == '%' && index + 1 < text.Length && omittedSubstitutions.Contains(char.ToLowerInvariant(text[index + 1]));

		var escaped = EscapeSoftcode(text, index => Omitted(index) == reverse);
		return ValueTask.FromResult(new CallState(omissions.Contains('f') ? escaped : "\\" + escaped));
	}

	/// <summary>globalroom(): the master room, to a wizard or royalty.</summary>
	[SharpFunction(Name = "globalroom", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public async ValueTask<CallState> GlobalRoom(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await executor.IsPriv()
			? new CallState($"#{Configuration.CurrentValue.Database.MasterRoom}")
			: new CallState(ErrorMessages.Returns.PermissionDenied);
	}

	[SharpFunction(Name = "isalnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> IsAlnum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FirstCharacterIs(parser, char.IsAsciiLetterOrDigit);

	[SharpFunction(Name = "isalpha", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> IsAlpha(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FirstCharacterIs(parser, char.IsAsciiLetter);

	[SharpFunction(Name = "isdigit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> IsDigit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FirstCharacterIs(parser, char.IsAsciiDigit);

	[SharpFunction(Name = "islower", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> IsLower(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FirstCharacterIs(parser, char.IsAsciiLetterLower);

	[SharpFunction(Name = "isupper", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> IsUpper(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FirstCharacterIs(parser, char.IsAsciiLetterUpper);

	[SharpFunction(Name = "ispunct", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> IsPunct(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FirstCharacterIs(parser, c => c is > ' ' and < '\x7f' && !char.IsAsciiLetterOrDigit(c));

	[SharpFunction(Name = "isspace", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> IsSpace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FirstCharacterIs(parser, c => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r');

	[SharpFunction(Name = "isxdigit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string"])]
	public ValueTask<CallState> IsXDigit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FirstCharacterIs(parser, char.IsAsciiHexDigit);

	/// <summary>Whether the first character of the argument is an ASCII character of one class, as C's <c>ctype.h</c> tests are.</summary>
	private static ValueTask<CallState> FirstCharacterIs(IMUSHCodeParser parser, Func<char, bool> test)
		=> ValueTask.FromResult(new CallState(
			parser.CurrentState.Arguments["0"].Message.ToPlainText() is [var first, ..] && test(first)));

	[SharpFunction(Name = "cosh", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "angle-type"])]
	public ValueTask<CallState> Cosh(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> Hyperbolic(parser, Math.Cosh);

	[SharpFunction(Name = "sinh", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "angle-type"])]
	public ValueTask<CallState> Sinh(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> Hyperbolic(parser, Math.Sinh);

	[SharpFunction(Name = "tanh", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "angle-type"])]
	public ValueTask<CallState> Tanh(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> Hyperbolic(parser, Math.Tanh);

	private ValueTask<CallState> Hyperbolic(IMUSHCodeParser parser, Func<double, double> func)
	{
		var numbers = NumericEvaluation.For(parser);
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var typeArg) ? typeArg.Message.ToPlainText() : null;
		return ValueTask.FromResult(numbers.TryDouble(parser.CurrentState.Arguments["0"].Message.ToPlainText(), out var value)
			? AngleTypeMath(angleType, value, func)
			: new CallState(ErrorMessages.Returns.Number));
	}

	/// <summary>fbetween(): whether a number lies between two others, each rounded to six decimal places.</summary>
	[SharpFunction(Name = "fbetween", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["low", "high", "number", "inclusive"])]
	public ValueTask<CallState> FBetween(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var numbers = NumericEvaluation.For(parser);
		var args = parser.CurrentState.ArgumentsOrdered;
		if (!numbers.TryDouble(args["0"].Message.ToPlainText(), out var low)
			|| !numbers.TryDouble(args["1"].Message.ToPlainText(), out var high)
			|| !numbers.TryDouble(args["2"].Message.ToPlainText(), out var number))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		(low, high, number) = (Math.Round(low, 6), Math.Round(high, 6), Math.Round(number, 6));
		var inclusive = args.TryGetValue("3", out var inclusiveArg) && inclusiveArg.Message.ToPlainText().Trim() == "1";
		return ValueTask.FromResult(new CallState(inclusive
			? low <= number && number <= high
			: low < number && number < high));
	}

	/// <summary>move(): makes an object one controls go through an exit, or home, as if it typed the move.</summary>
	[SharpFunction(Name = "move", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		ParameterNames = ["object", "direction"])]
	public async ValueTask<CallState> Move(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var direction = parser.CurrentState.Arguments["1"].Message.ToPlainText().Trim();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			parser.CurrentState.Arguments["0"].Message.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (await PermissionService.Check(executor, Requires.ControlOf(target)) is PermissionRefusal refused)
				{
					return refused.Return;
				}

				var command = direction.Equals("home", StringComparison.OrdinalIgnoreCase)
					? "home"
					: $"goto {EscapeSoftcode(direction, _ => true)}";
				await parser.With(
					state => state with { Executor = target.Object().DBRef, Caller = state.Executor },
					async moving => await moving.CommandParse(MarkupText.Plain(command)));
				return CallState.Empty;
			});
	}
}
