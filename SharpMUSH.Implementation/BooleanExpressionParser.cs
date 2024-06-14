using Antlr4.Runtime;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using System.Linq.Expressions;
using SharpMUSH.Library;

namespace SharpMUSH.Implementation;

public class BooleanExpressionParser(ISharpDatabase database) : IBooleanExpressionParser
{
	// TODO: Allow the Evaluation to indicate if the cache should be evaluated for optimization.
	// This should occur if a character stop existing, a flag gets removed, etc, and should be unusual.
	public Func<AnySharpObject, AnySharpObject, bool> Compile(string text)
	{
		AntlrInputStream inputStream = new(text);
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
		ParameterExpression parameter = Expression.Parameter(typeof(AnySharpObject), "gated");
		ParameterExpression parameter2 = Expression.Parameter(typeof(AnySharpObject), "unlocker");
		SharpMUSHBooleanExpressionVisitor visitor = new(database, parameter, parameter2);
		Expression expression = visitor.Visit(chatContext);

		return Expression.Lambda<Func<AnySharpObject, AnySharpObject, bool>>(expression, parameter, parameter2).Compile();
	}

	public bool Validate(string text, AnySharpObject lockee)
	{
		AntlrInputStream inputStream = new(text);
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
		SharpMUSHBooleanExpressionValidationVisitor visitor = new(lockee);

		bool valid = visitor.Visit(chatContext)!.Value;

		return valid;
	}
}
