﻿using SharpMUSH.Library.DiscriminatedUnions;

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
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		var value = context.attributeName().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
	{
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
	{
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
	{
		var value = context.@string().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
	{
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
	{
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();
		return VisitChildren(context);
	}

	public override bool? VisitString(SharpMUSHBoolExpParser.StringContext context) =>
		throw new ArgumentException("Parser should never reach here.");

	public override bool? VisitAttributeName(SharpMUSHBoolExpParser.AttributeNameContext context) =>
		throw new ArgumentException("Parser should never reach here.");

}
