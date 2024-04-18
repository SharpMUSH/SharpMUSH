using Antlr4.Runtime;
using OneOf;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Linq.Expressions;

namespace SharpMUSH.Implementation;

public class BooleanExpressionParser(IMUSHCodeParser parser)
{
	// TODO: Allow the Evaluation to indicate if the cache should be evaluated for optimization.
	// This should occur if a character stop existing, a flag gets removed, etc, and should be unusual.
	public Func<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, bool> Compile(string text)
	{
		AntlrInputStream inputStream = new(text);
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
		ParameterExpression parameter = Expression.Parameter(typeof(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>), "gated");
		ParameterExpression parameter2 = Expression.Parameter(typeof(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>), "unlocker");
		SharpMUSHBooleanExpressionVisitor visitor = new(parser, parameter, parameter2);
		Expression expression = visitor.Visit(chatContext);

		return Expression.Lambda<Func<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, bool>>(expression, parameter, parameter2).Compile();
	}

	public bool Validate(string text, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> lockee)
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
