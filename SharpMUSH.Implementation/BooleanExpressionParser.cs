using Antlr4.Runtime;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.Models;
using System.Linq.Expressions;

namespace SharpMUSH.Implementation;

public class BooleanExpressionParser(MUSHCodeParser parser)
{
	// TODO: Allow the Evaluation to indicate if the cache should be evaluated for optimization.
	// This should occur if a character stop existing, a flag gets removed, etc, and should be unusual.
	public Func<DBRef, DBRef, bool> Compile(string text)
	{
		AntlrInputStream inputStream = new(text);
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
		ParameterExpression parameter = Expression.Parameter(typeof(DBRef), "gated");
		ParameterExpression parameter2 = Expression.Parameter(typeof(DBRef), "unlocker");
		SharpMUSHBooleanExpressionVisitor visitor = new(parser, parameter, parameter2);
		Expression expression = visitor.Visit(chatContext);

		return Expression.Lambda<Func<DBRef, DBRef, bool>>(expression, parameter, parameter2).Compile();
	}

	public bool Validate(string text, DBRef lockee)
	{
		AntlrInputStream inputStream = new(text);
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
		SharpMUSHBooleanExpressionValidationVisitor visitor = new(parser, lockee);

		bool valid = visitor.Visit(chatContext)!.Value;

		return valid;
	}
}
