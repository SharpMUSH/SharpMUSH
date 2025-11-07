using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Visitors;

public class SharpMUSHBooleanExpressionValidationVisitor(AnySharpObject invoker) : SharpMUSHBoolExpParserBaseVisitor<bool?>
{
	protected override bool? AggregateResult(bool? aggregate, bool? nextResult)
		=> (aggregate ?? true) && (nextResult ?? true);

	public override bool? VisitLock(SharpMUSHBoolExpParser.LockContext context)
		=> VisitChildren(context);

	public override bool? VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
		=> VisitChildren(context);

	public override bool? VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
		=> Visit(context.lockExpr())!.Value && Visit(context.lockExprList())!.Value;

	public override bool? VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
		=> Visit(context.lockExpr())!.Value && Visit(context.lockExprList())!.Value;

	public override bool? VisitLockExpr(SharpMUSHBoolExpParser.LockExprContext context)
		=> VisitChildren(context);

	public override bool? VisitNotExpr(SharpMUSHBoolExpParser.NotExprContext context)
		=> Visit(context.lockExpr());

	public override bool? VisitFalseExpr(SharpMUSHBoolExpParser.FalseExprContext context)
		=> true;

	public override bool? VisitTrueExpr(SharpMUSHBoolExpParser.TrueExprContext context)
		=> true;

	public override bool? VisitEnclosedExpr(SharpMUSHBoolExpParser.EnclosedExprContext context)
		=> Visit(context.lockExprList());

	public override bool? VisitOwnerExpr(SharpMUSHBoolExpParser.OwnerExprContext context)
	{
		var value = context.@string().GetText();
		var result = VisitChildren(context);
		return result;
	}

	public override bool? VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
	{
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitBitFlagExpr(SharpMUSHBoolExpParser.BitFlagExprContext context)
	{
		var value = context.@string().GetText();
		var _ = invoker; // Silence the linter / compiler for now.
										 // We don't check for legality of flags.
		return true;
	}

	public override bool? VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
	{
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
		=> context.@string().GetText().ToUpper().Trim() is "PLAYER" or "THING" or "EXIT" or "ROOM";

	public override bool? VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		// Channel locks are always valid syntactically
		var value = context.@string().GetText();
		return true;
	}

	public override bool? VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		// DBRef list locks are always valid syntactically
		var value = context.attributeName().GetText();
		return true;
	}

	public override bool? VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
	{
		// IP locks are always valid syntactically
		var value = context.@string().GetText();
		return true;
	}

	public override bool? VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
	{
		// Hostname locks are always valid syntactically
		var value = context.@string().GetText();
		return true;
	}

	public override bool? VisitNameExpr(SharpMUSHBoolExpParser.NameExprContext context)
	{
		// Name locks are always valid - they just check pattern matching
		var pattern = context.@string().GetText();
		return true;
	}

	public override bool? VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
	{
		// Exact object locks are always valid - they check at runtime
		var value = context.@string().GetText();
		return true;
	}

	public override bool? VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		// Attribute locks are always valid syntactically
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();
		return true;
	}

	public override bool? VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
	{
		// Evaluation locks are always valid syntactically
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();
		return true;
	}

	public override bool? VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
	{
		// Indirect locks are always valid syntactically
		var value = context.@string().GetText();
		var attribute = context.attributeName()?.GetText();
		return true;
	}

	public override bool? VisitString(SharpMUSHBoolExpParser.StringContext context) =>
		throw new ArgumentException("Parser should never reach here.");

	public override bool? VisitAttributeName(SharpMUSHBoolExpParser.AttributeNameContext context) =>
		throw new ArgumentException("Parser should never reach here.");

}
