using Antlr4.Runtime;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.Models;
using System.Linq.Expressions;

namespace SharpMUSH.Implementation;

public class BooleanExpressionParser(Parser parser)
{
	public bool Parse(string text, DBRef invoker)
	{
		AntlrInputStream inputStream = new(text);
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
		ParameterExpression parameter = Expression.Parameter(typeof(DBRef), "invoker");
		SharpMUSHBooleanExpressionVisitor visitor = new(parser, parameter);
		Expression expression = visitor.Visit(chatContext);
		UnaryExpression isTrue = Expression.IsTrue(expression);

		bool result = Expression.Lambda<Func<DBRef, bool>>(expression, parameter).Compile().Invoke(invoker);

		return result;
	}

	public bool Validate(string text, DBRef invoker)
	{
		AntlrInputStream inputStream = new(text);
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
		SharpMUSHBooleanExpressionValidationVisitor visitor = new(parser, invoker);
		
		bool valid = visitor.Visit(chatContext)!.Value;

		return valid;
	}
}
