using OneOf;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Linq.Expressions;
using System.Linq;

namespace SharpMUSH.Implementation.Visitors;

public class SharpMUSHBooleanExpressionVisitor(IMUSHCodeParser parser, ParameterExpression gated, ParameterExpression unlocker) : SharpMUSHBoolExpParserBaseVisitor<Expression>
{
	protected override Expression AggregateResult(Expression aggregate, Expression nextResult)
		=> new Expression[] { aggregate, nextResult }.First(x => x != null);

	private readonly Expression<Func<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, string, IMUSHCodeParser, bool>> hasFlag = (dbRef, flag, psr)
		=> dbRef.Object().Flags.Any(x => x.Name == flag || x.Symbol == flag);

	private readonly Expression<Func<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, string, IMUSHCodeParser, bool>> hasPower = (dbRef, power, psr)
		=> dbRef.Object().Powers.Any(x => x.Name == power || x.Alias == power);

	private readonly Expression<Func<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, string, IMUSHCodeParser, bool>> isType = (dbRef, type, psr)
		=> dbRef.Object().Type == type;

	private static readonly string[] defaultStringArrayValue = [];

	public override Expression VisitLock(SharpMUSHBoolExpParser.LockContext context)
	{
		Expression result = VisitChildren(context);
		for (; result.CanReduce; result = result.Reduce()) { }
		return result;
	}

	public override Expression VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
	{
		Expression result = VisitChildren(context);
		for (; result.CanReduce; result = result.Reduce()) { }
		return result;
	}

	public override Expression VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
		=> Expression.AndAlso(Visit(context.lockExpr()), Visit(context.lockExprList()));

	public override Expression VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
		=> Expression.OrElse(Visit(context.lockExpr()), Visit(context.lockExprList()));

	public override Expression VisitLockExpr(SharpMUSHBoolExpParser.LockExprContext context)
	{
		Expression result = VisitChildren(context);
		for (; result.CanReduce; result = result.Reduce()) { }
		return result;
	}

	public override Expression VisitNotExpr(SharpMUSHBoolExpParser.NotExprContext context)
		=> Expression.Not(Visit(context.lockExpr()));

	public override Expression VisitFalseExpr(SharpMUSHBoolExpParser.FalseExprContext context)
		=> Expression.Constant(false);

	public override Expression VisitTrueExpr(SharpMUSHBoolExpParser.TrueExprContext context)
		=> Expression.Constant(true);

	public override Expression VisitEnclosedExpr(SharpMUSHBoolExpParser.EnclosedExprContext context)
		=> Visit(context.lockExprList());

	public override Expression VisitOwnerExpr(SharpMUSHBoolExpParser.OwnerExprContext context)
	{
		var value = context.@string().GetText();

		var result = VisitChildren(context);
		return result;
	}

	public override Expression VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
	{
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override Expression VisitBitFlagExpr(SharpMUSHBoolExpParser.BitFlagExprContext context)
		=> Expression.Invoke(hasFlag, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()), Expression.Constant(parser));

	public override Expression VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
		=> Expression.Invoke(hasPower, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()), Expression.Constant(parser));

	public override Expression VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
		=> Expression.Invoke(isType, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()), Expression.Constant(parser));

	public override Expression VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		// TODO: Implement Channel Expression
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override Expression VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		// TODO: Implement DBRef List Expression
		var value = context.attributeName().GetText();
		return VisitChildren(context);
	}

	public override Expression VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
	{
		// TODO: Implement IP Expression
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override Expression VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
	{
		// TODO: Implement Host Name Expression
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override Expression VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
	{
		// TODO: Implement Exact Object Expression
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override Expression VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();

		Expression<Func<DBRef, bool>> expr = dbref =>
			parser.Database
				.GetAttributeAsync(dbref, new string[] { attribute })
				.ConfigureAwait(false).GetAwaiter().GetResult()!
				.FirstOrDefault(new SharpAttribute() { 
					Name = string.Empty,
					Flags = defaultStringArrayValue,
					Value = Guid.NewGuid().ToString() }).Value == value; 

		return Expression.Invoke(expr, gated);
	}

	public override Expression VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
	{
		// TODO: Implement Evaluation Expression
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();
		return VisitChildren(context);
	}

	public override Expression VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
	{
		// TODO: Implement Indirect Expression
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();
		return VisitChildren(context);
	}

	public override Expression VisitString(SharpMUSHBoolExpParser.StringContext context) =>
		throw new ArgumentException("Parser should never reach here.");

	public override Expression VisitAttributeName(SharpMUSHBoolExpParser.AttributeNameContext context) =>
		throw new ArgumentException("Parser should never reach here.");

}
