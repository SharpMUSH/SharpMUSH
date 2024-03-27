using System.Linq.Expressions;

namespace SharpMUSH.Implementation.Visitors
{
	public class SharpMUSHBooleanExpressionVisitor(Parser parser, ParameterExpression parameter) : SharpMUSHBoolExpParserBaseVisitor<Expression>
	{
		protected override Expression AggregateResult(Expression aggregate, Expression nextResult) => 
			new Expression[] { aggregate, nextResult }.First(x => x != null);

		public override Expression VisitLock(SharpMUSHBoolExpParser.LockContext context)
		{
			var _ = parser.GetType();
			Expression result = VisitChildren(context);
			for (; result.CanReduce; result = result.Reduce()) { }
			return result;
		}

		public override Expression VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
		{
			// Do Nothing
			return VisitChildren(context);
		}

		public override Expression VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
			=> Expression.And(Visit(context.lockExpr()), Visit(context.lockExprList()));

		public override Expression VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
			=> Expression.Or(Visit(context.lockExpr()), Visit(context.lockExprList()));

		public override Expression VisitLockExpr(SharpMUSHBoolExpParser.LockExprContext context)
		{
			// Do Nothing. Consider maybe a reduce here?
			return VisitChildren(context);
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
		{
			var value = context.@string().GetText();
			var _ = parameter; // Silence the linter / compiler for now.
			return VisitChildren(context);
		}

		public override Expression VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
		{
			var value = context.@string().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
		{
			var value = context.@string().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
		{
			var value = context.@string().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
		{
			var value = context.attributeName().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
		{
			var value = context.@string().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
		{
			var value = context.@string().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
		{
			var value = context.@string().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
		{
			var value = context.@string().GetText();
			var attribute = context.attributeName().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
		{
			var value = context.@string().GetText();
			var attribute = context.attributeName().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
		{
			var value = context.@string().GetText();
			var attribute = context.attributeName().GetText();
			return VisitChildren(context);
		}

		public override Expression VisitString(SharpMUSHBoolExpParser.StringContext context) =>
			VisitChildren(context);

		public override Expression VisitAttributeName(SharpMUSHBoolExpParser.AttributeNameContext context) =>
			VisitChildren(context);

	}
}
