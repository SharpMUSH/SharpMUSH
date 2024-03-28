using SharpMUSH.Library.Models;
using System.Linq.Expressions;

namespace SharpMUSH.Implementation.Visitors;

public class SharpMUSHBooleanExpressionVisitor(Parser parser, ParameterExpression caller, ParameterExpression victim) : SharpMUSHBoolExpParserBaseVisitor<Expression>
{
	protected override Expression AggregateResult(Expression aggregate, Expression nextResult) =>
		new Expression[] { aggregate, nextResult }.First(x => x != null);

	private readonly Expression<Func<DBRef, string, Parser, bool>> hasFlag = (dbRef, flag, psr)
		=> psr.Database.GetObjectNode(dbRef)!
				.Match(
					player => player.Object!.Flags!.Any(x => x.Name == flag || x.Symbol == flag),
					room => room.Object!.Flags!.Any(x => x.Name == flag || x.Symbol == flag),
					exit => exit.Object!.Flags!.Any(x => x.Name == flag || x.Symbol == flag),
					thing => thing.Object!.Flags!.Any(x => x.Name == flag || x.Symbol == flag),
					none => false
				);

	private readonly Expression<Func<DBRef, string, Parser, bool>> hasPower = (dbRef, power, psr)
		=> psr.Database.GetObjectNode(dbRef)!
				.Match(
					player => player.Object!.Powers!.Any(x => x.Name == power || x.Alias == power ),
					room => room.Object!.Powers!.Any(x => x.Name == power || x.Alias == power),
					exit => exit.Object!.Powers!.Any(x => x.Name == power || x.Alias == power),
					thing => thing.Object!.Powers!.Any(x => x.Name == power || x.Alias == power),
					none => false
				);

	private readonly Expression<Func<DBRef, string, Parser, bool>> isType = (dbRef, type, psr)
		=> psr.Database.GetObjectNode(dbRef)!
				.Match(
					player => player.Object!.Type == type,
					room => room.Object!.Type == type,
					exit => exit.Object!.Type == type,
					thing => thing.Object!.Type == type,
					none => false
				);

	public override Expression VisitLock(SharpMUSHBoolExpParser.LockContext context)
	{
		var _2 = caller;
		var _ = parser.GetType();

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
		=> Expression.Invoke(hasFlag, victim, Expression.Constant(context.@string().GetText().ToUpper().Trim()), Expression.Constant(parser));

	public override Expression VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
		=> Expression.Invoke(hasPower, victim, Expression.Constant(context.@string().GetText().ToUpper().Trim()), Expression.Constant(parser));

	public override Expression VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context) 
		=> Expression.Invoke(isType, victim, Expression.Constant(context.@string().GetText().ToUpper().Trim()), Expression.Constant(parser));

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
		// TODO: Implement Attribute Expression
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();
		return VisitChildren(context);
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
