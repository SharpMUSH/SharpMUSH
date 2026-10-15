using Humanizer;
using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Substitutions;

public static partial class Substitutions
{
	public static async ValueTask<CallState> ParseSimpleSubstitution(string symbol, IMUSHCodeParser parser,
		IMediator mediator,
		IAttributeService attributeService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		SubstitutionSymbolContext _)
		=> symbol switch
		{
			"0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" =>
				parser.CurrentState.EnvironmentRegisters.TryGetValue(symbol, out var tmpCs)
					? tmpCs.Message
					: MarkupText.Empty,
			"B" or "b" => " ",
			"R" or "r" => "\n",
			"T" or "t" => "\t",
			"#" => $"#{parser.CurrentState.Enactor!.Value.Number}",
			":" => parser.CurrentState.Enactor!.Value.ToString(),
			// Both cases return the plain name; the caller (VisitValidSubstitution) capitalizes the
			// first character for the uppercase selector %N. Doing it there rather than with
			// ApplyCase(Sentence) matches PennMUSH, which upper-cases only the first character and
			// leaves the rest alone — "McDonald" stays "McDonald", not "Mcdonald".
			"n" or "N" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name,
			"~" => await new SharpMUSH.Library.Services.NameFormatter(attributeService, configuration).FormatAsync(
				await parser.CurrentState.KnownEnactorObject(mediator), SharpMUSH.Library.Services.NameContext.Accented),
			"K" or "k" => await new SharpMUSH.Library.Services.NameFormatter(attributeService, configuration).FormatAsync(
				await parser.CurrentState.KnownEnactorObject(mediator), SharpMUSH.Library.Services.NameContext.UnaccentedMoniker),
			"S" or "s" =>
				await AttributeHelpers.GetPronoun(attributeService, mediator, parser,
					await parser.CurrentState.KnownEnactorObject(mediator),
					configuration.CurrentValue.Attribute.GenderAttribute,
					configuration.CurrentValue.Attribute.SubjectivePronounAttribute,
					x => x switch
					{
						"M" or "Male" => "he",
						"F" or "Female" => "she",
						_ => "they"
					}),
			"O" or "o" => await AttributeHelpers.GetPronoun(attributeService, mediator, parser,
				await parser.CurrentState.KnownEnactorObject(mediator),
				configuration.CurrentValue.Attribute.GenderAttribute,
				configuration.CurrentValue.Attribute.ObjectivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "him",
					"F" or "Female" => "her",
					_ => "them"
				}),
			"P" or "p" => await AttributeHelpers.GetPronoun(attributeService, mediator, parser,
				await parser.CurrentState.KnownEnactorObject(mediator),
				configuration.CurrentValue.Attribute.GenderAttribute,
				configuration.CurrentValue.Attribute.PossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "her",
					_ => "their"
				}),
			"A" or "a" => await AttributeHelpers.GetPronoun(attributeService, mediator, parser,
				await parser.CurrentState.KnownEnactorObject(mediator),
				configuration.CurrentValue.Attribute.GenderAttribute,
				configuration.CurrentValue.Attribute.AbsolutePossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "hers",
					_ => "theirs"
				}),
			"@" => $"#{parser.CurrentState.Caller!.Value.Number}",
			"!" => $"#{parser.CurrentState.Executor!.Value.Number}",
			// %j: the connection the command came in on. %d is RhostMUSH's @dolist element; no MUSH
			// server gives %j a meaning.
			"j" or "J" => parser.CurrentState.Handle?.ToString() ?? string.Empty,
			"L" or "l" => await GetLocationDbRefString(parser, mediator),
			// %m is TinyMUX's name for the same thing; PennMUSH has no %m and prints m.
			"C" or "c" or "M" or "m" => CommandBeforeEvaluation(parser),
			"U" or "u" => CommandAfterEvaluation(parser),
			// %>: the logical output of the last command run in this queue entry (help command output).
			// PennMUSH has no %> and prints a literal >.
			">" => parser.CurrentState.PipedOutput,
			// %|: what the command before a ;| printed (help piping), TinyMUX's piped output. PennMUSH has
			// no %| and prints a literal |.
			"|" => parser.CurrentState.PrintedOutput,
			// PennMUSH: %? returns "invocations recursions" (two space-separated numbers)
			"?" => $"{parser.CurrentState.TotalInvocations!.Count} {parser.CurrentState.CallDepth!.Count}",
			// PennMUSH's %+ is the argument count, not the register count: pi_regs_get_envc
			// (src/parse.c:1783-1813) takes the highest numeric argument index plus one and skips
			// the rest outright — "only check numeric args, ignore named ones". Named registers
			// share this dictionary with %0-%N: a regexp $-command's capture groups land here
			// (PatternArguments.cs:19-20), as do a mapsql callback's column names, so counting
			// entries over-reported both. A key counts as a position when int.TryParse accepts it,
			// which is the same test ParserState.ArgumentsOrdered applies; Penn's sscanf("%d") also
			// takes a leading-integer prefix, which nothing here produces. Negative indices are
			// ignored, as they are in Penn, where the running maximum starts at zero.
			"+" => (parser.CurrentState.EnvironmentRegisters.Keys
				.Select(key => int.TryParse(key, out var index) ? index : -1)
				.DefaultIfEmpty(-1)
				.Max() + 1).ToString(),
			// PennMUSH emits "% " (percent then space) literally "for more natural typing"
			// (src/parse.c), unlike other unknown substitutions where the percent is dropped
			// (%z -> z). The literal space arrives here as the OTHER_SUB catch-all.
			" " => "% ",
			_ => symbol,
		};

	/// <summary><c>%c</c>: the running command as written (parse.c, <c>pe_info-&gt;cmd_raw</c>).</summary>
	public static MString CommandBeforeEvaluation(IMUSHCodeParser parser) =>
		parser.CurrentState.CommandText?.Raw ?? MarkupText.Empty;

	/// <summary><c>%u</c>: the last parsed command, rebuilt from its evaluated arguments (<c>cmd_evaled</c>).</summary>
	public static MString CommandAfterEvaluation(IMUSHCodeParser parser) =>
		parser.CurrentState.CommandText?.Evaluated ?? MarkupText.Empty;

	private static async ValueTask<string> GetLocationDbRefString(IMUSHCodeParser parser, IMediator mediator)
	{
		var enactor = await parser.CurrentState.KnownEnactorObject(mediator);
		var location = await enactor.Where();
		return $"#{location.Object().DBRef.Number}";
	}

	public static async ValueTask<CallState> ParseComplexSubstitution(CallState? symbol, IMUSHCodeParser parser,
		IAttributeService attributeService, IMediator mediator,
		IComplexSubstitutionSymbolContext context)
	{
		ArgumentNullException.ThrowIfNull(symbol);

		if (context.REG_NUM() is not null) return HandleRegistrySymbol(symbol, parser);
		if (context.REG_ALPHA() is not null) return HandleRegistrySymbol(symbol, parser);
		if (context.ITEXT_NUM() is not null) return HandleITextNumber(symbol, parser);
		if (context.ITEXT_LAST() is not null) return HandleITextOutermost(symbol, parser);
		if (context.DTEXT_NUM() is not null) return HandleDoListNumber(symbol, parser);
		if (context.DTEXT_LAST() is not null) return HandleDoListOutermost(parser);
		if (context.STEXT_NUM() is not null) return HandleSTextNumber(symbol, parser);
		if (context.STEXT_LAST() is not null) return HandleSTextLast(parser);
		if (context.VWX() is not null) return await HandleVWX(symbol, parser, mediator, attributeService);
		if (context.ARG_STARTCARET() is not null) return new CallState(parser.CurrentState.Argument(symbol.Message.ToPlainText()));
		if (context.ATTR_STARTCARET() is not null) return await HandleAttributeOrArgument(symbol, parser, mediator, attributeService);
		return HandleRegistrySymbol(symbol, parser);
	}

	/// <summary>
	/// <c>%iL</c> — the OUTERMOST iteration, matching <c>itext(L)</c> and <c>itext(ilev())</c>
	/// (<c>help ITEXT()</c>). <c>##</c> also names the outermost loop, but only one started by the
	/// same evaluation (<c>VisitIterationToken</c>).
	///
	/// <para>The register is returned as-is, the way <c>%i&lt;n&gt;</c> does; interpolating it into a
	/// string flattens the <c>MString</c> and bakes any colour into the plain text.</para>
	/// </summary>
	private static CallState HandleITextOutermost(CallState symbol, IMUSHCodeParser parser) =>
		parser.CurrentState.IterationRegisters.IsEmpty
			? new CallState(ErrorMessages.Returns.RegisterRange)
			: new CallState(parser.CurrentState.IterationRegisters.Last().Value);

	/// <summary>
	/// <c>%d&lt;n&gt;</c>, from RhostMUSH: the element of the n-th <c>@dolist</c> out from the innermost,
	/// counting only <c>@dolist</c> levels. <c>%i&lt;n&gt;</c> counts <c>iter()</c> levels as well, so in
	/// <c>@dolist a=think iter(x,%d0:%i0)</c> the two name different loops.
	/// </summary>
	private static CallState HandleDoListNumber(CallState symbol, IMUSHCodeParser parser) =>
		ReadLevel(symbol, parser,
			level => parser.CurrentState.IterationRegisters.Where(frame => frame.DoList).ElementAtOrDefault(level));

	/// <summary><c>%dL</c>: the element of the outermost <c>@dolist</c>.</summary>
	private static CallState HandleDoListOutermost(IMUSHCodeParser parser) =>
		parser.CurrentState.IterationRegisters.LastOrDefault(frame => frame.DoList) is { } frame
			? new CallState(frame.Value)
			: new CallState(ErrorMessages.Returns.RegisterRange);

	/// <summary>
	/// <c>%q&lt;name&gt;</c> and the rest. A bang before the name (<c>%q&lt;!!$name&gt;</c>) answers 1 or 0
	/// about the register (<see cref="Bang"/>); no register name starts with one.
	/// </summary>
	private static CallState HandleRegistrySymbol(CallState symbol, IMUSHCodeParser parser)
	{
		var name = symbol.Message.ToPlainText();
		var bangLength = Bang.Read(name, out var bang);
		parser.CurrentState.Registers.TryPeek(out var curVal);
		var value = curVal!.TryGetValue(name[bangLength..].ToUpper(), out var found) ? found : MarkupText.Empty;
		return new CallState(bangLength > 0 ? bang.Apply(value, parser) : value);
	}

	/// <summary>
	/// <c>%i&lt;n&gt;</c> or <c>%d&lt;n&gt;</c>, with any bang before the number applied to the element.
	/// An out-of-range level is the error, whatever the bang.
	/// </summary>
	private static CallState ReadLevel(CallState symbol, IMUSHCodeParser parser,
		Func<int, IterationWrapper<MString>?> frameAt)
	{
		var text = symbol.Message.ToPlainText();
		var bangLength = Bang.Read(text, out var bang);
		return (int.TryParse(text.AsSpan(bangLength), out var level) ? frameAt(level) : null) switch
		{
			null => new CallState(ErrorMessages.Returns.RegisterRange),
			var frame when bangLength > 0 => new CallState(bang.Apply(frame.Value, parser)),
			var frame => new CallState(frame.Value)
		};
	}

	// Symbol Example: %vw --> vw
	private static async ValueTask<CallState> HandleVWX(CallState symbol, IMUSHCodeParser parser, IMediator mediator,
		IAttributeService attributeService)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		var val = await attributeService.GetAttributeAsync(
			executor,
			executor,
			symbol.Message.ToPlainText(),
			IAttributeService.AttributeMode.Read);

		return val is SharpAttribute[] attr
			? new CallState(attr.Last().Value)
			: new CallState(string.Empty);
	}

	/// <summary>
	/// <c>%=&lt;name&gt;</c>, from TinyMUX: argument <c>name</c> when it starts with a digit
	/// (<c>%=&lt;12&gt;</c> is <c>%&lt;12&gt;</c>), otherwise the executor's attribute, as <c>v(name)</c>.
	/// </summary>
	private static async ValueTask<CallState> HandleAttributeOrArgument(CallState symbol, IMUSHCodeParser parser,
		IMediator mediator, IAttributeService attributeService)
	{
		var name = symbol.Message.ToPlainText();
		return name.Length > 0 && char.IsAsciiDigit(name[0])
			? new CallState(parser.CurrentState.Argument(name))
			: await HandleVWX(symbol, parser, mediator, attributeService);
	}

	// Symbol Example: %$0 --> 0
	private static CallState HandleSTextNumber(CallState symbol, IMUSHCodeParser parser)
	{
		var symbolValue = symbol.Message.ToPlainText();
		var stack = parser.CurrentState.SwitchStack;

		if (!int.TryParse(symbolValue, out var symbolNumber) || symbolNumber < 0)
		{
			return new CallState(ErrorMessages.Returns.NonNegativeInteger);
		}

		if (stack.Count == 0 || symbolNumber >= stack.Count)
		{
			return new CallState(ErrorMessages.Returns.RegisterRange);
		}

		// Get the nth item from the stack (0 is top/current)
		var item = stack.ElementAtOrDefault(symbolNumber);
		return new CallState(item?.Text ?? MarkupText.Empty);
	}

	// Symbol: %$L --> Last/outermost switch string
	private static CallState HandleSTextLast(IMUSHCodeParser parser)
	{
		var stack = parser.CurrentState.SwitchStack;

		if (stack.Count == 0)
		{
			return new CallState(ErrorMessages.Returns.RegisterRange);
		}

		var depth = stack.Count - 1;
		var item = stack.ElementAtOrDefault(depth);
		return new CallState(item?.Text ?? MarkupText.Empty);
	}

	// Symbol Example: %i0 --> 0
	private static CallState HandleITextNumber(CallState symbol, IMUSHCodeParser parser) =>
		ReadLevel(symbol, parser, level => parser.CurrentState.IterationRegisters.ElementAtOrDefault(level));
}
