using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NaturalSort.Extension;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Library.Services;

public class AttributeService(
	IMediator mediator,
	IPermissionService ps,
	ILocateService locateService,
	IValidateService validateService,
	INotifyService notifyService,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration,
	IServiceProvider serviceProvider)
	: IAttributeService
{
	private readonly NaturalSortComparer _attributeSort = new NaturalSortComparer(StringComparison.CurrentCulture);

	public async ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(
		AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		IAttributeService.AttributeMode mode,
		bool checkParent = true)
	{
		var attributePath = attribute.Split('`');

		if (!await validateService.Valid(IValidateService.ValidationType.AttributeName, MModule.single(attribute), obj))
		{
			return new Error<string>(ErrorMessages.Returns.ObjectAttributeString);
		}

		Func<AnySharpObject, AnySharpObject, SharpAttribute[], ValueTask<bool>> permissionPredicate = mode switch
		{
			IAttributeService.AttributeMode.Read => ps.CanViewAttribute,
			IAttributeService.AttributeMode.Execute => ps.CanExecuteAttribute,
			IAttributeService.AttributeMode.Set => ps.CanExecuteAttribute,
			IAttributeService.AttributeMode.SystemSet => (_, _, _) => ValueTask.FromResult(true),
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};
		var permissionFailureType = mode switch
		{
			IAttributeService.AttributeMode.Read => ErrorMessages.Returns.AttrPermissions,
			IAttributeService.AttributeMode.Execute => ErrorMessages.Returns.AttrEvalPermissions,
			IAttributeService.AttributeMode.Set => ErrorMessages.Returns.AttrSetPermissions,
			IAttributeService.AttributeMode.SystemSet => string.Empty,
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};

		var attributeResult = mediator.CreateStream(
			new GetAttributeWithInheritanceQuery(obj.Object().DBRef, attributePath, checkParent));

		var result = await attributeResult.FirstOrDefaultAsync();

		// PennMUSH ancestor fall-through: after the object's own @parent chain is exhausted,
		// consult the type ancestor (ANCESTOR_ROOM/PLAYER/EXIT/THING). Only when parent-checking
		// is enabled and nothing was found on the object or its parents.
		if (result == null && checkParent)
		{
			var ancestorAttributes = await GetAncestorAttributeAsync(obj, attributePath);
			if (ancestorAttributes == null)
			{
				return new None();
			}

			return await permissionPredicate(executor, obj, ancestorAttributes)
				? ancestorAttributes
				: new Error<string>(permissionFailureType);
		}

		if (result == null)
		{
			return new None();
		}

		return await permissionPredicate(executor, obj, result.Attributes)
			? result.Attributes
			: new Error<string>(permissionFailureType);
	}

	/// <summary>
	/// Resolves an attribute from the object's type ancestor (PennMUSH ANCESTOR_*), honoring the
	/// ancestor's own <c>@parent</c> chain but no further (no ancestor-of-ancestor). Returns null when:
	/// the ancestor is disabled, the object IS its own type ancestor (no self-loop), the ancestor
	/// object does not exist, or the attribute is flagged <c>no_inherit</c> on the ancestor.
	/// </summary>
	private async ValueTask<SharpAttribute[]?> GetAncestorAttributeAsync(AnySharpObject obj, string[] attributePath)
	{
		var ancestorRef = await obj.Ancestor(configuration);
		if (ancestorRef is null)
		{
			return null;
		}

		// No self-loop: an object that is its own type ancestor does not inherit from itself.
		if (ancestorRef.Value.Number == obj.Object().DBRef.Number)
		{
			return null;
		}

		var ancestorResult = await mediator
			.CreateStream(new GetAttributeWithInheritanceQuery(ancestorRef.Value, attributePath, true))
			.FirstOrDefaultAsync();

		if (ancestorResult == null)
		{
			return null;
		}

		// The attribute is being inherited by the child: a no_inherit flag on the resolved
		// (leaf) attribute blocks inheritance, matching the parent-chain semantics.
		var leaf = ancestorResult.Attributes.Last();
		if (leaf.Flags.Any(f => f.Name == "no_inherit"))
		{
			return null;
		}

		return ancestorResult.Attributes;
	}

	public async ValueTask<OptionalLazySharpAttributeOrError> LazilyGetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute,
		IAttributeService.AttributeMode mode, bool checkParent = true)
	{
		if (!await validateService.Valid(IValidateService.ValidationType.AttributeName, MModule.single(attribute), obj))
		{
			return new Error<string>(ErrorMessages.Returns.ObjectAttributeString);
		}

		var attributePath = attribute.Split('`');

		Func<AnySharpObject, AnySharpObject, LazySharpAttribute[], ValueTask<bool>> permissionPredicate = mode switch
		{
			IAttributeService.AttributeMode.Read => ps.CanViewAttribute,
			IAttributeService.AttributeMode.Execute => ps.CanExecuteAttribute,
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};
		var permissionFailureType = mode switch
		{
			IAttributeService.AttributeMode.Read => ErrorMessages.Returns.AttrPermissions,
			IAttributeService.AttributeMode.Execute => ErrorMessages.Returns.AttrEvalPermissions,
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};

		var attributeResult = mediator.CreateStream(
			new GetLazyAttributeWithInheritanceQuery(obj.Object().DBRef, attributePath, checkParent));

		var result = await attributeResult.FirstOrDefaultAsync();

		// PennMUSH ancestor fall-through (lazy): see GetAttributeAsync for full semantics.
		if (result == null && checkParent)
		{
			var ancestorAttributes = await GetLazyAncestorAttributeAsync(obj, attributePath);
			if (ancestorAttributes == null)
			{
				return new None();
			}

			return await permissionPredicate(executor, obj, ancestorAttributes)
				? ancestorAttributes
				: new Error<string>(permissionFailureType);
		}

		if (result == null)
		{
			return new None();
		}

		return await permissionPredicate(executor, obj, result.Attributes)
			? result.Attributes
			: new Error<string>(permissionFailureType);
	}

	/// <summary>
	/// Lazy variant of <see cref="GetAncestorAttributeAsync"/>.
	/// </summary>
	private async ValueTask<LazySharpAttribute[]?> GetLazyAncestorAttributeAsync(AnySharpObject obj, string[] attributePath)
	{
		var ancestorRef = await obj.Ancestor(configuration);
		if (ancestorRef is null)
		{
			return null;
		}

		if (ancestorRef.Value.Number == obj.Object().DBRef.Number)
		{
			return null;
		}

		var ancestorResult = await mediator
			.CreateStream(new GetLazyAttributeWithInheritanceQuery(ancestorRef.Value, attributePath, true))
			.FirstOrDefaultAsync();

		if (ancestorResult == null)
		{
			return null;
		}

		var leaf = ancestorResult.Attributes.Last();
		if (leaf.Flags.Any(f => f.Name == "no_inherit"))
		{
			return null;
		}

		return ancestorResult.Attributes;
	}

	public async ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj,
		string attribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false)
	{
		if (!await validateService.Valid(IValidateService.ValidationType.AttributeName, MModule.single(attribute), obj))
		{
			return MModule.single(ErrorMessages.Returns.ObjectAttributeString);
		}

		var realExecutor = executor;

		if (ignorePermissions)
		{
			var maybeOne = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
			realExecutor = maybeOne.Known;
		}

		var attr = await GetAttributeAsync(realExecutor, obj, attribute, IAttributeService.AttributeMode.Execute,
			evalParent);
		if (attr.IsError)
		{
			return MModule.single(attr.AsError.Value);
		}

		if (attr.IsNone)
		{
			return MModule.empty();
		}

		// PennMUSH: a HALTED object runs none of its softcode. process_expression returns
		// PE_NOTHING for a Halted executor (src/parse.c), so u()/ufun and any attribute evaluated
		// as that object yield the stored text unevaluated rather than its result. The HALT flag is
		// set by @halt and by @chown (to break ownership loops); until now nothing enforced it, so a
		// halted object kept running. The attribute runs as obj (Executor = obj below), so obj's
		// flag is the one that matters.
		if (await obj.HasFlag("HALT"))
		{
			return attr.AsAttribute.Last().Value;
		}

		var attributeName = attr.AsAttribute.Last().LongName!.ToUpper();

		// Use shared tracking collections from parser state.
		// These are guaranteed to be non-null because:
		// - CommandParse creates them for each command evaluation
		// - FunctionParse creates them for standalone parsing
		// - All nested calls propagate them through parser state
		var callDepth = parser.CurrentState.CallDepth!;
		var recursionDepths = parser.CurrentState.FunctionRecursionDepths!;
		var limitExceeded = parser.CurrentState.LimitExceeded!;

		callDepth.Increment();
		if (!recursionDepths.TryGetValue(attributeName, out var depth))
		{
			depth = 0;
		}
		recursionDepths[attributeName] = ++depth;

		if (depth > configuration.CurrentValue.Limit.FunctionRecursionLimit)
		{
			limitExceeded.IsExceeded = true;
			limitExceeded.ErrorMessage ??= ErrorMessages.Returns.Recursion;
			return MModule.single(ErrorMessages.Returns.Recursion);
		}

		try
		{
			var result = await parser.With(s =>
					s with
					{
						Arguments = args,
						EnvironmentRegisters = args,
						CurrentEvaluation = new DBAttribute(obj.Object().DBRef, attributeName),
						Function = attributeName,
						Executor = obj.Object().DBRef,
						Caller = s.Executor
					},
				async newParser =>
					await newParser.FunctionParse(attr.AsAttribute.Last().Value));

			return result!.Message!;
		}
		finally
		{
			callDepth.Decrement();
			if (recursionDepths.TryGetValue(attributeName, out var currentDepth) && currentDepth > 0)
			{
				recursionDepths[attributeName] = currentDepth - 1;
			}
		}
	}

	public async ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj,
		int depth = 1)
	{
		var actualObject = obj.Object();
		var attributes = actualObject.Attributes.Value;

		return depth <= 1
			? await attributes
				.Where(async (x, _) => await ps.CanViewAttribute(executor, obj, x))
				.ToArrayAsync(CancellationToken.None)
			: (await GetVisibleAttributesAsync(attributes, executor, obj, depth))
			.ToArray();
	}

	public async ValueTask<LazySharpAttributesOrError> LazilyGetVisibleAttributesAsync(AnySharpObject executor,
		AnySharpObject obj, int depth = 1)
	{
		await ValueTask.CompletedTask;
		var actualObject = obj.Object();
		var attributes = actualObject.LazyAttributes.Value;

		return depth <= 1
			? LazySharpAttributesOrError.FromAsync(attributes.Where(async (x, _) =>
				await ps.CanViewAttribute(executor, obj, x)))
			: LazySharpAttributesOrError.FromAsync(GetVisibleLazyAttributesAsync(attributes, executor, obj, depth));
	}

	public async ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString objAndAttribute,
		Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false,
		bool ignoreLambda = false)
	{
		var split = MModule.split("/", objAndAttribute);
		var obj = split.First();
		var attribute = MModule.multiple(split.Skip(1))!;
		var objPlainText = obj.ToPlainText();
		var applyPredicate = objPlainText.StartsWith("#apply", StringComparison.OrdinalIgnoreCase);
		var lambdaPredicate = objPlainText.StartsWith("#lambda", StringComparison.OrdinalIgnoreCase);

		if (!applyPredicate && !lambdaPredicate && attribute.Length == 0)
		{
			return await EvaluateAttributeFunctionAsync(parser, executor, executor,
				objPlainText, args, evalParent, ignorePermissions);
		}

		// Skip attribute name validation for lambda/apply: the "attribute" part is
		// executable code, not a database attribute name, and can contain characters
		// (e.g. '[', ']', '\') that are not valid in attribute names.
		if (!applyPredicate && !lambdaPredicate &&
				!await validateService.Valid(IValidateService.ValidationType.AttributeName, attribute, new None()))
		{
			return MModule.single(ErrorMessages.Returns.ObjectAttributeString);
		}

		var realExecutor = executor;

		if (ignorePermissions)
		{
			var maybeOne = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
			realExecutor = maybeOne.Known;
		}

		if (applyPredicate && !ignoreLambda)
		{
			var argN = 1;
			// The optional argument count is embedded in the obj portion after "#apply" (e.g. "#apply2" -> argN=2).
			// The function name is in the attribute portion (e.g. "#apply/strlen" -> funcname="strlen").
			var applyArgCountStr = objPlainText.Remove(0, 6); // part after "#apply"
			if (!string.IsNullOrWhiteSpace(applyArgCountStr) && !int.TryParse(applyArgCountStr, out argN))
			{
				return MModule.single(string.Format(ErrorMessages.Returns.BadArgumentFormat, "#APPLY"));
			}

			var slimArgs = Enumerable
				.Range(0, argN)
				.Select(i => i.ToString())
				.ToDictionary(k => k, k => args.TryGetValue(k, out var v) ? v : CallState.Empty);

			if (parser.FunctionLibrary.TryGetValue(attribute.ToPlainText().ToLower(), out var applyFunction))
			{
				var functionFlags = applyFunction.LibraryInformation.Attribute.Flags;

				if (functionFlags.HasFlag(FunctionFlags.GodOnly) && !await realExecutor.IsRoyalty())
				{
					return MModule.single(ErrorMessages.Returns.AttrEvalPermissions);
				}
				if (functionFlags.HasFlag(FunctionFlags.AdminOnly) && !await realExecutor.IsRoyalty())
				{
					return MModule.single(ErrorMessages.Returns.AttrEvalPermissions);
				}
				if (functionFlags.HasFlag(FunctionFlags.WizardOnly) && !await realExecutor.IsWizard())
				{
					return MModule.single(ErrorMessages.Returns.AttrEvalPermissions);
				}
				if (functionFlags.HasFlag(FunctionFlags.NoGuest) && await realExecutor.IsGuest())
				{
					return MModule.single(ErrorMessages.Returns.AttrEvalPermissions);
				}

				if (applyFunction.LibraryInformation.Attribute.Restrict.Length > 0)
				{
					var hasRestriction = await applyFunction.LibraryInformation.Attribute.Restrict.ToAsyncEnumerable()
						.AnyAsync(async (restriction, _) => await realExecutor.HasPower(restriction));
					if (!hasRestriction)
					{
						return MModule.single(ErrorMessages.Returns.AttrEvalPermissions);
					}
				}

				var result = await parser.With(
					s => s with
					{
						Arguments = slimArgs,
						EnvironmentRegisters = slimArgs,
						CallDepth = s.CallDepth,
						FunctionRecursionDepths = s.FunctionRecursionDepths,
						TotalInvocations = s.TotalInvocations,
						LimitExceeded = s.LimitExceeded
					},
					async np => await applyFunction.LibraryInformation.Function.Invoke(np)
				);

				return result.Message!;
			}

			// Check if proper function name in the attribute section.
			// Check if enough arguments are being passed to the function based on the number after #apply.
			// This is where we really need a proper attribute library access layer, similar to commands.

			// CallFunction must be Exposed by IMUSHCodeParser.
			// Further work is needed before this can be implemented properly.
		}

		if (lambdaPredicate && !ignoreLambda)
		{
			var result = await parser.With(s => s with
			{
				Arguments = args,
				EnvironmentRegisters = args,
				CallDepth = s.CallDepth,
				FunctionRecursionDepths = s.FunctionRecursionDepths,
				TotalInvocations = s.TotalInvocations,
				LimitExceeded = s.LimitExceeded
			},
				async np => await np.FunctionParse(attribute));
			return result!.Message!;
		}

		var maybeObject =
			await locateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objPlainText,
				LocateFlags.All);

		return maybeObject switch
		{
			{ IsError: true } => maybeObject.AsError.Message!,
			_ => await EvaluateAttributeFunctionAsync(parser, executor, maybeObject.AsSharpObject, attribute.ToPlainText(),
				args, evalParent, ignorePermissions)
		};
	}

	private async ValueTask<ImmutableList<SharpAttribute>> GetVisibleAttributesAsync(
		IAsyncEnumerable<SharpAttribute> attributes, AnySharpObject executor, AnySharpObject obj, int depth = 1)
	{
		if (depth == 0) return [];

		var visibleList = (await attributes.Where((x, _) => ps.CanViewAttribute(executor, obj, x))
				.ToListAsync())
			.ToImmutableList();

		foreach (var attribute in visibleList)
		{
			var subAttributes =
				await GetVisibleAttributesAsync(await attribute.Leaves.WithCancellation(CancellationToken.None), executor, obj,
					depth - 1);
			visibleList = visibleList.AddRange(subAttributes);
		}

		return visibleList;
	}

	private async IAsyncEnumerable<LazySharpAttribute> GetVisibleLazyAttributesAsync(
		IAsyncEnumerable<LazySharpAttribute> attributes, AnySharpObject executor, AnySharpObject obj, int currentDepth = 1)
	{
		var attrs = attributes;
		var stagingAttrs = new List<IAsyncEnumerable<LazySharpAttribute>>();

		const int maxDepth = 0;

		while (currentDepth > maxDepth)
		{
			var visibleAttributes = attrs
				.Where(async (x, _)
					=> await ps.CanViewAttribute(executor, obj, x));

			// Multiple Iteration that may be able to be optimized away.
			await foreach (var attr in visibleAttributes)
			{
				yield return attr;
				stagingAttrs.AddRange(await attr.Leaves.WithCancellation(CancellationToken.None));
			}

			attrs = visibleAttributes
				.Select<LazySharpAttribute, IAsyncEnumerable<LazySharpAttribute>>(async (x, _) =>
					await x.Leaves.WithCancellation(CancellationToken.None))
				.SelectMany(x => x);

			currentDepth++;
		}
	}

	/// <summary>
	/// Get attributes matching a pattern. Supports exact match, wildcard, and regex modes.
	/// </summary>
	/// <param name="executor">The object requesting the attributes</param>
	/// <param name="obj">The object whose attributes to retrieve</param>
	/// <param name="attributePattern">The pattern to match (exact name, wildcard pattern, or regex)</param>
	/// <param name="checkParents">Whether to check parent objects</param>
	/// <param name="mode">Pattern matching mode: Exact, Wildcard, or Regex</param>
	/// <returns>Array of matching attributes or error</returns>
	public async ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		bool checkParents,
		IAttributeService.AttributePatternMode mode)
	{
		var attributes = mediator.CreateStream(
			new GetAttributesQuery(obj.Object().DBRef, attributePattern.ToUpper(), checkParents, mode));

		var isPrivileged = executor.IsGod() || await executor.IsWizard();

		if (isPrivileged)
		{
			return await attributes
				.OrderBy(x => x.LongName, _attributeSort)
				.ToArrayAsync();
		}

		// A pattern can name a leaf without matching any of its ancestors, so the result
		// set alone never proves a branch is safe to reveal. Walk the real root..leaf path
		// for each match - PennMUSH re-checks every level, so a mortal_dark (or non-visual)
		// branch hides its leaves however narrow the pattern was.
		var results = await attributes.ToArrayAsync();
		var known = IndexByLongName(results, static x => x.LongName);

		var permitted = new List<SharpAttribute>();
		foreach (var attr in results)
		{
			var path = await AttributeAncestry.PathAsync(attr, known, parts => FetchAncestorAsync(obj, parts));
			if (await ps.CanViewAttribute(executor, obj, path))
				permitted.Add(attr);
		}

		return permitted
			.OrderBy(x => x.LongName, _attributeSort)
			.ToArray();
	}

	/// <summary>
	/// Loads a single ancestor attribute for the ancestor walk. Returns null when the
	/// ancestor does not exist - an orphaned leaf contributes no ancestor, not a denial.
	/// </summary>
	private async ValueTask<SharpAttribute?> FetchAncestorAsync(AnySharpObject obj, string[] path)
		=> await mediator
			.CreateStream(new GetAttributeQuery(obj.Object().DBRef, path))
			.LastOrDefaultAsync();

	/// <inheritdoc cref="FetchAncestorAsync"/>
	private async ValueTask<LazySharpAttribute?> FetchLazyAncestorAsync(AnySharpObject obj, string[] path)
		=> await mediator
			.CreateStream(new GetLazyAttributeQuery(obj.Object().DBRef, path))
			.LastOrDefaultAsync();

	/// <summary>
	/// Indexes already-materialised attributes by long name for the ancestor walk.
	/// Case-insensitive, as attribute names are; last write wins on a duplicate rather
	/// than throwing the way <c>ToDictionary</c> would.
	/// </summary>
	private static Dictionary<string, T> IndexByLongName<T>(IEnumerable<T> attributes, Func<T, string> longNameOf)
	{
		var index = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
		foreach (var attribute in attributes)
		{
			index[longNameOf(attribute)] = attribute;
		}

		return index;
	}

	/// <summary>
	/// Lazily get attributes matching a pattern. More efficient for large result sets.
	/// </summary>
	/// <param name="executor">The object requesting the attributes</param>
	/// <param name="obj">The object whose attributes to retrieve</param>
	/// <param name="attributePattern">The pattern to match</param>
	/// <param name="checkParents">Whether to check parent objects</param>
	/// <param name="mode">Pattern matching mode</param>
	/// <returns>Lazy enumerable of matching attributes</returns>
	public async ValueTask<LazySharpAttributesOrError> LazilyGetAttributePatternAsync(AnySharpObject executor,
		AnySharpObject obj, string attributePattern,
		bool checkParents, IAttributeService.AttributePatternMode mode = IAttributeService.AttributePatternMode.Exact)
	{
		var attributes = mediator.CreateStream(
			new GetLazyAttributesQuery(obj.Object().DBRef, attributePattern.ToUpper(), checkParents, mode));

		var isPrivileged = executor.IsGod() || await executor.IsWizard();

		if (isPrivileged)
		{
			return LazySharpAttributesOrError
				.FromAsync(attributes.OrderBy(x => x.LongName, _attributeSort));
		}

		// For non-privileged viewers, materialize so each match's ancestor path can be walked.
		return LazySharpAttributesOrError.FromAsync(FilterLazyAttributes(executor, obj, attributes));
	}

	private async IAsyncEnumerable<LazySharpAttribute> FilterLazyAttributes(
		AnySharpObject executor, AnySharpObject obj, IAsyncEnumerable<LazySharpAttribute> attributes)
	{
		// See GetAttributePatternAsync: permission follows the real root..leaf path, not
		// whatever subset of the tree the pattern happened to match.
		var results = await attributes.ToArrayAsync();
		var known = IndexByLongName(results, static x => x.LongName);

		var ordered = results.OrderBy(x => x.LongName, _attributeSort);

		foreach (var attr in ordered)
		{
			var path = await AttributeAncestry.PathAsync(attr, known, parts => FetchLazyAncestorAsync(obj, parts));
			if (await ps.CanViewAttribute(executor, obj, path))
				yield return attr;
		}
	}

	public ValueTask<OneOf<Success, Error<string>>> SetAttributeFlagAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute, string flag)
		=> SetAttributeFlagsAsync(executor, obj, attribute, [flag]);

	public ValueTask<OneOf<Success, Error<string>>> UnsetAttributeFlagAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute, string flag)
		=> SetAttributeFlagsAsync(executor, obj, attribute, [$"!{flag}"]);

	/// <summary>
	/// Applies a whole list of attribute-flag tokens (each optionally <c>!</c>-prefixed to
	/// unset) as ONE operation: one fetch, one permission check, then every mutation applied
	/// together. Mirrors PennMUSH's <c>do_attrib_flags</c>/<c>af_helper</c>
	/// (<c>src/set.c:483-533</c>), which parses the WHOLE flag argument into two bitmasks
	/// first and checks <c>Can_Write_Attr</c> exactly once against the attribute's pre-batch
	/// state, then applies both masks together - so <c>@set obj/attr=!safe wizard</c> and
	/// <c>@set obj/attr=wizard !safe</c> behave identically, and clearing <c>safe</c> doesn't
	/// block an unrelated flag change in the same command.
	/// <para>
	/// Before this (Task 6 fix round 1, M2/M3), every caller applied flags one at a time via
	/// <see cref="SetAttributeFlagAsync"/>/<see cref="UnsetAttributeFlagAsync"/> in a loop,
	/// re-checking permission after each mutation - order-dependent, and one flag's side
	/// effect (e.g. <c>safe</c> having just been set) could silently block a sibling flag in
	/// the same logical operation.
	/// </para>
	/// </summary>
	public async ValueTask<OneOf<Success, Error<string>>> SetAttributeFlagsAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute, IReadOnlyList<string> flagTokens)
	{
		if (flagTokens.Count == 0)
		{
			return new Success();
		}

		// SystemSet: fetch without a baked-in permission gate. The mode-based predicates
		// (CanViewAttribute/CanExecuteAttribute) test read/eval permission, not writer
		// permission - CanSet/CanSetIgnoringSafe below is the actual write gate for this path
		// (Task 6). checkParent: false - Penn's af_helper only ever iterates the target
		// object's own attributes (atr_iter_get), never a parent's, so this must not resolve
		// (and then gate/flag) an inherited attribute that doesn't actually live on `obj`
		// (Task 6 fix round 1, M4).
		var returnedAttribute = await GetAttributeAsync(executor, obj, attribute, IAttributeService.AttributeMode.SystemSet, false);
		if (returnedAttribute.IsError)
		{
			return returnedAttribute.AsError;
		}

		if (returnedAttribute.IsNone)
		{
			return new Error<string>(ErrorMessages.Returns.ObjectAttributeString);
		}

		var flagList = await mediator.CreateStream(new GetAttributeFlagsQuery()).ToArrayAsync();

		var resolved = new List<(SharpAttributeFlag Flag, bool Unset)>(flagTokens.Count);
		foreach (var token in flagTokens)
		{
			var unset = token.StartsWith('!');
			var name = unset ? token[1..] : token;

			var flag = flagList
				.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
					|| (x.Symbol != null && x.Symbol.Equals(name, StringComparison.OrdinalIgnoreCase)));

			// PennMUSH-compatible prefix matching: "wiz" matches "wizard"
			flag ??= flagList
				.Where(x => x.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
				.OrderBy(x => x.Name.Length)
				.FirstOrDefault();

			if (flag is null)
			{
				// Mirrors Penn: string_to_atrflagsets fails the WHOLE argument on one bad flag
				// name, before any flag in the batch is ever applied.
				return new Error<string>("Flag Found");
			}

			resolved.Add((flag, unset));
		}

		var target = returnedAttribute.AsAttribute.Last();

		// Penn's af_helper (src/set.c:509-511) requires the normal, safe-obeying Can_Write_Attr
		// UNLESS the batch clears SAFE itself, in which case it falls back to
		// Can_Write_Attr_Ignore_Safe for the WHOLE batch - the one safe=0 call site in the
		// codebase. Every other batch (including one that sets/clears other flags on an
		// attribute that happens to carry safe) still obeys it.
		var clearingSafe = resolved.Any(r => r.Unset && r.Flag.Name.Equals("SAFE", StringComparison.OrdinalIgnoreCase));
		var permitted = clearingSafe
			? await ps.CanSetIgnoringSafe(executor, obj, returnedAttribute.AsAttribute)
			: await ps.CanSet(executor, obj, returnedAttribute.AsAttribute);

		if (!permitted)
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		var currentFlags = target.Flags;

		// Clear first, then set - matching af_helper's own `AL_FLAGS(atr) &= ~clrf;` before
		// `AL_FLAGS(atr) |= setf;`, so the same flag appearing in both directions in one batch
		// ends up set, and the outcome never depends on the order flags were typed in.
		foreach (var (flag, _) in resolved.Where(r => r.Unset))
		{
			if (!currentFlags.Any(f => f.Name.Equals(flag.Name, StringComparison.OrdinalIgnoreCase)))
			{
				await notifyService.Notify(executor, $"Flag {flag.Name} is not set on attribute {target.LongName}", obj);
				continue;
			}

			await mediator.Send(new UnsetAttributeFlagCommand(obj.Object().DBRef, target, flag));
			await notifyService.Notify(executor, $"Flag {flag.Name} unset from attribute {target.LongName}", obj);
		}

		foreach (var (flag, _) in resolved.Where(r => !r.Unset))
		{
			if (currentFlags.Any(f => f.Name.Equals(flag.Name, StringComparison.OrdinalIgnoreCase)))
			{
				await notifyService.Notify(executor, $"Flag {flag.Name} is already set on attribute {target.LongName}", obj);
				continue;
			}

			await mediator.Send(new SetAttributeFlagCommand(obj.Object().DBRef, target, flag));
			await notifyService.Notify(executor, $"Flag {flag.Name} set on attribute {target.LongName}", obj);
		}

		return new Success();
	}

	public async ValueTask<OneOf<Success, Error<string>>> SetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		MString value)
	{
		if (!await ps.Controls(executor, obj))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		var attrPath = attribute.Split('`');

		// Materialized (not left as a stream) because it is used twice: once below for the
		// permission check, and again after the set for the target attribute's syntax flags. An
		// *existing* attribute's flags never change on a value set, so this pre-set snapshot is
		// exactly what the post-set flag check needs too -- reusing it avoids a second
		// GetAttributeQuery round trip on every overwrite of an already-existing attribute (the
		// overwhelmingly common case). It is NOT reusable for a brand-new attribute: GetAttributeQuery
		// is all-or-nothing, so `existing` comes back empty here, but SetAttributeCommand applies
		// SharpAttributeEntry.DefaultFlags (admin-configurable via @attribute, including cmdsyntax/
		// funsyntax) to the newly-created node during that same call -- see the post-set re-fetch below.
		var existing = await mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, attrPath))
			.ToListAsync();

		// Check both attribute permissions AND object permissions
		// Attribute permissions: executor must be able to set each attribute in the path
		// Object permissions: executor must control the object
		var permission = true;
		foreach (var x in existing)
		{
			if (!await ps.CanSet(executor, obj, x))
			{
				permission = false;
				break;
			}
		}

		if (!permission)
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		// If the target attribute doesn't exist yet (creating new), we still need to check
		// permissions on the existing ancestor path. The stream above yields nothing when
		// the full path doesn't exist (count != attribute.Length check in GetAttributeAsync).
		// Check each existing prefix of the path.
		if (attrPath.Length > 1)
		{
			for (var i = attrPath.Length - 1; i >= 1; i--)
			{
				var prefix = attrPath[..i];
				var prefixAttr = mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, prefix));
				var prefixPermission = await prefixAttr.AllAsync(async (x, _) => await ps.CanSet(executor, obj, x));
				if (!prefixPermission)
				{
					return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
				}
				// If prefix stream returned results, we found existing ancestors — done checking
				var prefixCheck = mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, prefix));
				if (await prefixCheck.AnyAsync())
				{
					break;
				}
			}
		}

		await mediator.Send(new SetAttributeCommand(obj.Object().DBRef, attrPath, value,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		// Advisory-only set-time validation: PennMUSH never validates softcode at set time, and
		// parity governs here, so a syntax error must never block the set -- only warn the setter,
		// after the value is already stored. `existing` (fetched pre-set, above) is reused when the
		// attribute already existed -- its flags cannot have changed underneath this call. But when
		// `existing` is empty, this was a first-ever write: DefaultFlags was just applied to the
		// brand-new node by the SetAttributeCommand handler, so only a fresh fetch can see it. Paying
		// one extra query here is a one-time cost per attribute, not a per-set cost.
		var storedAttribute = existing.Count != 0
			? existing.LastOrDefault()
			: await mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, attrPath)).LastOrDefaultAsync();
		var parseType = storedAttribute?.SyntaxParseType();

		if (parseType is not null)
		{
			// IMUSHCodeParser is resolved lazily via the container rather than taken as a constructor
			// parameter: MUSHCodeParser's own constructor eagerly resolves IAttributeService through
			// this same IServiceProvider, so an eager IMUSHCodeParser dependency here would be a
			// circular singleton resolution. Deferring the lookup to call time (long after both
			// singletons are fully constructed) breaks the cycle.
			var mushParser = serviceProvider.GetRequiredService<IMUSHCodeParser>();
			// Only the code half: a $-command's or listen's pattern is compiled to a match regex, never
			// parsed, so validating it would warn about an attribute that works (SoftcodeSource.Validate).
			var errors = SoftcodeSource.Validate(mushParser, value, parseType.Value);

			foreach (var error in errors)
			{
				await notifyService.Notify(executor, error.ToMushFailureString(), obj);
			}
		}

		return new Success();
	}

	/// <summary>
	/// Sets the value of an attribute to string.Empty
	/// </summary>
	/// <param name="executor"></param>
	/// <param name="obj"></param>
	/// <param name="attributePattern"></param>
	/// <param name="patternMode"></param>
	/// <param name="clearMode"></param>
	/// <returns></returns>
	public async ValueTask<OneOf<Success, Error<string>>> ClearAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		IAttributeService.AttributePatternMode patternMode,
		IAttributeService.AttributeClearMode clearMode)
	{
		await ValueTask.CompletedTask;

		if (!await ps.Controls(executor, obj))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		var attr = mediator.CreateStream(new GetAttributesQuery(obj.Object().DBRef, attributePattern, false, patternMode));

		var attrArr = await attr.ToArrayAsync();
		var isWipe = patternMode == IAttributeService.AttributePatternMode.Wildcard;

		// If no matching attributes exist, there is nothing to clear. Exact mode
		// (@set obj/attr=, every caller other than @WIPE) succeeds silently, as before -
		// PennMUSH does not error when clearing a non-existent attribute. But @wipe's own
		// do_wipe (set.c:1567-1577) ALWAYS prints its tally, even when atr_iter_get matched
		// nothing at all: a typo'd pattern still gets "No attributes wiped.", not silence.
		// Round 3 moved the tally below this early return, which made a zero-match @wipe go
		// completely silent - a real regression from round 2's (wrong, but at least present)
		// generic success line (Task 6 fix round 4).
		if (attrArr.Length == 0)
		{
			if (isWipe)
			{
				await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoAttributesWiped), executor, 0);
			}

			return new Success();
		}

		var dbref = obj.Object().DBRef;

		// Gate on each match's FULL ancestor path, not the matched attribute alone: a pattern
		// like "**" (@wipe) can match a leaf several levels under a wizard/safe/locked branch
		// without that branch node itself appearing in attrArr, so checking the leaf in
		// isolation would miss the ancestor's flag entirely (Task 6). Siblings that the
		// pattern also matched are reused as free ancestor data (Task 6 fix round 1, L1)
		// before falling back to a query.
		var matchKnown = IndexByLongName(attrArr, static x => x.LongName!);

		// PennMUSH's wipe_helper (src/set.c:1493-1523) is invoked once per matched attribute
		// via atr_iter_get and notifies each denial/tree-block AS IT'S DISCOVERED, then keeps
		// going - a denied or partially-blocked match never stops the others from being
		// processed, and neither failure class ever displaces the other's report (Task 6 fix
		// round 3). do_wipe (src/set.c:1568-1577) then ALWAYS prints a final tally - "No"/
		// "One"/"N attributes wiped." - regardless of whether anything was blocked, so the
		// player learns both what was refused and what actually happened. Exact-mode
		// (@set obj/attr=, used by callers other than @WIPE) keeps the original single
		// aggregated Success/Error contract those callers already depend on.
		var deniedNames = new List<string>();
		var wipedCount = 0;

		foreach (var attrItem in attrArr)
		{
			var path = await ResolveWriteGatePathAsync(dbref, attrItem.LongName!, matchKnown);

			// A path shorter than the split name is a broken/orphaned chain. PennMUSH's
			// can_write_attr_internal (src/attrib.c:392-393) returns 0 the instant a prefix
			// segment can't be found - denying, not silently permitting on incomplete data
			// (Task 6 fix round 1, M1: CanSet(...) with an empty array returns true, so this
			// must be checked explicitly before ever calling CanSet).
			if (path is null || !await ps.CanSet(executor, obj, path))
			{
				deniedNames.Add(attrItem.LongName!);
				if (isWipe)
				{
					// PennMUSH's AE_ERROR wording (set.c:1511-1513), one line per match -
					// never the raw "#-1 NO PERMISSION..." return code.
					await notifyService.NotifyLocalized(executor,
						nameof(ErrorMessages.Notifications.UnableToWipeAttribute), executor, attrItem.LongName!);
				}
				continue;
			}

			// For wildcard patterns (used by @wipe), delete the attribute and its
			// descendants - gated per descendant (WipeSubtreeGatedAsync). For exact patterns
			// (used by @set obj/attr=), use ClearAttributeCommand, which preserves parent
			// nodes that still have children.
			if (isWipe)
			{
				var (fullyWiped, deletedCount) = await WipeSubtreeGatedAsync(executor, obj, attrItem);
				wipedCount += deletedCount;
				if (!fullyWiped)
				{
					// PennMUSH's AE_TREE wording (set.c:1514-1518), one line per match.
					await notifyService.NotifyLocalized(executor,
						nameof(ErrorMessages.Notifications.AttributeCannotBeWipedChildBlocked), executor, attrItem.LongName!);
				}
			}
			else
			{
				await mediator.Send(new ClearAttributeCommand(dbref, attrItem.LongName!.Split('`')));
			}
		}

		if (!isWipe)
		{
			return deniedNames.Count > 0
				? new Error<string>(ErrorMessages.Returns.AttrSetPermissions)
				: new Success();
		}

		// The unconditional final tally (do_wipe, set.c:1568-1577) - every one of the three
		// states ("wiped everything", "wiped some", "wiped nothing") lands here, since
		// wipedCount already reflects exactly how many attribute nodes were actually removed
		// regardless of how many matches were denied or tree-blocked above.
		await notifyService.NotifyLocalized(executor, wipedCount switch
		{
			0 => nameof(ErrorMessages.Notifications.NoAttributesWiped),
			1 => nameof(ErrorMessages.Notifications.OneAttributeWiped),
			_ => nameof(ErrorMessages.Notifications.AttributesWipedCount)
		}, executor, wipedCount);

		return new Success();
	}

	/// <summary>
	/// Resolves the full root..leaf path for a WRITE gate. Unlike <see cref="AttributeAncestry"/>
	/// (built for the read path, where a missing ancestor is simply omitted so a caller can
	/// still evaluate what IS present - see <c>FetchAncestorAsync</c>), a missing ancestor here
	/// must deny the whole write: PennMUSH's <c>can_write_attr_internal</c>
	/// (<c>src/attrib.c:392-393</c>) returns 0 the instant a prefix segment isn't found, rather
	/// than treating a broken/orphaned chain as if the missing levels simply carried no flags.
	/// Ancestors present in <paramref name="known"/> are used without a query - the caller
	/// supplies whatever it already has in memory (sibling pattern matches, or an already-
	/// fetched subtree), so a query only ever happens for a genuinely absent prefix
	/// (Task 6 fix round 1, L1).
	/// </summary>
	/// <returns>The full path, or null if any prefix segment could not be resolved at all.</returns>
	private async ValueTask<SharpAttribute[]?> ResolveWriteGatePathAsync(
		DBRef dbref, string longName, IReadOnlyDictionary<string, SharpAttribute> known)
	{
		var segments = longName.Split('`');
		var result = new SharpAttribute[segments.Length];

		for (var i = 0; i < segments.Length; i++)
		{
			var prefixName = string.Join('`', segments[..(i + 1)]);

			if (known.TryGetValue(prefixName, out var attribute))
			{
				result[i] = attribute;
				continue;
			}

			var fetched = await mediator.CreateStream(new GetAttributeQuery(dbref, segments[..(i + 1)]))
				.LastOrDefaultAsync();

			if (fetched is null)
			{
				return null;
			}

			result[i] = fetched;
		}

		return result;
	}

	/// <summary>
	/// Deletes <paramref name="root"/> and its full descendant subtree if every one of them is
	/// individually writable. <paramref name="root"/>'s own permission has already passed the
	/// caller's gate.
	/// <para>
	/// PennMUSH's <c>real_atr_clr</c>/<c>atr_clear_children</c> (<c>attrib.c:1027-1145</c>)
	/// computes this bottom-up per node: a node's whole subtree is only removed if the node
	/// itself is writable AND every one of its children's subtrees also qualifies. A node that
	/// fails that test - because it isn't itself writable, or because ANY descendant beneath it
	/// isn't - is left <b>completely untouched: value included, not merely "kept but cleared."</b>
	/// There is no "clear the value but keep the node" middle ground in Penn for a branch that
	/// can't be fully cleared (<c>real_atr_clr</c> either <c>atr_free_one</c>s a node outright or
	/// does nothing to it at all) - a protected node's SIBLINGS are unaffected, though: each one
	/// is still deleted or preserved purely by its own subtree's outcome (Task 6 fix round 2:
	/// round 1's fallback wrongly called <c>ClearAttributeCommand</c>, which blanks the VALUE of
	/// any node that still has a remaining child, on every ancestor above a protected
	/// descendant - silently destroying data on an operation that was supposed to have been
	/// denied for that branch).
	/// </para>
	/// </summary>
	/// <returns>
	/// <c>FullyCleared</c>: <c>true</c> if the whole subtree (root included) was fully
	/// deleted; <c>false</c> if any part of it had to be left untouched because of a
	/// protected descendant. <c>DeletedCount</c>: how many attribute nodes were actually
	/// removed (root plus however many descendants qualified) - PennMUSH's <c>do_wipe</c>
	/// (<c>set.c:1568-1577</c>) always reports this count regardless of <c>FullyCleared</c>,
	/// so the caller needs it even on a partial result.
	/// </returns>
	private async ValueTask<(bool FullyCleared, int DeletedCount)> WipeSubtreeGatedAsync(
		AnySharpObject executor, AnySharpObject obj, SharpAttribute root)
	{
		var dbref = obj.Object().DBRef;
		var rootName = root.LongName!;

		// "root`**" matches every descendant at any depth (double-star crosses backticks) and
		// nothing else - root itself is never matched by this pattern.
		var rawDescendants = await mediator
			.CreateStream(new GetAttributesQuery(dbref, $"{rootName}`**".ToUpper(), false,
				IAttributeService.AttributePatternMode.Wildcard))
			.ToArrayAsync();

		// "?" is a legal attribute-name character (ValidateService.cs), and the wildcard
		// translation maps a literal "?" in the pattern to a single-char regex wildcard - so
		// an attribute literally named e.g. "WHAT?" turns "WHAT?`**" into a pattern that can
		// also match unrelated siblings sharing the "WHAT" prefix. Every extra match would
		// already be sitting in attrArr under its own gate, so this was never an actual
		// over-delete - but a delete path has no business trusting an unescaped
		// string-interpolated wildcard. Guard in memory instead.
		var descendants = rawDescendants
			.Where(d => d.LongName!.StartsWith(rootName + "`", StringComparison.OrdinalIgnoreCase))
			.ToArray();

		if (descendants.Length == 0)
		{
			await mediator.Send(new WipeAttributeCommand(dbref, rootName.Split('`')));
			return (true, 1);
		}

		// Every ancestor of every descendant here is either root or another member of this
		// same subtree listing (the "**" match reaches all of them at once), so this dict
		// makes ResolveWriteGatePathAsync's per-descendant walk free - no query should ever
		// be needed below (Task 6 fix round 1, L1).
		var known = IndexByLongName(descendants, static x => x.LongName!);
		known[rootName] = root;

		var permitted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { [rootName] = true };
		var deniedAny = false;

		foreach (var descendant in descendants)
		{
			var path = await ResolveWriteGatePathAsync(dbref, descendant.LongName!, known);
			var ok = path is not null && await ps.CanSet(executor, obj, path);
			permitted[descendant.LongName!] = ok;
			deniedAny |= !ok;
		}

		if (!deniedAny)
		{
			// Common case: nothing in the subtree is protected - one recursive delete, same
			// as before this fix.
			await mediator.Send(new WipeAttributeCommand(dbref, rootName.Split('`')));
			return (true, 1 + descendants.Length);
		}

		// Bottom-up: a node's subtree is fully clearable only if the node itself is permitted
		// AND every one of its direct children's subtrees is also fully clearable - exactly
		// PennMUSH's recursive atr_clear_children definition. Processing deepest-first means a
		// node's children are already resolved (and, if clearable, already deleted) by the time
		// the node itself is evaluated.
		var deepestFirst = descendants
			.OrderByDescending(d => d.LongName!.Count(c => c == '`'))
			.ToArray();

		var fullyClearable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		// IsDirectChildOf trusts that a node's immediate parent is either `root` or another
		// entry in `descendants` whenever that node itself is one. That invariant comes from
		// HOW descendants is populated, not from string-parsing LongName: the underlying
		// provider query (e.g. ArangoDB's `FOR v IN 1..99999 OUTBOUND start GRAPH
		// GraphAttributes FILTER v.LongName =~ @pattern`) is a graph traversal along real
		// parent->child edges, so a node can only appear in the results if every ancestor
		// between the object root and that node has its own vertex and edge - an "FOO`BAR`BAZ
		// exists but FOO`BAR doesn't" gap is not representable by the traversal that produced
		// this list in the first place (all three providers share this edge-per-level model).
		// If that ever stopped holding, a node whose immediate parent is missing from this set
		// would be "nobody's child" to IsDirectChildOf and so could never block an ancestor's
		// fullyClearable computation - the mitigation, if it were ever needed, would be to
		// treat any node whose direct parent isn't in `known` as denied up front, the same way
		// ResolveWriteGatePathAsync already fails closed on a missing prefix (M1).
		bool IsFullyClearable(string name)
		{
			var childrenClearable = descendants
				.Where(d => IsDirectChildOf(d.LongName!, name))
				.All(d => fullyClearable[d.LongName!]);
			return permitted[name] && childrenClearable;
		}

		foreach (var descendant in deepestFirst)
		{
			fullyClearable[descendant.LongName!] = IsFullyClearable(descendant.LongName!);
		}

		var rootFullyClearable = IsFullyClearable(rootName);

		// Delete every node whose own subtree is fully clearable, deepest-first, so that by
		// the time a node is reached, any of its children that qualified have already been
		// removed - ClearAttributeCommand's own "no remaining children -> fully remove" path
		// then applies cleanly. A node that ISN'T fully clearable is never touched at all -
		// no ClearAttributeCommand call, no value change, matching real_atr_clr leaving a
		// blocked branch completely alone.
		var deletedCount = 0;
		foreach (var descendant in deepestFirst)
		{
			if (fullyClearable[descendant.LongName!])
			{
				await mediator.Send(new ClearAttributeCommand(dbref, descendant.LongName!.Split('`')));
				deletedCount++;
			}
		}

		if (rootFullyClearable)
		{
			await mediator.Send(new ClearAttributeCommand(dbref, rootName.Split('`')));
			deletedCount++;
		}

		return (rootFullyClearable, deletedCount);
	}

	/// <summary>
	/// True if <paramref name="childLongName"/> names a direct (one level deeper) child of
	/// <paramref name="parentLongName"/> in the attribute tree - not a grandchild or deeper.
	/// See the invariant this relies on, documented at its call site in
	/// <see cref="WipeSubtreeGatedAsync"/>.
	/// </summary>
	private static bool IsDirectChildOf(string childLongName, string parentLongName)
	{
		var prefix = parentLongName + "`";
		return childLongName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			&& childLongName.LastIndexOf('`') == parentLongName.Length;
	}
}