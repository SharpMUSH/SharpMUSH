using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Visitor for normalizing PennMUSH lock expressions to canonical form.
/// Uppercases keyword prefixes and attribute names per PennMUSH conventions.
/// Resolves object names to dbrefs at lock-set time, matching PennMUSH behavior.
/// </summary>
public class SharpMUSHBooleanExpressionNormalizationVisitor(
	IMediator? mediator = null,
	AnySharpObject? executor = null)
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
		return $"${ResolveToDbRef(value)}";
	}

	public override string VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
	{
		var value = context.@string().GetText();
		return $"+{ResolveToDbRef(value)}";
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
		if (context.ATTRIBUTE_COLON() != null)
		{
			// Attribute pattern like =attr:value — not an object reference
			var value = $"{context.@string(0).GetText()}:{context.@string(1).GetText()}";
			return $"={value}";
		}

		var target = context.@string(0).GetText();
		return $"={ResolveToDbRef(target)}";
	}

	public override string VisitDefaultExpr(SharpMUSHBoolExpParser.DefaultExprContext context)
	{
		var value = context.@string().GetText();
		return ResolveToDbRef(value);
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
		var normalizedDbRef = ResolveToDbRef(value);

		if (context.@string().Length > 1)
		{
			var attrName = context.@string(1).GetText();
			return $"@{normalizedDbRef}/{attrName}";
		}

		return $"@{normalizedDbRef}/Basic";
	}

	/// <summary>
	/// Resolves a lock target to a dbref. If the value is already a dbref (#N or #N:timestamp),
	/// returns it as-is. If it's "me", returns it as-is (resolved at evaluation time).
	/// Otherwise, attempts name resolution using the executor's context, matching PennMUSH's
	/// behavior of converting names to dbrefs at @lock time.
	/// If name resolution fails (no executor or no match), preserves the original name.
	/// </summary>
	private string ResolveToDbRef(string value)
	{
		// Already a dbref — return as-is
		if (value.StartsWith('#'))
			return value;

		// "me" is special — resolved at evaluation time
		if (value.Equals("me", StringComparison.OrdinalIgnoreCase))
			return value;

		// No executor/mediator — can't resolve names, preserve as-is
		if (mediator == null || executor == null)
			return value;

		// Attempt to resolve the name to a dbref using the executor's context
		try
		{
			var exec = executor!;
			var locateResult = mediator.Send(
				new LocateObjectQuery(exec, exec, value, LocateFlags.All),
				CancellationToken.None
			).AsTask().GetAwaiter().GetResult();

			return locateResult.IsAnyObject
				? $"#{locateResult.AsAnyObject.Object().DBRef.Number}"
				: value; // Not found — preserve the name
		}
		catch
		{
			// If resolution fails for any reason, preserve the original name
			return value;
		}
	}
}
