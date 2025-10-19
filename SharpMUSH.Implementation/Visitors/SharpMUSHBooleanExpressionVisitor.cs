using System.Linq.Expressions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Visitors;

public class SharpMUSHBooleanExpressionVisitor(ISharpDatabase database, ParameterExpression gated, ParameterExpression unlocker) : SharpMUSHBoolExpParserBaseVisitor<Expression>
{
	protected override Expression AggregateResult(Expression aggregate, Expression nextResult)
		=> new Expression[] { aggregate, nextResult }.First(x => x is not null);

	private readonly Expression<Func<AnySharpObject, string, bool>> _hasFlag = (dbRef, flag)
		=> dbRef.Object().Flags.WithCancellation(CancellationToken.None).GetAwaiter().GetResult().Any(x => x.Name == flag || x.Symbol == flag);

	private readonly Expression<Func<AnySharpObject, string, bool>> _hasPower = (dbRef, power)
		=> dbRef.Object().Powers.WithCancellation(CancellationToken.None).GetAwaiter().GetResult().Any(x => x.Name == power || x.Alias == power);

	private readonly Expression<Func<AnySharpObject, string, bool>> _isType = (dbRef, type)
		=> dbRef.Object().Type == type;

	private static readonly string[] defaultStringArrayValue = [];

	public override Expression VisitLock(SharpMUSHBoolExpParser.LockContext context)
	{
		var result = VisitChildren(context);
		for (; result.CanReduce; result = result.Reduce()) { }
		return result;
	}

	public override Expression VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
	{
		var result = VisitChildren(context);
		for (; result.CanReduce; result = result.Reduce()) { }
		return result;
	}

	public override Expression VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
		=> Expression.AndAlso(Visit(context.lockExpr()), Visit(context.lockExprList()));

	public override Expression VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
		=> Expression.OrElse(Visit(context.lockExpr()), Visit(context.lockExprList()));

	public override Expression VisitLockExpr(SharpMUSHBoolExpParser.LockExprContext context)
	{
		var result = VisitChildren(context);
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
		=> Expression.Invoke(_hasFlag, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()));

	public override Expression VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
		=> Expression.Invoke(_hasPower, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()));

	public override Expression VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
		=> Expression.Invoke(_isType, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()));

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
			database
				.GetAttributeAsync(dbref, attribute) // TODO: PERMISSIONS - use the Service instead.
				.AsTask()
				.ConfigureAwait(false).GetAwaiter().GetResult()!
				.FirstOrDefaultAsync(new SharpAttribute(
					string.Empty,
					string.Empty,
					Enumerable.Empty<SharpAttributeFlag>(),
					null,
					string.Empty,
					new(_ => Task.FromResult(Enumerable.Empty<SharpAttribute>().ToAsyncEnumerable()), false),
					new(_ => Task.FromResult<SharpPlayer?>(null), false),
					new(_ => Task.FromResult<SharpAttributeEntry?>(null), false))
				{
					Value = MModule.single(Guid.NewGuid().ToString())
				}).Result.Value == MModule.single(value);

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
