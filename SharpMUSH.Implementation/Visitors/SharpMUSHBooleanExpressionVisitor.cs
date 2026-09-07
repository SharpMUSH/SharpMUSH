using SharpMUSH.Library.Markup;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;
using LockPredicate =
	System.Func<SharpMUSH.Library.DiscriminatedUnions.AnySharpObject,
		SharpMUSH.Library.DiscriminatedUnions.AnySharpObject, System.Threading.Tasks.ValueTask<bool>>;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Visitor for compiling PennMUSH lock expressions into an executable async delegate.
/// Supports all 11 documented PennMUSH lock key types including name, owner, carry, attribute,
/// evaluation, indirect, DBRef list, IP/hostname, channel, and bit locks.
/// Uses mediator queries to access services without creating circular dependencies.
/// <para>
/// Leaves are ordinary async lambdas and the operators are ordinary C#, so every database read a
/// lock makes is awaited rather than blocked on, and nothing is emitted as IL at runtime. The
/// short-circuit of <c>&amp;</c> and <c>|</c> is the language's own, so the right operand of
/// <c>a&amp;b</c> is not evaluated — and its database reads not made — when <c>a</c> is false.
/// </para>
/// </summary>
/// <param name="services">The locate, attribute and lock services a compiled lock reaches at evaluation time</param>
/// <param name="med">Mediator for database queries</param>
public class SharpMUSHBooleanExpressionVisitor(
	ILockEvaluationServices services,
	IMediator med) : SharpMUSHBoolExpParserBaseVisitor<LockPredicate>
{
	private static readonly ValueTask<bool> FalseResult = ValueTask.FromResult(false);
	private static readonly ValueTask<bool> TrueResult = ValueTask.FromResult(true);

	private static readonly LockPredicate False = (_, _) => FalseResult;
	private static readonly LockPredicate True = (_, _) => TrueResult;

	protected override LockPredicate AggregateResult(LockPredicate aggregate, LockPredicate nextResult)
		=> aggregate ?? nextResult ?? False;

	private static ValueTask<bool> HasFlag(AnySharpObject dbRef, string flag)
		=> dbRef.Object().Flags.Value
			.AnyAsync(x => x.Name == flag || x.Symbol == flag, CancellationToken.None);

	private static ValueTask<bool> HasPower(AnySharpObject dbRef, string power)
		=> dbRef.Object().Powers.Value
			.AnyAsync(x => x.Name == power || x.Alias == power, CancellationToken.None);

	// A lock is evaluated on every movement and every permission check, so the pattern is built once
	// and shared rather than rebuilt per evaluation, and it carries the wildcard match bound with it.
	private static bool MatchesName(AnySharpObject dbRef, string pattern)
	{
		var regex = SoftcodeRegex.Wildcard(pattern);
		return SoftcodeRegex.IsMatch(regex, dbRef.Object().Name)
			|| (dbRef.Aliases != null && dbRef.Aliases.Any(alias => SoftcodeRegex.IsMatch(regex, alias.Trim())));
	}

	public override LockPredicate VisitLock(SharpMUSHBoolExpParser.LockContext context)
		=> VisitChildren(context);

	public override LockPredicate VisitLockExprList(SharpMUSHBoolExpParser.LockExprListContext context)
		=> VisitChildren(context);

	public override LockPredicate VisitLockAndExpr(SharpMUSHBoolExpParser.LockAndExprContext context)
		=> Fold(context.lockExpr(), AndAlso);

	public override LockPredicate VisitLockOrExpr(SharpMUSHBoolExpParser.LockOrExprContext context)
		=> Fold(context.lockAndExpr(), OrElse);

	/// <summary>
	/// <c>a &amp; b</c>. The short-circuit is the language's own: <paramref name="right"/> is not
	/// invoked at all — and makes no database read — when <paramref name="left"/> is false.
	/// <para>
	/// Written as a synchronous combinator with an async slow path rather than one <c>async</c>
	/// lambda, because after the attribute and flag caches landed (#867/#869) the operand is
	/// usually already complete, and an <c>async</c> method pays for its state machine either way.
	/// Measured over an 8-term lock whose leaves all answer from cache: 79 ns for the plain
	/// <c>async</c> composition against 18 ns for this one, on a path where the surrounding
	/// FusionCache lookup costs ~89 ns.
	/// </para>
	/// </summary>
	private static LockPredicate AndAlso(LockPredicate left, LockPredicate right)
		=> (gatedObj, unlockerObj) =>
		{
			var first = left(gatedObj, unlockerObj);
			if (!first.IsCompletedSuccessfully)
			{
				return AndAlsoAwaited(first, right, gatedObj, unlockerObj);
			}

			return first.Result ? right(gatedObj, unlockerObj) : FalseResult;
		};

	private static async ValueTask<bool> AndAlsoAwaited(ValueTask<bool> first, LockPredicate right,
		AnySharpObject gatedObj, AnySharpObject unlockerObj)
		=> await first && await right(gatedObj, unlockerObj);

	/// <summary>
	/// <c>a | b</c>, on the same terms as <see cref="AndAlso"/>: <paramref name="right"/> is not
	/// evaluated when <paramref name="left"/> is true.
	/// </summary>
	private static LockPredicate OrElse(LockPredicate left, LockPredicate right)
		=> (gatedObj, unlockerObj) =>
		{
			var first = left(gatedObj, unlockerObj);
			if (!first.IsCompletedSuccessfully)
			{
				return OrElseAwaited(first, right, gatedObj, unlockerObj);
			}

			return first.Result ? TrueResult : right(gatedObj, unlockerObj);
		};

	private static async ValueTask<bool> OrElseAwaited(ValueTask<bool> first, LockPredicate right,
		AnySharpObject gatedObj, AnySharpObject unlockerObj)
		=> await first || await right(gatedObj, unlockerObj);

	/// <summary>
	/// Combines the operands of one precedence level left-to-right. A single operand passes
	/// through untouched, so the `a` in `a | b` and the bare `a` produce identical delegates.
	/// Folding left keeps short-circuit evaluation in source order.
	/// </summary>
	private LockPredicate Fold<T>(T[] operands, Func<LockPredicate, LockPredicate, LockPredicate> combine)
		where T : Antlr4.Runtime.ParserRuleContext
	{
		var result = Visit(operands[0]);
		for (var i = 1; i < operands.Length; i++)
		{
			result = combine(result, Visit(operands[i]));
		}

		return result;
	}

	public override LockPredicate VisitLockExpr(SharpMUSHBoolExpParser.LockExprContext context)
		=> VisitChildren(context);

	public override LockPredicate VisitNotExpr(SharpMUSHBoolExpParser.NotExprContext context)
	{
		var inner = Visit(context.lockExpr());
		return (gatedObj, unlockerObj) =>
		{
			var result = inner(gatedObj, unlockerObj);
			return result.IsCompletedSuccessfully
				? result.Result ? FalseResult : TrueResult
				: NotAwaited(result);
		};
	}

	private static async ValueTask<bool> NotAwaited(ValueTask<bool> result) => !await result;

	public override LockPredicate VisitFalseExpr(SharpMUSHBoolExpParser.FalseExprContext context)
		=> False;

	public override LockPredicate VisitTrueExpr(SharpMUSHBoolExpParser.TrueExprContext context)
		=> True;

	public override LockPredicate VisitEnclosedExpr(SharpMUSHBoolExpParser.EnclosedExprContext context)
		=> Visit(context.lockExprList());

	public override LockPredicate VisitOwnerExpr(SharpMUSHBoolExpParser.OwnerExprContext context)
	{
		var target = context.@string().GetText();

		// For owner locks, check if the unlocker is owned by the owner of the named object
		return async (gatedObj, unlockerObj) =>
		{
			try
			{
				var unlockerOwner = await unlockerObj.Object().Owner.WithCancellation(CancellationToken.None);
				var unlockerOwnerDbRef = unlockerOwner.Object.DBRef;

				// If target is "me", check if unlocker is owned by gated object's owner
				if (target.Equals("me", StringComparison.OrdinalIgnoreCase))
				{
					var gatedOwner = await gatedObj.Object().Owner.WithCancellation(CancellationToken.None);
					return unlockerOwnerDbRef == gatedOwner.Object.DBRef;
				}

				// If target is a DBRef or objid like "#123" or "#123:timestamp", compare owner DBRefs
				var parsedTargetOpt = HelperFunctions.ParseDbRef(target);
				if (parsedTargetOpt.IsSome())
				{
					// Get the target object by DBRef (validates creation timestamp if objid format)
					var targetObjResult = await med.Send(
						new GetObjectNodeQuery(parsedTargetOpt.AsValue()),
						CancellationToken.None);

					if (targetObjResult.IsNone())
						return false;

					var targetOwner = await targetObjResult.Known().Object().Owner.WithCancellation(CancellationToken.None);
					return unlockerOwnerDbRef == targetOwner.Object.DBRef;
				}

				// Name-based lookup using mediator query
				// Note: Parser is null as substitutions should have been pre-evaluated
				// A name, not a dbref — the dbref case returned above. AbsoluteMatch names no scope, so on
				// its own it could only ever resolve "#N", which is the branch that already ran.
				var locateResult = await services.LocateAsync(gatedObj, gatedObj, target, LocateFlags.All);

				if (!locateResult.IsValid())
					return false;

				var located = locateResult.WithoutError().WithoutNone();
				var locatedOwner = await located.Object().Owner.WithCancellation(CancellationToken.None);
				return unlockerOwnerDbRef == locatedOwner.Object.DBRef;
			}
			catch (Exception)
			{
				// Catch any errors during owner resolution (database access, null references, etc.)
				return false;
			}
		};
	}

	public override LockPredicate VisitCarryExpr(SharpMUSHBoolExpParser.CarryExprContext context)
	{
		var target = context.@string().GetText();

		// PennMUSH OP_TCARRY: passes ONLY if unlocker CARRIES the target (not if IS the target)
		return async (_, unlockerObj) =>
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
						return await unlockerObj.AsContainer.Content(med)
							.AnyAsync(item => item.Object().DBRef.Matches(searchDbRef), CancellationToken.None);
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
				// MAT_POSSESSION | MAT_CONTENTS — PennMUSH's MAT_OBJ_CONTENTS shape. MAT_CONTENTS on its own
				// is a filter over whatever the scopes turn up, not a scope, so it names nowhere to look.
				var locateResult = await services.LocateAsync(unlockerObj, unlockerObj, target,
					LocateFlags.MatchObjectsInLookerInventory | LocateFlags.OnlyMatchObjectsInLookerInventory);

				return locateResult.IsValid();
			}
			catch (Exception)
			{
				// Catch any errors during locate operation
				return false;
			}
		};
	}

	public override LockPredicate VisitBitFlagExpr(SharpMUSHBoolExpParser.BitFlagExprContext context)
	{
		var flag = context.@string().GetText().ToUpper().Trim();
		return (_, unlockerObj) => HasFlag(unlockerObj, flag);
	}

	public override LockPredicate VisitBitPowerExpr(SharpMUSHBoolExpParser.BitPowerExprContext context)
	{
		var power = context.@string().GetText().ToUpper().Trim();
		return (_, unlockerObj) => HasPower(unlockerObj, power);
	}

	public override LockPredicate VisitBitTypeExpr(SharpMUSHBoolExpParser.BitTypeExprContext context)
	{
		var typeText = context.objectType().GetText().ToUpper().Trim();

		// Validate that the type is one of the four valid MUSH object types
		if (typeText != "PLAYER" && typeText != "THING" && typeText != "EXIT" && typeText != "ROOM")
		{
			return False;
		}

		return (_, unlockerObj) => ValueTask.FromResult(unlockerObj.Object().Type == typeText);
	}

	public override LockPredicate VisitChannelExpr(SharpMUSHBoolExpParser.ChannelExprContext context)
	{
		var channel = context.@string().GetText();

		// Channel locks check if the unlocker is a member of the specified channel
		return async (_, unlockerObj) =>
		{
			try
			{
				// This checks if the unlocker (or their owner if they're an object) is on the channel
				return await med.Send(new IsOnChannelQuery(unlockerObj, channel), CancellationToken.None);
			}
			catch (Exception)
			{
				// If channel doesn't exist or any error occurs, lock fails
				return false;
			}
		};
	}

	public override LockPredicate VisitDbRefListExpr(SharpMUSHBoolExpParser.DbRefListExprContext context)
	{
		var attrName = context.@string().GetText();

		// DBRef list locks check if the unlocker's dbref is in a space-separated list stored in an attribute
		return async (gatedObj, unlockerObj) =>
		{
			var attrResult = await services.GetAttributeAsync(gatedObj, gatedObj, attrName,
				IAttributeService.AttributeMode.Execute, true);

			return attrResult.Match(
				attributes =>
				{
					if (!attributes.Any())
						return false;

					var listValue = attributes.First().Value.ToPlainText();
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
	}

	public override LockPredicate VisitIpExpr(SharpMUSHBoolExpParser.IpExprContext context)
		=> ConnectionAttributeMatch("LASTIP", context.@string().GetText());

	public override LockPredicate VisitHostNameExpr(SharpMUSHBoolExpParser.HostNameExprContext context)
		=> ConnectionAttributeMatch("LASTSITE", context.@string().GetText());

	/// <summary>
	/// IP and hostname locks are the same key with a different attribute: read
	/// <paramref name="attributeName"/> off the unlocker's owner and wildcard-match it.
	/// </summary>
	private LockPredicate ConnectionAttributeMatch(string attributeName, string pattern)
		=> async (_, unlockerObj) =>
		{
			try
			{
				var owner = await unlockerObj.Object().Owner.WithCancellation(CancellationToken.None);

				var attrResult = await services.GetAttributeAsync(owner, owner, attributeName,
					IAttributeService.AttributeMode.Execute, true);

				return attrResult.Match(
					attributes =>
					{
						if (!attributes.Any())
							return false;

						var actual = attributes.First().Value.ToPlainText();

						return SoftcodeRegex.IsMatch(SoftcodeRegex.Wildcard(pattern), actual);
					},
					none => false,
					error => false
				);
			}
			catch (Exception)
			{
				// Catch any errors during lookup (attribute access, regex errors, etc.)
				return false;
			}
		};

	public override LockPredicate VisitNameExpr(SharpMUSHBoolExpParser.NameExprContext context)
	{
		var pattern = context.@string().GetText();
		return (_, unlockerObj) => ValueTask.FromResult(MatchesName(unlockerObj, pattern));
	}

	public override LockPredicate VisitExactObjectExpr(SharpMUSHBoolExpParser.ExactObjectExprContext context)
	{
		// Reconstruct full identifier including optional :timestamp for objid format
		var targetIdentifier = context.ATTRIBUTE_COLON() != null
			? $"{context.@string(0).GetText()}:{context.@string(1).GetText()}"
			: context.@string(0).GetText();
		return BuildExactObjectPredicate(targetIdentifier);
	}

	public override LockPredicate VisitDefaultExpr(SharpMUSHBoolExpParser.DefaultExprContext context)
		=> BuildExactObjectPredicate(context.@string().GetText());

	private LockPredicate BuildExactObjectPredicate(string target)
		// PennMUSH OP_TCONST: passes if unlocker IS the target OR unlocker CARRIES the target
		=> async (gatedObj, unlockerObj) =>
		{
			// If target is "me", it refers to the gated object's owner
			if (target.Equals("me", StringComparison.OrdinalIgnoreCase))
			{
				var owner = await gatedObj.Object().Owner.WithCancellation(CancellationToken.None);
				return unlockerObj.Object().DBRef == owner.Object.DBRef;
			}

			// Try to parse as DBRef (supports both #123 and #123:timestamp formats)
			var parsedDbRef = HelperFunctions.ParseDbRef(target);
			if (parsedDbRef.IsSome())
			{
				var lockDbRef = parsedDbRef.AsValue();
				var unlockerDbRef = unlockerObj.Object().DBRef;

				// Check if unlocker IS the target
				var isMatch = lockDbRef.CreationMilliseconds.HasValue
					? (lockDbRef.Number == unlockerDbRef.Number && lockDbRef.CreationMilliseconds == unlockerDbRef.CreationMilliseconds)
					: lockDbRef.Number == unlockerDbRef.Number;

				if (isMatch)
					return true;

				// Check if unlocker CARRIES the target (PennMUSH: member(arg, Contents(player)))
				try
				{
					if (unlockerObj.IsContainer)
					{
						return await unlockerObj.AsContainer.Content(med)
							.AnyAsync(item => item.Object().DBRef.Matches(lockDbRef), CancellationToken.None);
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

	public override LockPredicate VisitAttributeExpr(SharpMUSHBoolExpParser.AttributeExprContext context)
	{
		var attrName = context.@string(0).GetText();
		var expectedValue = context.@string(1).GetText();

		return async (_, unlockerObj) =>
		{
			var attrResult = await services.GetAttributeAsync(unlockerObj, unlockerObj, attrName,
				IAttributeService.AttributeMode.Execute, true);

			return attrResult.Match(
				attributes =>
				{
					if (!attributes.Any())
						return false;

					var actualValue = attributes.First().Value.ToPlainText();

					if (expectedValue.StartsWith('>'))
					{
						return string.Compare(actualValue, expectedValue[1..], StringComparison.OrdinalIgnoreCase) > 0;
					}

					if (expectedValue.StartsWith('<'))
					{
						return string.Compare(actualValue, expectedValue[1..], StringComparison.OrdinalIgnoreCase) < 0;
					}

					if (expectedValue.Contains('*') || expectedValue.Contains('?'))
					{
						return SoftcodeRegex.IsMatch(SoftcodeRegex.Wildcard(expectedValue), actualValue);
					}

					return actualValue.Equals(expectedValue, StringComparison.OrdinalIgnoreCase);
				},
				none => false,
				error => false
			);
		};
	}

	public override LockPredicate VisitEvaluationExpr(SharpMUSHBoolExpParser.EvaluationExprContext context)
	{
		var attrName = context.@string(0).GetText();
		var expected = context.@string(1).GetText();

		// PennMUSH eval lock (ATTR/pattern): evaluate the attribute on the gated object
		// as MUSHcode with the unlocker as enactor (%#), then compare result to pattern.
		return async (gatedObj, unlockerObj) =>
		{
			var evalResult = await services.EvaluateAttributeAsync(gatedObj, unlockerObj, attrName);

			return evalResult.Match(
				// Compare with expected value (case-insensitive, per PennMUSH strcasecmp)
				value => value.Equals(expected, StringComparison.OrdinalIgnoreCase),
				// An evaluation that could not run denies, deliberately and in one place. PennMUSH's
				// check_attrib_lock() (src/boolexp.c) returns 0 for every way the evaluation can fail —
				// no attribute name, no comparison string, no such attribute — and pennlock.hlp says of a
				// permission failure inside the eval that "the person will automatically fail to pass the
				// lock". A lock that cannot be evaluated is a lock that has not been passed.
				_ => false);
		};
	}

	public override LockPredicate VisitIndirectExpr(SharpMUSHBoolExpParser.IndirectExprContext context)
	{
		var target = context.@string(0).GetText();
		var lockType = context.@string().Length > 1 ? context.@string(1).GetText() : "Basic"; // Default to Basic lock if not specified

		// Indirect locks check another object's lock
		// @object means check the Basic lock on object
		// @object/lockname means check the specific lock on object
		return async (gatedObj, unlockerObj) =>
		{
			try
			{
				AnySharpObject targetObj;

				// If target is a DBRef or objid like "#123" or "#123:timestamp", resolve it
				var parsedIndirectOpt = HelperFunctions.ParseDbRef(target);
				if (parsedIndirectOpt.IsSome())
				{
					// Validates creation timestamp if objid format
					var targetObjResult = await med.Send(
						new GetObjectNodeQuery(parsedIndirectOpt.AsValue()),
						CancellationToken.None);

					if (targetObjResult.IsNone())
						return false;

					targetObj = targetObjResult.Known();
				}
				else
				{
					// Name-based lookup using mediator query — again, the dbref case is handled above, so
					// AbsoluteMatch on its own would leave this branch with nowhere to search.
					var locateResult = await services.LocateAsync(gatedObj, gatedObj, target, LocateFlags.All);

					if (!locateResult.IsValid())
						return false;

					targetObj = locateResult.WithoutError().WithoutNone();
				}

				// Get the lock from the target object
				var lockData = targetObj.Object().Locks.GetValueOrDefault(lockType, new SharpLockData("#TRUE"));

				return await services.EvaluateLock(lockData.LockString, targetObj, unlockerObj);
			}
			catch (Exception)
			{
				// Catch any errors during indirect lock resolution (database access, etc.)
				return false;
			}
		};
	}

	public override LockPredicate VisitString(SharpMUSHBoolExpParser.StringContext context) =>
		throw new ArgumentException("Parser should never reach here.");
}
