using SharpMUSH.Library;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Visitor for normalizing PennMUSH lock expressions to canonical form.
/// Uppercases keyword prefixes and attribute names per PennMUSH conventions.
/// </summary>
public class SharpMUSHBooleanExpressionNormalizationVisitor
	: SharpMUSHBoolExpParserBaseVisitor<string>
{
	protected override string AggregateResult(string aggregate, string nextResult)
		=> string.IsNullOrEmpty(aggregate) ? nextResult : aggregate;

	public override string VisitLock(SharpMUSHBoolExpParser.LockContext context)
		=> Visit(context.lockExprList());

	public override string VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
		=> VisitChildren(context);

	public override string VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
		=> $"{Visit(context.lockExpr())} & {Visit(context.lockExprList())}";

	public override string VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
		=> $"{Visit(context.lockExpr())} | {Visit(context.lockExprList())}";

	public override string VisitLockExpr(SharpMUSHBoolExpParser.LockExprContext context)
		=> VisitChildren(context);

	public override string VisitNotExpr(SharpMUSHBoolExpParser.NotExprContext context)
		=> $"!{Visit(context.lockExpr())}";

	public override string VisitFalseExpr(SharpMUSHBoolExpParser.FalseExprContext context)
		=> "#FALSE";

	public override string VisitTrueExpr(SharpMUSHBoolExpParser.TrueExprContext context)
		=> "#TRUE";

	public override string VisitEnclosedExpr(SharpMUSHBoolExpParser.EnclosedExprContext context)
		=> $"({Visit(context.lockExprList())})";

	public override string VisitOwnerExpr(SharpMUSHBoolExpParser.OwnerExprContext context)
	{
		var value = context.@string().GetText();
		return $"${NormalizeDbRef(value)}";
	}

	public override string VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
	{
		var value = context.@string().GetText();
		return $"+{NormalizeDbRef(value)}";
	}

	public override string VisitBitFlagExpr(SharpMUSHBoolExpParser.BitFlagExprContext context)
	{
		var value = context.@string().GetText();
		return $"FLAG^{value.ToUpperInvariant()}";
	}

	public override string VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
	{
		var value = context.@string().GetText();
		return $"POWER^{value.ToUpperInvariant()}";
	}

	public override string VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
	{
		var typeValue = context.objectType().GetText();
		return $"TYPE^{typeValue.ToUpperInvariant()}";
	}

	public override string VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		var value = context.@string().GetText();
		return $"CHANNEL^{value.ToUpperInvariant()}";
	}

	public override string VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		var attrName = context.@string().GetText();
		// Note: The dbrefs in the attribute list will need to be normalized separately
		// when the attribute is set, not when the lock is set
		return $"DBREFLIST^{attrName.ToUpperInvariant()}";
	}

	public override string VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
	{
		var value = context.@string().GetText();
		return $"IP^{value}";
	}

	public override string VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
	{
		var value = context.@string().GetText();
		return $"HOSTNAME^{value.ToUpperInvariant()}";
	}

	public override string VisitNameExpr(SharpMUSHBoolExpParser.NameExprContext context)
	{
		var pattern = context.@string().GetText();
		return $"NAME^{pattern.ToUpperInvariant()}";
	}

	public override string VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
	{
		var value = context.ATTRIBUTE_COLON() != null
			? $"{context.@string(0).GetText()}:{context.@string(1).GetText()}"
			: context.@string(0).GetText();
		return $"={NormalizeDbRef(value)}";
	}

	public override string VisitDefaultExpr(SharpMUSHBoolExpParser.DefaultExprContext context)
	{
		var value = context.@string().GetText();
		return NormalizeDbRef(value);
	}

	public override string VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		var attrName = context.@string(0).GetText();
		var value = context.@string(1).GetText();
		return $"{attrName.ToUpperInvariant()}:{value}";
	}

	public override string VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
	{
		var attrName = context.@string(0).GetText();
		var value = context.@string(1).GetText();
		return $"{attrName.ToUpperInvariant()}/{value}";
	}

	public override string VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
	{
		var value = context.@string(0).GetText();
		var normalizedDbRef = NormalizeDbRef(value);

		if (context.@string().Length > 1)
		{
			var attrName = context.@string(1).GetText();
			return $"@{normalizedDbRef}/{attrName}";
		}

		return $"@{normalizedDbRef}/Basic";
	}

	/// <summary>
	/// Normalizes a dbref string. PennMUSH lock readback preserves bare dbrefs (#1)
	/// without adding creation timestamps. Only returns what was given.
	/// </summary>
	private string NormalizeDbRef(string value)
	{
		// PennMUSH preserves the dbref format as-is in lock readback.
		// No objid expansion needed — return the value unchanged.
		return value;
	}
}
