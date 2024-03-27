using Antlr4.Runtime;
using Serilog;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.Models;
using System.Linq.Expressions;

namespace SharpMUSH.Implementation;

public class BooleanExpressionParser(Parser parser)
{
	private readonly Dictionary<string, Func<DBRef, DBRef, bool>> _cache = [];

	public bool Parse(string text, DBRef caller, DBRef victim)
	{
		Func<DBRef, DBRef, bool> func;
		if( !_cache.TryGetValue(text, out func!) )
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
			SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
			ParameterExpression parameter = Expression.Parameter(typeof(DBRef), "caller");
			ParameterExpression parameter2 = Expression.Parameter(typeof(DBRef), "victim");
			SharpMUSHBooleanExpressionVisitor visitor = new(parser, parameter, parameter2);
			Expression expression = visitor.Visit(chatContext);

			func = Expression.Lambda<Func<DBRef, DBRef, bool>>(expression, parameter, parameter2).Compile();

			_cache.Add(text, func);
		}
		return func(caller, victim);
	}

	public bool Validate(string text, DBRef caller)
	{
		AntlrInputStream inputStream = new(text);
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		SharpMUSHBoolExpParser.LockContext chatContext = sharpParser.@lock();
		SharpMUSHBooleanExpressionValidationVisitor visitor = new(parser, caller);
		
		bool valid = visitor.Visit(chatContext)!.Value;

		return valid;
	}
}
