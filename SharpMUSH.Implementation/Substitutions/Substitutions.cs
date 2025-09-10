using Mediator;
using Humanizer;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
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
		IOptionsMonitor<PennMUSHOptions> configuration,
		SubstitutionSymbolContext _)
		=> symbol switch
		{
			"0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" => 
				parser.CurrentState.Arguments.TryGetValue(symbol, out var tmpCS) 
					? tmpCS.Message 
					: MModule.empty(),
			"B" or "b" => " ",
			"R" or "r" => Environment.NewLine,
			"T" or "t" => "\t",
			"#" => $"#{parser.CurrentState.Enactor!.Value.Number}",
			":" => $"#{parser.CurrentState.Enactor!.Value}",
			"n" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name,
			"N" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name.ApplyCase(LetterCasing.Sentence),
			"~" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name, // TODO: ACCENTED ENACTOR NAME
			"K" or "k" => (await parser.CurrentState.EnactorObject(mediator)).Object()!.Name, // TODO: MONIKER ENACTOR NAME
			"S" or "s" => 
				await GetGenderIndicatingAttribute(attributeService, mediator, parser,
						configuration.CurrentValue.Attribute.SubjectivePronounAttribute ?? "SEX") switch
				{
					"M" or "Male" => "he",
					"F" or "Female" => "she",
					_ => "they"
				}, // TODO: SUBJECT PRONOUN CUSTOMIZATION
			"O" or "o" => 
				await GetGenderIndicatingAttribute(attributeService, mediator, parser,
						configuration.CurrentValue.Attribute.ObjectivePronounAttribute ?? "SEX") switch
					{
						"M" or "Male" => "him",
						"F" or "Female" => "her",
						_ => "their"
					}, // TODO: OBJECT PRONOUN CUSTOMIZATION
			"P" or "p" => 
				await GetGenderIndicatingAttribute(attributeService, mediator, parser,
						configuration.CurrentValue.Attribute.PossessivePronounAttribute ?? "SEX") switch
					{
						"M" or "Male" => "his",
						"F" or "Female" => "her",
						_ => "their"
					}, // TODO: POSSESSIVE PRONOUN CUSTOMIZATION
			"A" or "a" => 
				await GetGenderIndicatingAttribute(attributeService, mediator, parser,
						configuration.CurrentValue.Attribute.AbsolutePossessivePronounAttribute ?? "SEX") switch
					{
						"M" or "Male" => "his",
						"F" or "Female" => "hers",
						_ => "theirs"
					}, // TODO: ABSOLUTE POSSESSIVE PRONOUN CUSTOMIZATION
			"@" => $"#{parser.CurrentState.Caller!.Value.Number}",
			"!" => $"#{parser.CurrentState.Executor!.Value.Number}",
			"L" or "l" => await GetLocationDBRefString(parser, mediator),
			"C" or "c" => LastCommandBeforeEvaluation(parser), // TODO: LAST COMMAND BEFORE EVALUATION
			"U" or "u" => LastCommandBeforeEvaluation(parser), // TODO: LAST COMMAND AFTER EVALUATION
			"?" => parser.State.Count().ToString(),
			"+" => parser.CurrentState.Arguments.Count.ToString(),
			_ => symbol,
		};

	private static MString LastCommandBeforeEvaluation(IMUSHCodeParser parser)
	{
		try
		{
			var getLast = parser.State.ElementAt(-1).Command;
			return MModule.single(getLast);
		}
		catch
		{
			return MModule.single(parser.State.Peek().Command ?? "");
		}
	}

	private static async ValueTask<string> GetGenderIndicatingAttribute(IAttributeService attributeService,
		IMediator mediator, IMUSHCodeParser parser, string attr)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		
		var attribute = await attributeService.GetAttributeAsync(
			executor,
			executor,
			attr,
			IAttributeService.AttributeMode.Read); 

		return attribute.IsAttribute 
			? attribute.AsAttribute.Last().Value.ToPlainText() 
			: "N";
	}

	private static async ValueTask<string> GetLocationDBRefString(IMUSHCodeParser parser, IMediator mediator)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var location = await executor.Where();
		var locationDBRef = location.Object().DBRef.Number.ToString();
		return $"#{locationDBRef}";
	}

	public static async ValueTask<CallState> ParseComplexSubstitution(CallState? symbol, IMUSHCodeParser parser,
		IAttributeService attributeService, IMediator mediator,
		ComplexSubstitutionSymbolContext context)
	{
		ArgumentNullException.ThrowIfNull(symbol);

		if (context.REG_NUM() is not null) return HandleRegistrySymbol(symbol, parser);
		if (context.ITEXT_NUM() is not null) return HandleITextNumber(symbol, parser);
		if (context.STEXT_NUM() is not null) return HandleSTextNumber(symbol, parser);
		if (context.VWX() is not null) return await HandleVWX(symbol, parser, mediator, attributeService);
		return HandleRegistrySymbol(symbol, parser);
	}

	private static CallState HandleRegistrySymbol(CallState symbol, IMUSHCodeParser parser)
	{
		parser.CurrentState.Registers.TryPeek(out var curVal);
		return curVal!.TryGetValue(MModule.plainText(symbol.Message).ToUpper(), out var value)
			? new CallState(value)
			: new CallState(string.Empty);
	}

	// Symbol Example: %vw --> vw
	private static async ValueTask<CallState> HandleVWX(CallState symbol, IMUSHCodeParser parser, IMediator mediator, IAttributeService attributeService)
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
		var symbolNumber = int.Parse(symbolValue);
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (maxCount <= symbolNumber)
		{
			return new CallState(Errors.ErrorRange); // TODO: Fix Value
		}

		var val = parser.CurrentState.IterationRegisters.ToArray().ElementAt(maxCount - symbolNumber - 1).Iteration;

		return new CallState(val.ToString());
	}

	// Symbol Example: %i0 --> 0
	private static CallState HandleITextNumber(CallState symbol, IMUSHCodeParser parser)
	{
		var symbolValue = symbol.Message!.ToString();
		var symbolNumber = int.Parse(symbolValue);
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (maxCount <= symbolNumber)
		{
			return new CallState(Errors.ErrorRange); // TODO: Fix Value
		}

		var val = parser.CurrentState.IterationRegisters.ElementAt(symbolNumber).Value;

		return new CallState(val);
	}
}