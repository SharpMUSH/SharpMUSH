using Antlr4.Runtime;
using SharpMUSH.Implementation.Visitors;
using System.Linq.Expressions;

namespace SharpMUSH.Implementation
{
	public class BooleanExpressionParser(Parser parser)
	{
		public Expression Parse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
			SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
			SharpMUSHBooleanExpressionVisitor visitor = new(parser);

			return visitor.Visit(chatContext);
		}
	}
}
