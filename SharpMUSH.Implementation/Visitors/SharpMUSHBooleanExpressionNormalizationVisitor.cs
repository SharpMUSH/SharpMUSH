namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Formats parsed lock syntax, transforming only object operands through the supplied resolver.
/// </summary>
public class SharpMUSHBooleanExpressionNormalizationVisitor(
	Func<string, string>? resolveObject = null, bool compact = false)
	: SharpMUSHBoolExpParserBaseVisitor<string>
{
	protected override string AggregateResult(string aggregate, string nextResult)
		=> string.IsNullOrEmpty(aggregate) ? nextResult : aggregate;

	public override string VisitLock(SharpMUSHBoolExpParser.LockContext context)
		=> Visit(context.lockExprList());

	public override string VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
		=> VisitChildren(context);

	public override string VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
		=> string.Join(compact ? "&" : " & ", context.lockExpr().Select(x => Visit(x)));

	public override string VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
		=> string.Join(compact ? "|" : " | ", context.lockAndExpr().Select(x => Visit(x)));

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
		var value = LockLiteralText.ReadOperand(context.objectOperand());
		return $"${ResolveToDbRef(value)}";
	}

	public override string VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
	{
		var value = LockLiteralText.ReadOperand(context.objectOperand());
		return $"+{ResolveToDbRef(value)}";
	}

	public override string VisitBitFlagExpr(SharpMUSHBoolExpParser.BitFlagExprContext context)
	{
		var value = LockLiteralText.Read(context.literal());
		return $"FLAG^{value.ToUpperInvariant()}";
	}

	public override string VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
	{
		var value = LockLiteralText.Read(context.literal());
		return $"POWER^{value.ToUpperInvariant()}";
	}

	public override string VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
	{
		var typeValue = context.objectType().GetText();
		return $"TYPE^{typeValue.ToUpperInvariant()}";
	}

	public override string VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		var value = LockLiteralText.Read(context.literal());
		return $"CHANNEL^{value}";
	}

	public override string VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		var attrName = LockLiteralText.Read(context.literal());
		// Note: The dbrefs in the attribute list will need to be normalized separately
		// when the attribute is set, not when the lock is set
		return $"DBREFLIST^{attrName.ToUpperInvariant()}";
	}

	public override string VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
	{
		var value = LockLiteralText.Read(context.literal());
		return $"IP^{value}";
	}

	public override string VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
	{
		var value = LockLiteralText.Read(context.literal());
		return $"HOSTNAME^{value}";
	}

	public override string VisitNameExpr(SharpMUSHBoolExpParser.NameExprContext context)
	{
		var pattern = LockLiteralText.Read(context.literal());
		return $"NAME^{pattern}";
	}

	public override string VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
		=> $"={ResolveToDbRef(LockLiteralText.ReadOperand(context.objectOperand()))}";

	public override string VisitDefaultExpr(SharpMUSHBoolExpParser.DefaultExprContext context)
	{
		var value = context.@string().GetText();
		return ResolveToDbRef(value);
	}

	public override string VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		var attrName = context.@string().GetText();
		var value = LockLiteralText.Read(context.literal());
		return $"{attrName.ToUpperInvariant()}:{value}";
	}

	public override string VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
	{
		var attrName = context.@string().GetText();
		var value = LockLiteralText.Read(context.literal());
		return $"{attrName.ToUpperInvariant()}/{value}";
	}

	public override string VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
		=> $"@{ResolveToDbRef(LockLiteralText.ReadOperand(context.objectOperand()))}/{context.@string()?.GetText() ?? "Basic"}";

	private string ResolveToDbRef(string value) => resolveObject?.Invoke(value) ?? value;
}
