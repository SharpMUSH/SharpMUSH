using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Visitors;

public class SharpMUSHBooleanExpressionValidationVisitor(AnySharpObject? invoker = null) : SharpMUSHBoolExpParserBaseVisitor<bool?>
{
	protected override bool? AggregateResult(bool? aggregate, bool? nextResult)
		=> (aggregate ?? true) && (nextResult ?? true);

	public override bool? VisitLock(SharpMUSHBoolExpParser.LockContext context)
		=> VisitChildren(context);

	public override bool? VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
		=> VisitChildren(context);

	// Validity of a compound expression is the conjunction of its operands' validity,
	// regardless of which operator joins them.
	public override bool? VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
		=> context.lockExpr().All(x => Visit(x)!.Value);

	public override bool? VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
		=> context.lockAndExpr().All(x => Visit(x)!.Value);

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
		=> ValidObjectOperand(context.@string());

	public override bool? VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
		=> ValidObjectOperand(context.@string());

	public override bool? VisitBitFlagExpr(SharpMUSHBoolExpParser.BitFlagExprContext context)
	{
		var value = context.@string().GetText();
		var _ = invoker; // Silence the linter / compiler for now.
										 // We don't check for legality of flags.
		return true;
	}

	public override bool? VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
	{
		// Power locks are always valid syntactically
		return true;
	}

	public override bool? VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
		=> context.objectType().GetText().ToUpper().Trim() is "PLAYER" or "THING" or "EXIT" or "ROOM";

	public override bool? VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		// Channel locks are always valid syntactically
		var value = context.@string().GetText();
		return true;
	}

	public override bool? VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		// DBRef list locks are always valid syntactically
		var value = context.@string().GetText();
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
		=> ValidObjectOperand(context.@string(0));

	public override bool? VisitDefaultExpr(SharpMUSHBoolExpParser.DefaultExprContext context)
		=> ValidObjectOperand(context.@string());

	public override bool? VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		// Attribute locks are always valid syntactically
		return true;
	}

	public override bool? VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
	{
		// Evaluation locks are always valid syntactically
		return true;
	}

	public override bool? VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
		=> ValidObjectOperand(context.@string(0));

	private static bool ValidObjectOperand(SharpMUSHBoolExpParser.StringContext operand)
	{
		var value = operand.GetText();
		var numericReference = operand.STAMPED_DBREF() != null ||
			value.Length > 1 && value[0] == '#' && value.Skip(1).All(char.IsDigit);
		return !numericReference || DBRef.TryParse(value, out _);
	}

	public override bool? VisitString(SharpMUSHBoolExpParser.StringContext context) =>
		throw new ArgumentException("Parser should never reach here.");

}
