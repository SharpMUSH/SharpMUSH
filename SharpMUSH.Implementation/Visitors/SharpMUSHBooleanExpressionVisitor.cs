using OneOf.Monads;
using System.Linq.Expressions;

namespace SharpMUSH.Implementation.Visitors
{
	public class SharpMUSHBooleanExpressionVisitor(Parser parser) : SharpMUSHBoolExpParserBaseVisitor<Expression>
	{
		public override Expression VisitLock(SharpMUSHBoolExpParser.LockContext context)
		{
			var _ = parser.GetType();
			Expression result;
			for (result = VisitChildren(context); result.CanReduce; result = result.Reduce()) { }
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
		{
			// Do nothing. But consider a reduce.
			return VisitChildren(context);
		}

		public override Expression VisitOwnerExpr(SharpMUSHBoolExpParser.OwnerExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitBitFlagExpr(SharpMUSHBoolExpParser.BitFlagExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitString(SharpMUSHBoolExpParser.StringContext context)
		{
			return VisitChildren(context);
		}

		public override Expression VisitAttributeName(SharpMUSHBoolExpParser.AttributeNameContext context)
		{
			return VisitChildren(context);
		}

	}
}
