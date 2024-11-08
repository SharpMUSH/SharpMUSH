using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Substitutions;

public static partial class Substitutions
{
	public static CallState ParseSimpleSubstitution(string symbol, IMUSHCodeParser parser, SubstitutionSymbolContext _)
		=> symbol switch
		{
			"0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" => new((parser.CurrentState.Arguments.TryGetValue(symbol, out var tmpCS) ? tmpCS.Message : MModule.empty())),
			"B" or "b" => new(" "),
			"R" or "r" => new(Environment.NewLine),
			"T" or "t" => new("\t"),
			"#" => new($"#{parser.CurrentState.Enactor!.Value.Number}"),
			":" => new($"#{parser.CurrentState.Enactor!.Value}"),
			"n" => new(parser.CurrentState.EnactorObject(parser.Database).Object()!.Name),
			"N" => new(parser.CurrentState.EnactorObject(parser.Database).Object()!.Name), // TODO: CAPPED ENACTOR NAME
			"~" => new(parser.CurrentState.EnactorObject(parser.Database).Object()!.Name), // TODO: ACCENTED ENACTOR NAME
			"K" or "k" => new(parser.CurrentState.EnactorObject(parser.Database).Object()!.Name), // TODO: MONIKER ENACTOR NAME
			"S" or "s" => new CallState("they"), // TODO: SUBJECT PRONOUN
			"O" or "o" => throw new NotImplementedException(), // TODO: OBJECT PRONOUN
			"P" or "p" => throw new NotImplementedException(), // TODO: POSSESSIVE PRONOUN
			"A" or "a" => throw new NotImplementedException(), // TODO: ABSOLUTE POSSESSIVE PRONOUN
			"@" => new($"#{parser.CurrentState.Caller!.Value.Number}"),
			"!" => new($"#{parser.CurrentState.Executor!.Value.Number}"),
			"L" or "l" => new(parser.CurrentState.ExecutorObject(parser.Database).WithoutNone().Where.Object().DBRef.ToString()), // TODO: LOCATION OF EXECUTOR
			"C" or "c" => throw new NotImplementedException(), // TODO: LAST COMMAND BEFORE EVALUATION
			"U" or "u" => throw new NotImplementedException(), // TODO: LAST COMMAND AFTER EVALUATION
			"?" => new(parser.State.Count().ToString()),
			"+" => new(parser.CurrentState.Arguments.Count.ToString()),
			_ => new(symbol),
		};

	public static CallState ParseComplexSubstitution(CallState? symbol, IMUSHCodeParser parser,
		ComplexSubstitutionSymbolContext context)
	{
		ArgumentNullException.ThrowIfNull(symbol);

		if (context.REG_NUM() is not null) return HandleRegistrySymbol(symbol, parser);
		if (context.ITEXT_NUM() is not null) return HandleITextNumber(symbol, parser);
		if (context.STEXT_NUM() is not null) return HandleSTextNumber(symbol, parser);
		if (context.VWX() is not null) return HandleVWX(symbol, parser);
		return HandleRegistrySymbol(symbol, parser);
	}

	private static CallState HandleRegistrySymbol(CallState symbol, IMUSHCodeParser parser)
		=> parser.CurrentState.Registers.Peek().TryGetValue(MModule.plainText(symbol.Message).ToUpper(), out var value)
			? new CallState(value)
			: new CallState(string.Empty);

	// Symbol Example: %vw --> vw
	private static CallState HandleVWX(CallState symbol, IMUSHCodeParser parser)
	{
		var db = parser.Database;
		var attrService = parser.AttributeService;
		var executor = parser.CurrentState.ExecutorObject(db).Known();

		var val = attrService.GetAttributeAsync(executor, executor, symbol.Message!.ToString(),
			IAttributeService.AttributeMode.Read, true).AsTask().Result;

		return val.Match(
			attr => new CallState(attr.Value),
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
			// TODO: Investigate visits from DoListComplex5 getting here.
			return new CallState(Errors.ErrorRange); // TODO: Fix Value
		}

		var val = parser.CurrentState.IterationRegisters.ElementAt(symbolNumber).Value;

		return new CallState(val);
	}
}