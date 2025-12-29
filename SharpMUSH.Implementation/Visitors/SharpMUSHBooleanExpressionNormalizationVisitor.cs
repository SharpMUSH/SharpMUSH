using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Visitor for normalizing PennMUSH lock expressions by converting bare dbrefs to objids.
/// This ensures that locks reference specific object instances and won't incorrectly match
/// new objects when dbrefs are recycled after object destruction.
/// </summary>
/// <param name="med">Mediator for database queries to look up object creation times</param>
public class SharpMUSHBooleanExpressionNormalizationVisitor(IMediator med) 
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
		return $"flag^{value}";
	}

	public override string VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
	{
		var value = context.@string().GetText();
		return $"power^{value}";
	}

	public override string VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
	{
		var typeValue = context.objectType().GetText();
		return $"type^{typeValue}";
	}

	public override string VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		var value = context.@string().GetText();
		return $"channel^{value}";
	}

	public override string VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		var attrName = context.@string().GetText();
		// Note: The dbrefs in the attribute list will need to be normalized separately
		// when the attribute is set, not when the lock is set
		return $"dbreflist^{attrName}";
	}

	public override string VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
	{
		var value = context.@string().GetText();
		return $"ip^{value}";
	}

	public override string VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
	{
		var value = context.@string().GetText();
		return $"hostname^{value}";
	}

	public override string VisitNameExpr(SharpMUSHBoolExpParser.NameExprContext context)
	{
		var pattern = context.@string().GetText();
		return $"name^{pattern}";
	}

	public override string VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
	{
		var value = context.@string().GetText();
		return $"={NormalizeDbRef(value)}";
	}

	public override string VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		var attrName = context.attributeName().GetText();
		var value = context.@string().GetText();
		return $"{attrName}:{value}";
	}

	public override string VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
	{
		var attrName = context.attributeName().GetText();
		var value = context.@string().GetText();
		return $"{attrName}/{value}";
	}

	public override string VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
	{
		var value = context.@string().GetText();
		var normalizedDbRef = NormalizeDbRef(value);
		
		if (context.attributeName() != null)
		{
			var attrName = context.attributeName().GetText();
			return $"@{normalizedDbRef}/{attrName}";
		}
		
		return $"@{normalizedDbRef}";
	}

	/// <summary>
	/// Normalizes a dbref string by converting bare dbrefs to objids.
	/// If the input is already an objid or not a dbref, returns it unchanged.
	/// </summary>
	private string NormalizeDbRef(string value)
	{
		// Try to parse as a dbref
		var parsed = HelperFunctions.ParseDbRef(value);
		
		if (parsed.IsNone())
		{
			// Not a dbref, return as-is (could be a name like "me" or an object name)
			return value;
		}
		
		var dbref = parsed.AsValue();
		
		// If it already has a creation timestamp, no normalization needed
		if (dbref.CreationMilliseconds.HasValue)
		{
			return value;
		}
		
		// Look up the object to get its creation time
		try
		{
			var objResult = med.Send(
					new GetObjectNodeQuery(dbref),
					CancellationToken.None)
				.AsTask()
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();
			
			if (objResult.IsNone)
			{
				// Object doesn't exist, return original (validation will catch this)
				return value;
			}
			
			var obj = objResult.Known;
			var objDbRef = obj.Object().DBRef;
			
			// Return objid format
			return $"#{objDbRef.Number}:{objDbRef.CreationMilliseconds}";
		}
		catch
		{
			// If lookup fails, return original value
			return value;
		}
	}
}
