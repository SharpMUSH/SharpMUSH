using Antlr4.Runtime;
using AntlrCSharp.Implementation.Visitors;

namespace AntlrCSharp.Implementation
{
	/// <summary>
	/// Provides the parser.
	/// Each call is Synchronous, and stateful at this time.
	/// </summary>
	public class Parser
	{
		public CallState? FunctionParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.EvaluationStringContext chatContext = pennParser.evaluationString();
			PennMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandListParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.CommandListContext chatContext = pennParser.commandList();
			PennMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.CommandContext chatContext = pennParser.command();
			PennMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}
	}
}
