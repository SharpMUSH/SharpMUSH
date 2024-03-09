using Antlr4.Runtime;
using System.Runtime.InteropServices;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Substitutions
{
	public static partial class Substitutions
	{
		public static CallState ParseSimpleSubstitution(string symbol, Parser parser, SubstitutionSymbolContext context)
		{
			var contextValue = context.GetText();
			switch (contextValue)
			{
				case "0":
				case "1":
				case "2":
				case "3":
				case "4":
				case "5":
				case "6":
				case "7":
				case "8":
				case "9":
					return new CallState((parser.State.Peek().Arguments.ElementAtOrDefault(int.Parse(contextValue))?.Message) ?? MModule.empty());
				case "B":
				case "b":
					return new CallState(" ");
				case "R":
				case "r":
					return new CallState(Environment.NewLine);
				case "T":
				case "t":
					return new CallState("\t");
				case "#":
					return new CallState($"#{parser.State.Peek().Enactor.Number}");
				case "n":
					// ENACTOR NAME
					throw new NotImplementedException();
				case "N":
					// CAPPED ENACTOR NAME
					throw new NotImplementedException();
				case "~":
					// ACCENTED ENACTOR NAME
					throw new NotImplementedException();
				case "K":
				case "k":
					// MONIKER ENACTOR NAME
					throw new NotImplementedException();
				case "S":
				case "s":
					// SUBJECT PRONOUN
					throw new NotImplementedException();
				case "O":
				case "o":
					// OBJECT PRONOUN
					throw new NotImplementedException();
				case "P":
				case "p":
					// POSSESSIVE PRONOUN
					throw new NotImplementedException();
				case "A":
				case "a":
					// ABSOLUTE POSSESSIVE PRONOUN
					throw new NotImplementedException();
				case "@":
					return new CallState($"#{parser.State.Peek().Caller.Number}");
				case "!":
					return new CallState($"#{parser.State.Peek().Executor.Number}");
				case "L":
				case "l":
					// LOCATION OF EXECUTOR
					throw new NotImplementedException();
				case "C":
				case "c":
					// LAST COMMAND BEFORE EVALUATION
					throw new NotImplementedException();
				case "U":
				case "u":
					// LAST COMMAND AFTER EVALUATION
					throw new NotImplementedException();
				case "?":
					return new CallState(parser.State.Count().ToString());
				case "+":
					return new CallState(parser.State.Peek().Arguments.Count().ToString());
				default: 
					return new CallState(contextValue);
			}
		}

		public static CallState ParseComplexSubstitution(string symbol, Parser parser, ComplexSubstitutionSymbolContext context)
		{
			throw new NotImplementedException();
		}
	}
}
