using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Visitors;

public class SharpMUSHBooleanExpressionVisitor(
	IMediator med,
	ParameterExpression gated,
	ParameterExpression unlocker) : SharpMUSHBoolExpParserBaseVisitor<Expression>
{
	protected override Expression AggregateResult(Expression aggregate, Expression nextResult)
		=> new Expression[] { aggregate, nextResult }.First(x => x is not null);

	private readonly Expression<Func<AnySharpObject, string, bool>> _hasFlag = (dbRef, flag)
		=> dbRef.Object().Flags.Value
			.AnyAsync(x => x.Name == flag || x.Symbol == flag, CancellationToken.None).AsTask().GetAwaiter().GetResult();

	private readonly Expression<Func<AnySharpObject, string, bool>> _hasPower = (dbRef, power)
		=> dbRef.Object().Powers.Value
			.AnyAsync(x => x.Name == power || x.Alias == power, CancellationToken.None).AsTask().GetAwaiter().GetResult();

	private readonly Expression<Func<AnySharpObject, string, bool>> _isType = (dbRef, type)
		=> dbRef.Object().Type == type;

	private readonly Expression<Func<AnySharpObject, string, bool>> _matchesName = (dbRef, pattern) =>
		Regex.IsMatch(dbRef.Object().Name, MModule.getWildcardMatchAsRegex2(pattern), RegexOptions.IgnoreCase)
		|| dbRef.Aliases.Any(alias => Regex.IsMatch(alias.Trim(), MModule.getWildcardMatchAsRegex2(pattern), RegexOptions.IgnoreCase));

	private static readonly string[] defaultStringArrayValue = [];

	public override Expression VisitLock(SharpMUSHBoolExpParser.LockContext context)
	{
		var result = VisitChildren(context);
		for (; result.CanReduce; result = result.Reduce())
		{
		}

		return result;
	}

	public override Expression VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
	{
		var result = VisitChildren(context);
		for (; result.CanReduce; result = result.Reduce())
		{
		}

		return result;
	}

	public override Expression VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
		=> Expression.AndAlso(Visit(context.lockExpr()), Visit(context.lockExprList()));

	public override Expression VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
		=> Expression.OrElse(Visit(context.lockExpr()), Visit(context.lockExprList()));

	public override Expression VisitLockExpr(SharpMUSHBoolExpParser.LockExprContext context)
	{
		var result = VisitChildren(context);
		for (; result.CanReduce; result = result.Reduce())
		{
		}

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
		var targetName = context.@string().GetText();
		
		// For owner locks, we need to check if the unlocker is owned by the owner of the named object
		// Simplified implementation: just return false for now as this needs full database query support
		// TODO: Full implementation requires looking up target object and checking ownership
		Func<AnySharpObject, string, bool> func = (unlockerObj, target) =>
		{
			// Placeholder - needs database query at evaluation time to look up target object
			return false;
		};

		return Expression.Invoke(Expression.Constant(func), unlocker, Expression.Constant(targetName));
	}

	public override Expression VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
	{
		var targetName = context.@string().GetText();
		
		// For carry locks, check if the unlocker is carrying the named object or IS the object
		Func<AnySharpObject, string, bool> func = (unlockerObj, target) =>
		{
			// Check if unlocker IS the target object (name match)
			if (unlockerObj.Object().Name.Equals(target, StringComparison.OrdinalIgnoreCase))
				return true;
			
			// Check aliases too
			if (unlockerObj.Aliases.Any(a => a.Equals(target, StringComparison.OrdinalIgnoreCase)))
				return true;
			
			// For checking inventory, we need database access
			try
			{
				// Attempt to check contents if this is a container
				if (unlockerObj.IsContainer)
				{
					var contents = unlockerObj.AsContainer.Content(med);
					return contents
						.AnyAsync(item => item.Object().Name.Equals(target, StringComparison.OrdinalIgnoreCase), CancellationToken.None)
						.AsTask().GetAwaiter().GetResult();
				}
			}
			catch
			{
				// If not a container or any error, just use name/alias match
			}
			
			return false;
		};

		return Expression.Invoke(Expression.Constant(func), unlocker, Expression.Constant(targetName));
	}

	public override Expression VisitBitFlagExpr(SharpMUSHBoolExpParser.BitFlagExprContext context)
		=> Expression.Invoke(_hasFlag, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()));

	public override Expression VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
		=> Expression.Invoke(_hasPower, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()));

	public override Expression VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
		=> Expression.Invoke(_isType, unlocker, Expression.Constant(context.@string().GetText().ToUpper().Trim()));

	public override Expression VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		// TODO: Implement Channel Expression - requires channel system integration
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

	public override Expression VisitNameExpr(SharpMUSHBoolExpParser.NameExprContext context)
	{
		var pattern = context.@string().GetText();
		
		// Use the _matchesName lambda to check if the unlocker's name matches the pattern
		// This supports wildcards and checks both name and aliases
		return Expression.Invoke(_matchesName, unlocker, Expression.Constant(pattern));
	}

	public override Expression VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
	{
		var targetIdentifier = context.@string().GetText();
		
		// Check if unlocker matches the exact target object
		Func<AnySharpObject, AnySharpObject, string, bool> func = (gatedObj, unlockerObj, target) =>
		{
			// If target is "me", it refers to the gated object's owner
			if (target.Equals("me", StringComparison.OrdinalIgnoreCase))
			{
				var ownerTask = gatedObj.Object().Owner.WithCancellation(CancellationToken.None);
				var owner = ownerTask.GetAwaiter().GetResult();
				return unlockerObj.Object().DBRef == owner.Object.DBRef;
			}
			
			// Try to parse as DBRef
			if (target.StartsWith('#') && int.TryParse(target.Substring(1), out int dbrefNum))
			{
				// Compare DBRef numbers (ignoring creation time for now)
				return unlockerObj.Object().DBRef.Number == dbrefNum;
			}
			
			// Otherwise try exact name match
			// Check if the unlocker itself matches
			if (unlockerObj.Object().Name.Equals(target, StringComparison.OrdinalIgnoreCase))
				return true;
			
			// Check aliases
			if (unlockerObj.Aliases.Any(a => a.Equals(target, StringComparison.OrdinalIgnoreCase)))
				return true;
			
			return false;
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(targetIdentifier));
	}

	public override Expression VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		var value = context.@string().GetText();
		var attribute = context.attributeName().GetText();

		Func<AnySharpObject, string, string, bool> func = (unlockerObj, attrName, expectedValue) =>
		{
			var attrResult = med.Send(
					new GetAttributeServiceQuery(unlockerObj, unlockerObj, attrName, IAttributeService.AttributeMode.Execute, true),
					CancellationToken.None)
				.AsTask()
				.ConfigureAwait(false).GetAwaiter().GetResult();

			return attrResult.Match(
				attributes =>
				{
					if (!attributes.Any())
						return false;
						
					var actualValue = MModule.plainText(attributes.First().Value);
					
					// Handle comparison operators
					if (expectedValue.StartsWith('>'))
					{
						// Greater than comparison
						var compareValue = expectedValue.Substring(1);
						return string.Compare(actualValue, compareValue, StringComparison.OrdinalIgnoreCase) > 0;
					}
					else if (expectedValue.StartsWith('<'))
					{
						// Less than comparison
						var compareValue = expectedValue.Substring(1);
						return string.Compare(actualValue, compareValue, StringComparison.OrdinalIgnoreCase) < 0;
					}
					else if (expectedValue.Contains('*') || expectedValue.Contains('?'))
					{
						// Wildcard match
						var pattern = MModule.getWildcardMatchAsRegex2(expectedValue);
						return Regex.IsMatch(actualValue, pattern, RegexOptions.IgnoreCase);
					}
					else
					{
						// Exact match (case-insensitive)
						return actualValue.Equals(expectedValue, StringComparison.OrdinalIgnoreCase);
					}
				},
				none => false,
				error => false
			);
		};

		return Expression.Invoke(Expression.Constant(func), unlocker, Expression.Constant(attribute), Expression.Constant(value));
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