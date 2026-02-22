using Humanizer;
using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
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
					? tmpCs.Message!
					: MModule.empty(),
			"B" or "b" => " ",
			"R" or "r" => "\n",
			"T" or "t" => "\t",
			"#" => $"#{parser.CurrentState.Enactor!.Value.Number}",
			":" => $"#{parser.CurrentState.Enactor!.Value}",
			"n" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name,
			"N" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name.ApplyCase(LetterCasing.Sentence),
			"~" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name, // Accented name - using regular name as fallback
			"K" or "k" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name, // Moniker - using regular name as fallback
			"S" or "s" =>
				await AttributeHelpers.GetPronoun(attributeService, mediator, parser,
					await parser.CurrentState.KnownExecutorObject(mediator),
					configuration.CurrentValue.Attribute.GenderAttribute,
					configuration.CurrentValue.Attribute.SubjectivePronounAttribute,
					x => x switch
					{
						"M" or "Male" => "he",
						"F" or "Female" => "she",
						_ => "they"
					}),
			"O" or "o" => await AttributeHelpers.GetPronoun(attributeService, mediator, parser,
				await parser.CurrentState.KnownExecutorObject(mediator),
				configuration.CurrentValue.Attribute.GenderAttribute,
				configuration.CurrentValue.Attribute.ObjectivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "him",
					"F" or "Female" => "her",
					_ => "them"
				}),
			"P" or "p" => await AttributeHelpers.GetPronoun(attributeService, mediator, parser,
				await parser.CurrentState.KnownExecutorObject(mediator),
				configuration.CurrentValue.Attribute.GenderAttribute,
				configuration.CurrentValue.Attribute.PossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "her",
					_ => "their"
				}),
			"A" or "a" => await AttributeHelpers.GetPronoun(attributeService, mediator, parser,
				await parser.CurrentState.KnownExecutorObject(mediator),
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
			"L" or "l" => await GetLocationDbRefString(parser, mediator),
			"C" or "c" => LastCommandBeforeEvaluation(parser), // Last command before evaluation - using same implementation
			"U" or "u" => LastCommandBeforeEvaluation(parser), // Last command after evaluation - using same implementation
			"?" => parser.State.Count().ToString(),
			"+" => parser.CurrentState.EnvironmentRegisters.Count.ToString(),
			_ => symbol,
		};

	public static MString LastCommandBeforeEvaluation(IMUSHCodeParser parser) =>
		MModule.single(parser.StateHistory(2).Match(
			state => state.Command,
			_ => string.Empty));

	private static async ValueTask<string> GetLocationDbRefString(IMUSHCodeParser parser, IMediator mediator)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var location = await executor.Where();
		var locationDbRef = location.Object().DBRef.Number.ToString();
		return $"#{locationDbRef}";
	}

	public static async ValueTask<CallState> ParseComplexSubstitution(CallState? symbol, IMUSHCodeParser parser,
		IAttributeService attributeService, IMediator mediator,
		ComplexSubstitutionSymbolContext context)
	{
		ArgumentNullException.ThrowIfNull(symbol);

		if (context.REG_NUM() is not null) return HandleRegistrySymbol(symbol, parser);
		if (context.ITEXT_NUM() is not null) return HandleITextNumber(symbol, parser);
		if (context.ITEXT_LAST() is not null) return HandleITextTop(symbol, parser);
		if (context.STEXT_NUM() is not null) return HandleSTextNumber(symbol, parser);
		if (context.STEXT_LAST() is not null) return HandleSTextLast(parser);
		if (context.VWX() is not null) return await HandleVWX(symbol, parser, mediator, attributeService);
		return HandleRegistrySymbol(symbol, parser);
	}

	private static CallState HandleITextTop(CallState symbol, IMUSHCodeParser parser) =>
		$"{(parser.CurrentState.IterationRegisters.TryPeek(out var result)
			? result.Value
			: MModule.single(Errors.ErrorRegisterRange))}";

	private static CallState HandleRegistrySymbol(CallState symbol, IMUSHCodeParser parser)
	{
		parser.CurrentState.Registers.TryPeek(out var curVal);
		return curVal!.TryGetValue(MModule.plainText(symbol.Message).ToUpper(), out var value)
			? new CallState(value)
			: new CallState(string.Empty);
	}

	// Symbol Example: %vw --> vw
	private static async ValueTask<CallState> HandleVWX(CallState symbol, IMUSHCodeParser parser, IMediator mediator,
		IAttributeService attributeService)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		var val = await attributeService.GetAttributeAsync(
			executor,
			executor,
			symbol.Message!.ToString(),
			IAttributeService.AttributeMode.Read);

		return val.Match(
			attr => new CallState(attr.Last().Value),
			_ => new CallState(string.Empty),
			_ => new CallState(string.Empty)
		);
	}

	// Symbol Example: %$0 --> 0
	private static CallState HandleSTextNumber(CallState symbol, IMUSHCodeParser parser)
	{
		var symbolValue = symbol.Message!.ToString();
		var stack = parser.CurrentState.SwitchStack;

		// Parse the symbol number
		if (!int.TryParse(symbolValue, out var symbolNumber) || symbolNumber < 0)
		{
			return new CallState("#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER");
		}

		// Check if we're in a switch context or if the depth is out of range
		if (stack.Count == 0 || symbolNumber >= stack.Count)
		{
			return new CallState(Errors.ErrorRegisterRange);
		}

		// Get the nth item from the stack (0 is top/current)
		var item = stack.ElementAtOrDefault(symbolNumber);
		return new CallState(item ?? MModule.empty());
	}

	// Symbol: %$L --> Last/outermost switch string
	private static CallState HandleSTextLast(IMUSHCodeParser parser)
	{
		var stack = parser.CurrentState.SwitchStack;

		// Check if we're in a switch context
		if (stack.Count == 0)
		{
			return new CallState(Errors.ErrorRegisterRange);
		}

		// Get the outermost (last) item from the stack
		var depth = stack.Count - 1;
		var item = stack.ElementAtOrDefault(depth);
		return new CallState(item ?? MModule.empty());
	}

	// Symbol Example: %i0 --> 0
	private static CallState HandleITextNumber(CallState symbol, IMUSHCodeParser parser)
	{
		var symbolValue = symbol.Message!.ToString();
		var symbolNumber = int.Parse(symbolValue);
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (maxCount <= symbolNumber)
		{
			return new CallState(Errors.ErrorRegisterRange);
		}

		var val = parser.CurrentState.IterationRegisters.ElementAt(symbolNumber).Value;

		return new CallState(val);
	}
}