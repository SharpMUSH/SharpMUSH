using Antlr4.Runtime;
using AntlrCSharp.Implementation.Visitors;
using System.Collections.Immutable;

namespace AntlrCSharp.Implementation
{
	public class Parser
	{
		public IImmutableList<string> Parse(string text)
		{
			AntlrInputStream inputStream = new(text.ToString());
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.EvaluationStringContext chatContext = pennParser.evaluationString();
			PennMUSHParserVisitor visitor = new();

			return visitor.Visit(chatContext);
		}
	}
}
