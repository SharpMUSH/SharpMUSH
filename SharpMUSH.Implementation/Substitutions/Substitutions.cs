using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Substitutions;

public static partial class Substitutions
{
	public static CallState ParseSimpleSubstitution(string symbol, IMUSHCodeParser parser, SubstitutionSymbolContext _)
		=> symbol switch
		{
			"0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" => new((parser.CurrentState.Arguments.ElementAtOrDefault(int.Parse(symbol))?.Message) ?? MModule.empty()),
			"B" or "b" => new(" "),
			"R" or "r" => new(Environment.NewLine),
			"T" or "t" => new("\t"),
			"#" => new($"#{parser.CurrentState.Enactor!.Value.Number}"),
			":" => new($"#{parser.CurrentState.Enactor!.Value.Number}:{parser.CurrentState.Enactor!.Value.CreationMilliseconds}"),
			"n" => new(parser.CurrentState.Enactor!.Value.Get(parser.Database).Object()!.Name),
			"N" => new(parser.CurrentState.Enactor!.Value.Get(parser.Database).Object()!.Name),// TODO: CAPPED ENACTOR NAME
			"~" => throw new NotImplementedException(),// TODO: ACCENTED ENACTOR NAME
			"K" or "k" => throw new NotImplementedException(),// TODO: MONIKER ENACTOR NAME
			"S" or "s" => throw new NotImplementedException(),// TODO: SUBJECT PRONOUN
			"O" or "o" => throw new NotImplementedException(),// TODO: OBJECT PRONOUN
			"P" or "p" => throw new NotImplementedException(),// TODO: POSSESSIVE PRONOUN
			"A" or "a" => throw new NotImplementedException(),// TODO: ABSOLUTE POSSESSIVE PRONOUN
			"@" => new($"#{parser.CurrentState.Caller!.Value.Number}"),
			"!" => new($"#{parser.CurrentState.Executor!.Value.Number}"),
			"L" or "l" => new($"#{parser.CurrentState.Executor!.Value.GetLocation(parser.Database).Object()!.DBRef}"),// TODO: LOCATION OF EXECUTOR
			"C" or "c" => throw new NotImplementedException(),// TODO: LAST COMMAND BEFORE EVALUATION
			"U" or "u" => throw new NotImplementedException(),// TODO: LAST COMMAND AFTER EVALUATION
			"?" => new(parser.State.Count().ToString()),
			"+" => new(parser.CurrentState.Arguments.Count.ToString()),
			_ => new(symbol),
		};

	public static CallState ParseComplexSubstitution(CallState? symbol, IMUSHCodeParser parser, ComplexSubstitutionSymbolContext context)
	{
		ArgumentNullException.ThrowIfNull(symbol);

		if (context.REG_NUM() is not null) return HandleRegistrySymbol(symbol, parser);
		else if (context.ITEXT_NUM() is not null) return HandleITextNumber(symbol, parser);
		else if (context.STEXT_NUM() is not null) return HandleSTextNumber(symbol, parser);
		else if (context.VWX() is not null) return HandleVWX(symbol, parser);
		else return HandleRegistrySymbol(symbol, parser);
	}

	private static CallState HandleRegistrySymbol(CallState symbol, IMUSHCodeParser parser)
		=> parser.CurrentState.Registers.Peek().TryGetValue(MModule.plainText(symbol.Message).ToUpper(), out var value)
				? new CallState(value)
				: new CallState(string.Empty);

	private static CallState HandleVWX(CallState symbol, IMUSHCodeParser parser)
	{
		// Symbol Example: %vw --> vw
		return symbol;
		throw new NotImplementedException();
	}

	private static CallState HandleSTextNumber(CallState symbol, IMUSHCodeParser parser)
	{
		// Symbol Example: %qi0 --> qi0
		return symbol;
		throw new NotImplementedException();
	}

	private static CallState HandleITextNumber(CallState symbol, IMUSHCodeParser parser)
	{
		// Symbol Example: %q$0 --> q$0
		return symbol;
		throw new NotImplementedException();
	}
}
