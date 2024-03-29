﻿using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Substitutions
{
	public static partial class Substitutions
	{
		public static CallState ParseSimpleSubstitution(string symbol, Parser parser, SubstitutionSymbolContext _)
			=> symbol switch
			{
				"0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" => new CallState((parser.CurrentState.Arguments.ElementAtOrDefault(int.Parse(symbol))?.Message) ?? MModule.empty()),
				"B" or "b" => new CallState(" "),
				"R" or "r" => new CallState(Environment.NewLine),
				"T" or "t" => new CallState("\t"),
				"#" => new CallState($"#{parser.CurrentState.Enactor!.Value.Number}"),
				":" => new CallState($"#{parser.CurrentState.Enactor!.Value.Number}:{parser.CurrentState.Enactor!.Value.CreationMilliseconds}"),
				"n" => throw new NotImplementedException(),// ENACTOR NAME
				"N" => throw new NotImplementedException(),// CAPPED ENACTOR NAME
				"~" => throw new NotImplementedException(),// ACCENTED ENACTOR NAME
				"K" or "k" => throw new NotImplementedException(),// MONIKER ENACTOR NAME
				"S" or "s" => throw new NotImplementedException(),// SUBJECT PRONOUN
				"O" or "o" => throw new NotImplementedException(),// OBJECT PRONOUN
				"P" or "p" => throw new NotImplementedException(),// POSSESSIVE PRONOUN
				"A" or "a" => throw new NotImplementedException(),// ABSOLUTE POSSESSIVE PRONOUN
				"@" => new CallState($"#{parser.CurrentState.Caller!.Value.Number}"),
				"!" => new CallState($"#{parser.CurrentState.Executor!.Value.Number}"),
				"L" or "l" => throw new NotImplementedException(),// LOCATION OF EXECUTOR
				"C" or "c" => throw new NotImplementedException(),// LAST COMMAND BEFORE EVALUATION
				"U" or "u" => throw new NotImplementedException(),// LAST COMMAND AFTER EVALUATION
				"?" => new CallState(parser.State.Count().ToString()),
				"+" => new CallState(parser.CurrentState.Arguments.Count.ToString()),
				_ => new CallState(symbol),
			};

		public static CallState ParseComplexSubstitution(string symbol, Parser parser, ComplexSubstitutionSymbolContext context)
		{
			throw new NotImplementedException();
		}
	}
}
