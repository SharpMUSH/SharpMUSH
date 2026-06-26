using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Visitor for compiling PennMUSH lock expressions into executable Expression trees.
/// Supports all 11 documented PennMUSH lock key types including name, owner, carry, attribute,
/// evaluation, indirect, DBRef list, IP/hostname, channel, and bit locks.
/// Uses mediator queries to access services without creating circular dependencies.
/// </summary>
/// <param name="med">Mediator for database and service queries</param>
/// <param name="gated">Expression parameter representing the object being locked</param>
/// <param name="unlocker">Expression parameter representing the object attempting to pass the lock</param>
public class SharpMUSHBooleanExpressionVisitor(
	IMediator med,
	ParameterExpression gated,
	ParameterExpression unlocker) : SharpMUSHBoolExpParserBaseVisitor<Expression>
{
	protected override Expression AggregateResult(Expression aggregate, Expression nextResult)
		=> new Expression[] { aggregate, nextResult }.FirstOrDefault(x => x is not null)
			?? Expression.Constant(false);

	// Compiled expressions for bit checks (flag, power, type)
	// Note: These use .GetAwaiter().GetResult() which is necessary for Expression trees
	// that cannot be async. This is acceptable technical debt in this context.

	private readonly Expression<Func<AnySharpObject, string, bool>> _hasFlag = (dbRef, flag)
		=> dbRef.Object().Flags.Value
			.AnyAsync(x => x.Name == flag || x.Symbol == flag, CancellationToken.None).AsTask().GetAwaiter().GetResult();

	private readonly Expression<Func<AnySharpObject, string, bool>> _hasPower = (dbRef, power)
		=> dbRef.Object().Powers.Value
			.AnyAsync(x => x.Name == power || x.Alias == power, CancellationToken.None).AsTask().GetAwaiter().GetResult();

	private readonly Expression<Func<AnySharpObject, string, bool>> _isType = (dbRef, type)
		=> dbRef.Object().Type == type;

	// For name matching, we need to convert the pattern to regex outside the expression tree
	// because MModule.getWildcardMatchAsRegex2 cannot be compiled into an expression tree
	private bool MatchesName(AnySharpObject dbRef, string pattern)
	{
		var regexPattern = MModule.getWildcardMatchAsRegex2(pattern);
		return Regex.IsMatch(dbRef.Object().Name, regexPattern, RegexOptions.IgnoreCase)
			|| (dbRef.Aliases != null && dbRef.Aliases.Any(alias => Regex.IsMatch(alias.Trim(), regexPattern, RegexOptions.IgnoreCase)));
	}

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
		Func<AnySharpObject, AnySharpObject, string, bool> func = (gatedObj, unlockerObj, target) =>
		{
			try
			{
				var unlockerOwner = unlockerObj.Object().Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
				var unlockerOwnerDbRef = unlockerOwner.Object.DBRef;

				// If target is "me", check if unlocker is owned by gated object's owner
				if (target.Equals("me", StringComparison.OrdinalIgnoreCase))
				{
					var gatedOwner = gatedObj.Object().Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
					return unlockerOwnerDbRef == gatedOwner.Object.DBRef;
				}

				// If target is a DBRef or objid like "#123" or "#123:timestamp", compare owner DBRefs
				var parsedTargetOpt = HelperFunctions.ParseDbRef(target);
if (parsedTargetOpt.IsSome())
{
					// Get the target object by DBRef (validates creation timestamp if objid format)
					var targetObjResult = med.Send(
							new GetObjectNodeQuery(parsedTargetOpt.AsValue()),
							CancellationToken.None)
						.AsTask()
						.ConfigureAwait(false).GetAwaiter().GetResult();

					if (targetObjResult.IsNone())
						return false;

					var targetObj = targetObjResult.Known();
					var targetOwner = targetObj.Object().Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
					return unlockerOwnerDbRef == targetOwner.Object.DBRef;
				}

				// Name-based lookup using mediator query
				// Note: Parser is null as substitutions should have been pre-evaluated
				var locateResult = med.Send(
					new LocateObjectQuery(gatedObj, gatedObj, target, LocateFlags.AbsoluteMatch),
					CancellationToken.None)
					.AsTask()
					.ConfigureAwait(false).GetAwaiter().GetResult();

				return locateResult.Match(
					player =>
					{
						var targetOwner = player.Object.Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
						return unlockerOwnerDbRef == targetOwner.Object.DBRef;
					},
					room =>
					{
						var targetOwner = room.Object.Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
						return unlockerOwnerDbRef == targetOwner.Object.DBRef;
					},
					exit =>
					{
						var targetOwner = exit.Object.Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
						return unlockerOwnerDbRef == targetOwner.Object.DBRef;
					},
					thing =>
					{
						var targetOwner = thing.Object.Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
						return unlockerOwnerDbRef == targetOwner.Object.DBRef;
					},
					none => false,
					error => false
				);
			}
			catch (Exception)
			{
				// Catch any errors during owner resolution (database access, null references, etc.)
				return false;
			}
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(targetName));
	}

	public override Expression VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
	{
		var targetName = context.@string().GetText();

		// PennMUSH OP_TCARRY: passes ONLY if unlocker CARRIES the target (not if IS the target)
		Func<AnySharpObject, string, bool> func = (unlockerObj, target) =>
		{

			// If target is a DBRef or objid like "#123" or "#123:timestamp", check if carrying that specific object
			var parsedCarryOpt = HelperFunctions.ParseDbRef(target);
if (parsedCarryOpt.IsSome())
{
				try
				{
					if (unlockerObj.IsContainer)
					{
						var searchDbRef = parsedCarryOpt.AsValue();
						var contents = unlockerObj.AsContainer.Content(med);
						return contents
							.AnyAsync(item => item.Object().DBRef.Matches(searchDbRef), CancellationToken.None)
							.AsTask().GetAwaiter().GetResult();
					}
				}
				catch (Exception)
				{
					// Catch any errors during inventory check
				}
				return false;
			}

			try
			{
				var locateResult = med.Send(
					new LocateObjectQuery(unlockerObj, unlockerObj, target, LocateFlags.OnlyMatchObjectsInLookerInventory),
					CancellationToken.None)
					.AsTask()
					.ConfigureAwait(false).GetAwaiter().GetResult();

				return locateResult.Match(
					player => true,
					room => true,
					exit => true,
					thing => true,
					none => false,
					error => false
				);
			}
			catch (Exception)
			{
				// Catch any errors during locate operation
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
	{
		var typeText = context.objectType().GetText().ToUpper().Trim();

		// Validate that the type is one of the four valid MUSH object types
		if (typeText != "PLAYER" && typeText != "THING" && typeText != "EXIT" && typeText != "ROOM")
		{
			// Return false constant if type is invalid
			return Expression.Constant(false);
		}

		return Expression.Invoke(_isType, unlocker, Expression.Constant(typeText));
	}

	public override Expression VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		var channelName = context.@string().GetText();

		// Channel locks check if the unlocker is a member of the specified channel
		Func<AnySharpObject, string, bool> func = (unlockerObj, channel) =>
		{
			try
			{
				// This checks if the unlocker (or their owner if they're an object) is on the channel
				var isOnChannelQuery = new IsOnChannelQuery(unlockerObj, channel);
				var result = med.Send(isOnChannelQuery, CancellationToken.None)
					.GetAwaiter()
					.GetResult();

				return result;
			}
			catch (Exception)
			{
				// If channel doesn't exist or any error occurs, lock fails
				return false;
			}
		};

		return Expression.Invoke(Expression.Constant(func), unlocker, Expression.Constant(channelName));
	}

	public override Expression VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		var attributeName = context.@string().GetText();

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

					foreach (var dbrefStr in dbrefs)
					{
						var parsedDbRef = HelperFunctions.ParseDbRef(dbrefStr);
						if (parsedDbRef.IsSome())
						{
							var lockDbRef = parsedDbRef.AsValue();

							// If lock specifies creation time (objid format), both number and timestamp must match
							// This prevents locks from matching recycled dbrefs after objects are destroyed
							if (lockDbRef.CreationMilliseconds.HasValue)
							{
								if ((lockDbRef.Number == unlockerDbRef.Number)
										&& (lockDbRef.CreationMilliseconds == unlockerDbRef.CreationMilliseconds))
								{
									return true;
								}
							}
							// If lock doesn't specify creation time (bare dbref), only match number for backward compatibility
							else
							{
								if (lockDbRef.Number == unlockerDbRef.Number)
								{
									return true;
								}
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
				var ownerTask = unlockerObj.Object().Owner.WithCancellation(CancellationToken.None);
				var owner = ownerTask.GetAwaiter().GetResult();

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
			catch (Exception)
			{
				// Catch any errors during IP lookup (attribute access, regex errors, etc.)
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
				var ownerTask = unlockerObj.Object().Owner.WithCancellation(CancellationToken.None);
				var owner = ownerTask.GetAwaiter().GetResult();

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
			catch (Exception)
			{
				// Catch any errors during hostname lookup (attribute access, regex errors, etc.)
				return false;
			}
		};

		return Expression.Invoke(Expression.Constant(func), unlocker, Expression.Constant(hostPattern));
	}

	public override Expression VisitNameExpr(SharpMUSHBoolExpParser.NameExprContext context)
	{
		var pattern = context.@string().GetText();

		// Create a lambda that calls our MatchesName method
		// We can't use Expression.Invoke with a method call directly, so we create a Func
		Func<AnySharpObject, AnySharpObject, string, bool> func = (gatedObj, unlockerObj, pat) =>
			MatchesName(unlockerObj, pat);

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(pattern));
	}

	public override Expression VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
	{
		// Reconstruct full identifier including optional :timestamp for objid format
		var targetIdentifier = context.ATTRIBUTE_COLON() != null
			? $"{context.@string(0).GetText()}:{context.@string(1).GetText()}"
			: context.@string(0).GetText();
		return BuildExactObjectExpression(targetIdentifier);
	}

	public override Expression VisitDefaultExpr(SharpMUSHBoolExpParser.DefaultExprContext context)
	{
		var targetIdentifier = context.@string().GetText();
		return BuildExactObjectExpression(targetIdentifier);
	}

	private Expression BuildExactObjectExpression(string targetIdentifier)
	{
		// PennMUSH OP_TCONST: passes if unlocker IS the target OR unlocker CARRIES the target
		Func<AnySharpObject, AnySharpObject, string, bool> func = (gatedObj, unlockerObj, target) =>
		{
			// If target is "me", it refers to the gated object's owner
			if (target.Equals("me", StringComparison.OrdinalIgnoreCase))
			{
				var ownerTask = gatedObj.Object().Owner.WithCancellation(CancellationToken.None);
				var owner = ownerTask.GetAwaiter().GetResult();
				return unlockerObj.Object().DBRef == owner.Object.DBRef;
			}

			// Try to parse as DBRef (supports both #123 and #123:timestamp formats)
			var parsedDbRef = HelperFunctions.ParseDbRef(target);
			if (parsedDbRef.IsSome())
			{
				var lockDbRef = parsedDbRef.AsValue();
				var unlockerDbRef = unlockerObj.Object().DBRef;

				// Check if unlocker IS the target
				bool isMatch = lockDbRef.CreationMilliseconds.HasValue
					? (lockDbRef.Number == unlockerDbRef.Number && lockDbRef.CreationMilliseconds == unlockerDbRef.CreationMilliseconds)
					: lockDbRef.Number == unlockerDbRef.Number;

				if (isMatch)
					return true;

				// Check if unlocker CARRIES the target (PennMUSH: member(arg, Contents(player)))
				try
				{
					if (unlockerObj.IsContainer)
					{
						var contents = unlockerObj.AsContainer.Content(med);
						return contents
							.AnyAsync(item => item.Object().DBRef.Matches(lockDbRef), CancellationToken.None)
							.AsTask().GetAwaiter().GetResult();
					}
				}
				catch (Exception)
				{
					// Catch errors during inventory check
				}

				return false;
			}

			// Name-based targets should have been resolved to dbrefs at @lock time
			// by the normalization visitor. If we reach here with a non-dbref value,
			// the lock was set without name resolution (e.g. programmatically) —
			// treat as no match, matching PennMUSH behavior.
			return false;
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(targetIdentifier));
	}

	public override Expression VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		var attribute = context.@string(0).GetText();
		var value = context.@string(1).GetText();

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

					if (expectedValue.StartsWith('>'))
					{
						var compareValue = expectedValue.Substring(1);
						return string.Compare(actualValue, compareValue, StringComparison.OrdinalIgnoreCase) > 0;
					}
					else if (expectedValue.StartsWith('<'))
					{
						var compareValue = expectedValue.Substring(1);
						return string.Compare(actualValue, compareValue, StringComparison.OrdinalIgnoreCase) < 0;
					}
					else if (expectedValue.Contains('*') || expectedValue.Contains('?'))
					{
						var pattern = MModule.getWildcardMatchAsRegex2(expectedValue);
						return Regex.IsMatch(actualValue, pattern, RegexOptions.IgnoreCase);
					}
					else
					{
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
		var attribute = context.@string(0).GetText();
		var expectedValue = context.@string(1).GetText();

		// PennMUSH eval lock (ATTR/pattern): evaluate the attribute on the gated object
		// as MUSHcode with the unlocker as enactor (%#), then compare result to pattern.
		Func<AnySharpObject, AnySharpObject, string, string, bool> func = (gatedObj, unlockerObj, attrName, expected) =>
		{
			// Use the mediator query to evaluate the attribute as MUSHcode
			var evalResult = med.Send(
					new EvaluateAttributeForLockQuery(gatedObj, unlockerObj, attrName),
					CancellationToken.None)
				.AsTask()
				.ConfigureAwait(false).GetAwaiter().GetResult();

			if (evalResult == null)
				return false;

			// Compare with expected value (case-insensitive, per PennMUSH strcasecmp)
			return evalResult.Equals(expected, StringComparison.OrdinalIgnoreCase);
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(attribute), Expression.Constant(expectedValue));
	}

	public override Expression VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
	{
		var targetName = context.@string(0).GetText();
		var lockName = context.@string().Length > 1 ? context.@string(1).GetText() : "Basic"; // Default to Basic lock if not specified

		// Indirect locks check another object's lock
		// @object means check the Basic lock on object
		// @object/lockname means check the specific lock on object
		Func<AnySharpObject, AnySharpObject, string, string, bool> func = (gatedObj, unlockerObj, target, lockType) =>
		{
			try
			{
				AnySharpObject? targetObj = null;

				// If target is a DBRef or objid like "#123" or "#123:timestamp", resolve it
				var parsedIndirectOpt = HelperFunctions.ParseDbRef(target);
if (parsedIndirectOpt.IsSome())
{
					// Validates creation timestamp if objid format
					var targetObjResult = med.Send(
							new GetObjectNodeQuery(parsedIndirectOpt.AsValue()),
							CancellationToken.None)
						.AsTask()
						.ConfigureAwait(false).GetAwaiter().GetResult();

					if (targetObjResult.IsNone())
						return false;

					targetObj = targetObjResult.Known();
				}
				else
				{
					// Name-based lookup using mediator query
					var locateResult = med.Send(
						new LocateObjectQuery(gatedObj, gatedObj, target, LocateFlags.AbsoluteMatch),
						CancellationToken.None)
						.AsTask()
						.ConfigureAwait(false).GetAwaiter().GetResult();

					var found = locateResult.Match(
						player => { targetObj = player; return true; },
						room => { targetObj = room; return true; },
						exit => { targetObj = exit; return true; },
						thing => { targetObj = thing; return true; },
						none => false,
						error => false);

					if (!found)
					{
						return false;
					}
				}

				if (targetObj == null)
					return false;

				// Get the lock from the target object
				var lockData = targetObj.Object().Locks.GetValueOrDefault(lockType, new Library.Models.SharpLockData("#TRUE"));
				var lockString = lockData.LockString;

				// Use mediator query to recursively evaluate the lock
				// This breaks the circular dependency between parser and lock service
				var evaluateResult = med.Send(
					new EvaluateLockQuery(lockString, targetObj, unlockerObj),
					CancellationToken.None)
					.AsTask()
					.ConfigureAwait(false).GetAwaiter().GetResult();

				return evaluateResult;
			}
			catch (Exception)
			{
				// Catch any errors during indirect lock resolution (database access, etc.)
				return false;
			}
		};

		return Expression.Invoke(Expression.Constant(func), gated, unlocker, Expression.Constant(targetName), Expression.Constant(lockName));
	}

	public override Expression VisitString(SharpMUSHBoolExpParser.StringContext context) =>
		throw new ArgumentException("Parser should never reach here.");
}