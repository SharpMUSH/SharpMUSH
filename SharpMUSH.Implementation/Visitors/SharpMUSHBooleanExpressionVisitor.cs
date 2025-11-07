using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
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
		
		// For owner locks, check if the unlocker is owned by the owner of the named object
		// This is a simplified implementation focusing on DBRef-based owner checks
		Func<AnySharpObject, AnySharpObject, string, bool> func = (gatedObj, unlockerObj, target) =>
		{
			try
			{
				// Get the owner of the unlocker
				var unlockerOwner = unlockerObj.Object().Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
				var unlockerOwnerDbRef = unlockerOwner.Object.DBRef;
				
				// If target is "me", check if unlocker is owned by gated object's owner
				if (target.Equals("me", StringComparison.OrdinalIgnoreCase))
				{
					var gatedOwner = gatedObj.Object().Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
					return unlockerOwnerDbRef == gatedOwner.Object.DBRef;
				}
				
				// If target is a DBRef like "#123", compare owner DBRefs
				if (target.StartsWith('#') && int.TryParse(target.Substring(1), out int targetDbrefNum))
				{
					// Get the target object by DBRef and check if same owner
					var targetObjResult = med.Send(
							new GetObjectNodeQuery(new DBRef(targetDbrefNum)),
							CancellationToken.None)
						.AsTask()
						.ConfigureAwait(false).GetAwaiter().GetResult();
					
					if (targetObjResult.IsNone())
						return false;
						
					var targetObj = targetObjResult.Known();
					var targetOwner = targetObj.Object().Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
					return unlockerOwnerDbRef == targetOwner.Object.DBRef;
				}
				
				// For name-based lookup, we would need database query which isn't practical here
				return false;
			}
			catch
			{
				return false;
			}
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(targetName));
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
		var channelName = context.@string().GetText();

		// Channel locks check if the unlocker is a member of the specified channel
		Func<AnySharpObject, string, bool> func = (unlockerObj, channel) =>
		{
			// TODO: Full implementation requires channel system integration
			// For now, return false as a placeholder
			// This would need to:
			// 1. Query the channel system for channel membership
			// 2. Check if unlocker (or unlocker's owner) is on the channel
			return false;
		};

		return Expression.Invoke(Expression.Constant(func), unlocker, Expression.Constant(channelName));
	}

	public override Expression VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		var attributeName = context.attributeName().GetText();

		// DBRef list locks check if the unlocker's dbref is in a space-separated list stored in an attribute
		Func<AnySharpObject, AnySharpObject, string, bool> func = (gatedObj, unlockerObj, attrName) =>
		{
			var attrResult = med.Send(
					new GetAttributeServiceQuery(gatedObj, gatedObj, attrName, IAttributeService.AttributeMode.Execute, true),
					CancellationToken.None)
				.AsTask()
				.ConfigureAwait(false).GetAwaiter().GetResult();

			return attrResult.Match(
				attributes =>
				{
					if (!attributes.Any())
						return false;
						
					var listValue = MModule.plainText(attributes.First().Value);
					var dbrefs = listValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					var unlockerDbRef = unlockerObj.Object().DBRef;
					
					// Check if unlocker's dbref is in the list
					foreach (var dbrefStr in dbrefs)
					{
						// Handle both #123 and #123:timestamp formats
						if (dbrefStr.StartsWith('#'))
						{
							var colonIndex = dbrefStr.IndexOf(':');
							var numStr = colonIndex > 0 ? dbrefStr.Substring(1, colonIndex - 1) : dbrefStr.Substring(1);
							
							if (int.TryParse(numStr, out int dbrefNum))
							{
								if (unlockerDbRef.Number == dbrefNum)
									return true;
							}
						}
					}
					
					return false;
				},
				none => false,
				error => false
			);
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(attributeName));
	}

	public override Expression VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
	{
		var ipPattern = context.@string().GetText();

		// IP locks check if the unlocker's owner is connected from a matching IP address
		Func<AnySharpObject, string, bool> func = (unlockerObj, pattern) =>
		{
			try
			{
				// Get the owner of the unlocker
				var ownerTask = unlockerObj.Object().Owner.WithCancellation(CancellationToken.None);
				var owner = ownerTask.GetAwaiter().GetResult();
				
				// Get the LASTIP attribute from the owner
				var attrResult = med.Send(
						new GetAttributeServiceQuery(owner, owner, "LASTIP", IAttributeService.AttributeMode.Execute, true),
						CancellationToken.None)
					.AsTask()
					.ConfigureAwait(false).GetAwaiter().GetResult();

				return attrResult.Match(
					attributes =>
					{
						if (!attributes.Any())
							return false;
							
						var actualIp = MModule.plainText(attributes.First().Value);
						
						// Use wildcard matching for IP pattern
						var regexPattern = MModule.getWildcardMatchAsRegex2(pattern);
						return Regex.IsMatch(actualIp, regexPattern, RegexOptions.IgnoreCase);
					},
					none => false,
					error => false
				);
			}
			catch
			{
				return false;
			}
		};

		return Expression.Invoke(Expression.Constant(func), unlocker, Expression.Constant(ipPattern));
	}

	public override Expression VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
	{
		var hostPattern = context.@string().GetText();

		// Hostname locks check if the unlocker's owner is connected from a matching hostname
		Func<AnySharpObject, string, bool> func = (unlockerObj, pattern) =>
		{
			try
			{
				// Get the owner of the unlocker
				var ownerTask = unlockerObj.Object().Owner.WithCancellation(CancellationToken.None);
				var owner = ownerTask.GetAwaiter().GetResult();
				
				// Get the LASTSITE attribute from the owner
				var attrResult = med.Send(
						new GetAttributeServiceQuery(owner, owner, "LASTSITE", IAttributeService.AttributeMode.Execute, true),
						CancellationToken.None)
					.AsTask()
					.ConfigureAwait(false).GetAwaiter().GetResult();

				return attrResult.Match(
					attributes =>
					{
						if (!attributes.Any())
							return false;
							
						var actualHost = MModule.plainText(attributes.First().Value);
						
						// Use wildcard matching for hostname pattern
						var regexPattern = MModule.getWildcardMatchAsRegex2(pattern);
						return Regex.IsMatch(actualHost, regexPattern, RegexOptions.IgnoreCase);
					},
					none => false,
					error => false
				);
			}
			catch
			{
				return false;
			}
		};

		return Expression.Invoke(Expression.Constant(func), unlocker, Expression.Constant(hostPattern));
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
		var expectedValue = context.@string().GetText();
		var attribute = context.attributeName().GetText();

		// Evaluation locks evaluate an attribute on the gated object (not the unlocker)
		// The result is compared to the expected value
		// %# is the unlocker, %! is the gated object during evaluation
		Func<AnySharpObject, AnySharpObject, string, string, bool> func = (gatedObj, unlockerObj, attrName, expected) =>
		{
			var attrResult = med.Send(
					new GetAttributeServiceQuery(gatedObj, gatedObj, attrName, IAttributeService.AttributeMode.Execute, true),
					CancellationToken.None)
				.AsTask()
				.ConfigureAwait(false).GetAwaiter().GetResult();

			return attrResult.Match(
				attributes =>
				{
					if (!attributes.Any())
						return false;
						
					// Get the attribute value and evaluate it
					// TODO: The attribute should be evaluated with %# = unlocker, %! = gated object
					// For now, we just get the plaintext value
					var actualValue = MModule.plainText(attributes.First().Value);
					
					// Compare with expected value (case-insensitive)
					return actualValue.Equals(expected, StringComparison.OrdinalIgnoreCase);
				},
				none => false,
				error => false
			);
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(attribute), Expression.Constant(expectedValue));
	}

	public override Expression VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
	{
		var targetName = context.@string().GetText();
		var lockName = context.attributeName()?.GetText() ?? "Basic"; // Default to Basic lock if not specified

		// Indirect locks check another object's lock
		// @object means check the Basic lock on object
		// @object/lockname means check the specific lock on object
		Func<AnySharpObject, AnySharpObject, string, string, bool> func = (gatedObj, unlockerObj, target, lockType) =>
		{
			try
			{
				// If target is a DBRef like "#123", resolve and evaluate its lock
				if (target.StartsWith('#') && int.TryParse(target.Substring(1), out int targetDbrefNum))
				{
					var targetObjResult = med.Send(
							new GetObjectNodeQuery(new DBRef(targetDbrefNum)),
							CancellationToken.None)
						.AsTask()
						.ConfigureAwait(false).GetAwaiter().GetResult();
					
					if (targetObjResult.IsNone())
						return false;
						
					var targetObj = targetObjResult.Known();
					// Get the lock from the target object
					var lockString = targetObj.Object().Locks.GetValueOrDefault(lockType, "#TRUE");
					
					// We would need the BooleanExpressionParser to compile and evaluate this lock
					// This creates a circular dependency issue
					// For now, return false as placeholder
					// TODO: Need to refactor to allow recursive lock evaluation
					return false;
				}
				
				// For name-based lookup, we would need database query which isn't practical here
				return false;
			}
			catch
			{
				return false;
			}
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(targetName), Expression.Constant(lockName));
	}

	public override Expression VisitString(SharpMUSHBoolExpParser.StringContext context) =>
		throw new ArgumentException("Parser should never reach here.");

	public override Expression VisitAttributeName(SharpMUSHBoolExpParser.AttributeNameContext context) =>
		throw new ArgumentException("Parser should never reach here.");
}