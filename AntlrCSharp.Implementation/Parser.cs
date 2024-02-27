using Antlr4.Runtime;
using AntlrCSharp.Implementation.Visitors;
using System.Collections.Immutable;

namespace AntlrCSharp.Implementation
{
	public class Parser
	{
		public IImmutableList<string> FunctionParse(string text)
		{
			AntlrInputStream inputStream = new(text.ToString());
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.EvaluationStringContext chatContext = pennParser.evaluationString();
			PennMUSHParserVisitor visitor = new();

			return visitor.Visit(chatContext);
		}

		public IImmutableList<string> CommandListParse(string text)
		{
			AntlrInputStream inputStream = new(text.ToString());
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.CommandListContext chatContext = pennParser.commandList();
			PennMUSHParserVisitor visitor = new();

			return visitor.Visit(chatContext);
		}

		public IImmutableList<string> CommandParse(string text)
		{
			AntlrInputStream inputStream = new(text.ToString());
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.CommandContext chatContext = pennParser.command();
			PennMUSHParserVisitor visitor = new();

			return visitor.Visit(chatContext);
		}
	}
}
