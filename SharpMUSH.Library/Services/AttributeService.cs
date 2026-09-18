using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NaturalSort.Extension;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Library.Services;

public class AttributeService(
	IMediator mediator,
	IPermissionService ps,
	ILocateService locateService,
	IValidateService validateService,
	INotifyService notifyService,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration,
	IServiceProvider serviceProvider,
	Microsoft.Extensions.Logging.ILogger<AttributeService> logger,
	IUserDefinedFunctionService? userFunctions = null)
	: IAttributeService, IAttributeFunctionResultService
{
	private readonly NaturalSortComparer _attributeSort = new NaturalSortComparer(StringComparison.CurrentCulture);

	/// <summary>Flag semantics — PennMUSH's <c>af_helper</c>. See <see cref="AttributeFlagWriter"/>.</summary>
	private readonly AttributeFlagWriter _flagWriter = new(mediator, ps, notifyService);

	/// <summary>Writes and <c>@wipe</c> — PennMUSH's <c>do_set_atr</c>/<c>do_wipe</c>. See <see cref="AttributeWriter"/>.</summary>
	private readonly AttributeWriter _writer = new(mediator, ps, notifyService, serviceProvider);

	/// <inheritdoc/>
	public ValueTask<Result<Success>> SetAttributeAsync(AnySharpObject executor, AnySharpObject obj,
		string attribute, MString value)
		=> _writer.SetAttributeAsync(executor, obj, attribute, value);

	/// <inheritdoc/>
	public ValueTask<Result<Success>> SetAttributeAsync(AnySharpObject executor, AnySharpObject obj,
		string attribute, MString value, SharpPlayer creator)
		=> _writer.SetAttributeAsync(executor, obj, attribute, value, creator);

	/// <inheritdoc/>
	public ValueTask<Result<Success>> ClearAttributeAsync(AnySharpObject executor, AnySharpObject obj,
		string attributePattern, IAttributeService.AttributePatternMode patternMode)
		=> _writer.ClearAttributeAsync(executor, obj, attributePattern, patternMode);

	private AttributeEvaluator? _evaluator;

	/// <summary>
	/// Softcode evaluation — PennMUSH's <c>call_ufun</c> and the <c>#apply</c>/<c>#lambda</c>
	/// pseudo-objects. See <see cref="AttributeEvaluator"/>. Built on first use rather than in a
	/// field initializer because it is handed this service as its Execute-mode read, and C# forbids
	/// <c>this</c> there; construction is idempotent, so a race simply builds it twice.
	/// </summary>
	private AttributeEvaluator Evaluator => _evaluator ??= new AttributeEvaluator(this, mediator, locateService,
		validateService, notifyService, configuration, logger, userFunctions);

	/// <inheritdoc/>
	public ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj, string attribute, Dictionary<string, CallState> args,
		bool evalParent = true, bool ignorePermissions = false)
		=> Evaluator.EvaluateAttributeFunctionAsync(parser, executor, obj, attribute, args, evalParent, ignorePermissions);

	/// <inheritdoc/>
	public ValueTask<CallState> EvaluateAttributeFunctionResultAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj, string attribute, Dictionary<string, CallState> args,
		bool evalParent = true, bool ignorePermissions = false)
		=> Evaluator.EvaluateAttributeFunctionResultAsync(parser, executor, obj, attribute, args, evalParent, ignorePermissions);

	/// <inheritdoc/>
	public ValueTask<CallState> CallAttributeFunctionAsync(IMUSHCodeParser parser, AttributeFunction function)
		=> Evaluator.CallAttributeFunctionAsync(parser, function);

	/// <inheritdoc/>
	public ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString objAndAttribute, Dictionary<string, CallState> args, bool evalParent = true,
		bool ignorePermissions = false, bool ignoreLambda = false)
		=> Evaluator.EvaluateAttributeFunctionAsync(parser, executor, objAndAttribute, args, evalParent,
			ignorePermissions, ignoreLambda);

	/// <inheritdoc/>
	public ValueTask<AttributeFunctionFetch> FetchAttributeFunctionAsync(IMUSHCodeParser parser,
		AnySharpObject executor, string objectAndAttribute)
		=> Evaluator.FetchAttributeFunctionAsync(parser, executor, objectAndAttribute);

	/// <inheritdoc/>
	public ValueTask<CallState> EvaluateAttributeFunctionResultAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString objAndAttribute, Dictionary<string, CallState> args, bool evalParent = true,
		bool ignorePermissions = false, bool ignoreLambda = false)
		=> Evaluator.EvaluateAttributeFunctionResultAsync(parser, executor, objAndAttribute, args, evalParent,
			ignorePermissions, ignoreLambda);

	public async ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(
		AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		IAttributeService.AttributeMode mode,
		bool parent = true)
	{
		var cancellationToken = ExecutionBudget.CurrentToken;
		cancellationToken.ThrowIfCancellationRequested();
		var attributePath = attribute.Split('`');

		if (!await CheckReadAsync(() => validateService.Valid(IValidateService.ValidationType.AttributeName, MarkupText.Plain(attribute), obj)))
		{
			return new Error<string>(ErrorMessages.Returns.ObjectAttributeString);
		}

		Func<AnySharpObject, AnySharpObject, SharpAttribute[], ValueTask<bool>> permissionPredicate = mode switch
		{
			IAttributeService.AttributeMode.Read => (who, target, path) => CheckReadAsync(() => ps.CanViewAttribute(who, target, path)),
			IAttributeService.AttributeMode.Execute => (who, target, path) => CheckReadAsync(() => ps.CanExecuteAttribute(who, target, path)),
			IAttributeService.AttributeMode.Set => (who, target, path) => CheckReadAsync(() => ps.CanExecuteAttribute(who, target, path)),
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
			new GetAttributeWithInheritanceQuery(obj.Object().DBRef, attributePath, parent), cancellationToken);

		var result = await attributeResult.FirstOrDefaultAsync(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		// PennMUSH ancestor fall-through: after the object's own @parent chain is exhausted,
		// consult the type ancestor (ANCESTOR_ROOM/PLAYER/EXIT/THING). Only when parent-checking
		// is enabled and nothing was found on the object or its parents.
		if (result == null && parent)
		{
			var ancestor = await GetAncestorAttributeAsync(obj, attributePath);
			if (ancestor == null)
			{
				return new None();
			}

			// Penn draws no line between an @parent-sourced and an ancestor-sourced leaf: `target =
			// obj` is the first target either way, and the ancestor is only ever reached through
			// continue_target (attrib.c:344-353). So a failing prefix on obj itself denies here
			// exactly as it does above - flag-testing only the ancestor's own nodes was the same
			// fail-open, one path over.
			if (ReadWalkApplies(mode, attributePath, ancestor.Source))
			{
				return await AttributeAncestry.CanReadAsync(ancestor.Attributes[^1], ancestor.SourceObject,
						await AncestorTargetChainAsync(obj, ancestor.AncestorRef), obj.Object().DBRef,
						(target, parts) =>
							FetchReadWalkAncestorAsync(target, parts, ancestor.SourceObject, ancestor.Attributes),
						path => CheckReadAsync(() => ps.CanViewAttribute(executor, obj, path)))
					? ancestor.Attributes
					: new Error<string>(permissionFailureType);
			}

			return await permissionPredicate(executor, obj, ancestor.Attributes)
				? ancestor.Attributes
				: new Error<string>(permissionFailureType);
		}

		if (result == null)
		{
			return new None();
		}

		if (ReadWalkApplies(mode, attributePath, result.Source))
		{
			var origin = obj.Object().DBRef;
			var source = result.SourceObject;
			var resolved = result.Attributes;

			return await AttributeAncestry.CanReadAsync(resolved[^1], source,
					source.SameObjectAs(origin) ? [origin] : await ParentChainAsync(obj), origin,
					(target, parts) => FetchReadWalkAncestorAsync(target, parts, source, resolved),
					path => CheckReadAsync(() => ps.CanViewAttribute(executor, obj, path)))
				? resolved
				: new Error<string>(permissionFailureType);
		}

		return await permissionPredicate(executor, obj, result.Attributes)
			? result.Attributes
			: new Error<string>(permissionFailureType);
	}

	internal static ValueTask<bool> CheckReadAsync(Func<ValueTask<bool>> read)
		=> CheckReadAsync(read, ExecutionBudget.CurrentToken);

	internal static async ValueTask<bool> CheckReadAsync(Func<ValueTask<bool>> read, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		// Permission/validation APIs include legacy lazy reads without a token parameter.
		// Bound only this read-only decision; attribute mutations are never detached.
		var pending = read();
		var result = pending.IsCompletedSuccessfully ? pending.Result : await pending.AsTask().WaitAsync(token);
		token.ThrowIfCancellationRequested();
		return result;
	}

	/// <summary>
	/// Whether a single-attribute lookup must re-walk its ancestor path over the target chain
	/// (PennMUSH's <c>can_read_attr_internal</c>, <c>src/attrib.c:318-356</c>) rather than simply
	/// flag-testing the path as it resolved on the source object.
	/// <para>
	/// Penn re-walks on EVERY read, from <c>obj</c> outward along the <c>@parent</c> chain - not
	/// over the single object the leaf happened to resolve on. Testing only the source-resolved
	/// path fails OPEN two ways: a restrictively-flagged branch of the same name on a NEARER object
	/// is never tested at all (Penn's <c>return 0</c> is inline at <c>attrib.c:331</c> - it does not
	/// <c>continue_target</c>), and a nearer target that holds a failing prefix but not the leaf is
	/// skipped as merely "incomplete" instead of denying.
	/// </para>
	/// </summary>
	/// <remarks>
	/// Three guards, each of which breaks working reads if dropped:
	/// <list type="bullet">
	/// <item>
	/// <b>Flat names short-circuit.</b> An attribute with no <c>`</c> IS its whole path, and Penn
	/// returns 1 before the walk even starts (<c>attrib.c:311-312</c>). Walking would cost a
	/// <c>ParentChainAsync</c> on the hottest read path in the server for no decision at all.
	/// </item>
	/// <item>
	/// <b>Zone sources skip the walk.</b> The chains this is called with -
	/// <see cref="ParentChainAsync"/>, and <see cref="AncestorTargetChainAsync"/> for the ancestor
	/// fall-through - follow <c>@parent</c> only, so a zone-sourced result's source object is NOT in
	/// the chain: the walk would run off the end and DENY (<c>attrib.c:356</c>), a fail-CLOSED
	/// regression on zone reads that work today. The provider really does emit
	/// <see cref="AttributeSource.Zone"/>, so this arm is live.
	/// <para>
	/// <see cref="AttributeSource.Ancestor"/> is excluded alongside it purely defensively: no
	/// provider ever emits it. The type-ancestor fall-through is a SEPARATE lookup rooted at the
	/// ancestor (<see cref="GetAncestorAttributeAsync"/>), so its results come back tagged
	/// <c>Self</c> or <c>Parent</c> relative to that root, and the caller pairs them with
	/// <see cref="AncestorTargetChainAsync"/> - which continues through the ancestor's own parents,
	/// as Penn's <c>target = Parent(target)</c> does after <c>target = ancestor</c>
	/// (<c>attrib.c:344-353</c>). The early return at that call site, not this clause, is what makes
	/// the ancestor path work.
	/// </para>
	/// </item>
	/// <item>
	/// <b>Read only.</b> <c>Set</c>/<c>SystemSet</c> are <c>can_write_attr_internal</c>'s business
	/// (a different function with different rules - see <see cref="SetAttributeFlagsAsync"/>).
	/// Gating <c>Execute</c> on <c>Can_Read_Attr</c> is what Penn's <c>fun_ufun</c> actually does,
	/// but that is a behaviour change beyond this fix and is deliberately left alone.
	/// </item>
	/// </list>
	/// </remarks>
	private static bool ReadWalkApplies(IAttributeService.AttributeMode mode, string[] attributePath,
		AttributeSource source)
		=> mode == IAttributeService.AttributeMode.Read
			&& attributePath.Length > 1
			&& source is AttributeSource.Self or AttributeSource.Parent;

	/// <summary>
	/// A type-ancestor fall-through hit, carrying everything the read walk needs that a bare
	/// <c>T[]</c> threw away: the object the leaf was actually resolved on
	/// (<paramref name="SourceObject"/>, which may be the ancestor OR one of the ancestor's own
	/// parents), how it got there (<paramref name="Source"/>), and the type ancestor the lookup was
	/// rooted at (<paramref name="AncestorRef"/>, where the walk's target chain has to resume).
	/// Same shape as <see cref="AttributeWithSource"/>, which PR #808 added for the pattern paths.
	/// </summary>
	private sealed record AncestorHit<T>(T[] Attributes, DBRef SourceObject, AttributeSource Source, DBRef AncestorRef);

	/// <summary>
	/// Resolves an attribute from the object's type ancestor (PennMUSH ANCESTOR_*), honoring the
	/// ancestor's own <c>@parent</c> chain but no further (no ancestor-of-ancestor). Returns null when:
	/// the ancestor is disabled, the object IS its own type ancestor (no self-loop), the ancestor
	/// object does not exist, or the attribute is flagged <c>no_inherit</c> on the ancestor.
	/// </summary>
	private async ValueTask<AncestorHit<SharpAttribute>?> GetAncestorAttributeAsync(AnySharpObject obj,
		string[] attributePath)
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
			.CreateStream(new GetAttributeWithInheritanceQuery(ancestorRef.Value, attributePath, true), ExecutionBudget.CurrentToken)
			.FirstOrDefaultAsync(ExecutionBudget.CurrentToken);
		ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();

		if (ancestorResult == null)
		{
			return null;
		}

		// The attribute is being inherited by the child, so no_inherit on ANY level of the branch
		// blocks the whole path - not just on the resolved leaf. Penn's atr_get_with_parent
		// (attrib.c:1232-1252) tests AF_PRIVATE on every backtick-delimited segment while
		// crossing an inheritance boundary, and the ancestor is such a boundary exactly like an
		// @parent is. Task 7 fixed this shape for @parent chains in the provider; this
		// fall-through kept the old leaf-only test.
		if (ancestorResult.Attributes.Any(a => a.IsNoInherit()))
		{
			return null;
		}

		return new AncestorHit<SharpAttribute>(ancestorResult.Attributes, ancestorResult.SourceObject,
			ancestorResult.Source, ancestorRef.Value);
	}

	/// <summary>
	/// The read walk's target chain for a type-ancestor fall-through: <paramref name="obj"/>'s own
	/// <c>@parent</c> chain, then the ancestor's. Penn does not stop at the ancestor - it sets
	/// <c>target = ancestor</c> and keeps running <c>target = Parent(target)</c>
	/// (<c>attrib.c:344-353</c>) - so a leaf resolved on an ancestor-of-ancestor is legitimately
	/// readable, and a chain ending at the bare <c>ancestorRef</c> would leave that source off the
	/// end and deny it (<c>attrib.c:356</c>).
	/// </summary>
	/// <remarks>
	/// Targets already visited via <paramref name="obj"/>'s own chain are not walked a second time,
	/// which subsumes Penn's <c>if (target == ancestor) ancestor = NOTHING</c> (<c>attrib.c:322</c>)
	/// and keeps a shared object from being flag-tested twice.
	/// </remarks>
	private async ValueTask<List<DBRef>> AncestorTargetChainAsync(AnySharpObject obj, DBRef ancestorRef)
	{
		var chain = await ParentChainAsync(obj);

		if (await mediator.Send(new GetObjectNodeQuery(ancestorRef), ExecutionBudget.CurrentToken) is not AnySharpObject ancestor)
		{
			return chain;
		}

		foreach (var target in await ParentChainAsync(ancestor))
		{
			if (!chain.Any(seen => seen.SameObjectAs(target)))
			{
				chain.Add(target);
			}
		}

		return chain;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The eager twin of this method, line for line, apart from the element type and the two write
	/// modes it does not serve. The duplication is deliberate rather than collapsed behind a
	/// type-shape abstraction: this is the server's hottest read path, the two bodies are the
	/// PennMUSH <c>can_read_attr_internal</c> walk with its citations attached, and a generic
	/// rewrite would trade a readable ninety lines for indirection over the query type, the
	/// permission overload, the ancestor fetch and the return union — while hiding exactly the
	/// asymmetry documented on the interface.
	/// </remarks>
	public async ValueTask<OptionalLazySharpAttributeOrError> LazilyGetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute,
		IAttributeService.AttributeMode mode, bool parent = true)
	{
		if (!await CheckReadAsync(() => validateService.Valid(IValidateService.ValidationType.AttributeName, MarkupText.Plain(attribute), obj)))
		{
			return new Error<string>(ErrorMessages.Returns.ObjectAttributeString);
		}

		var attributePath = attribute.Split('`');

		Func<AnySharpObject, AnySharpObject, LazySharpAttribute[], ValueTask<bool>> permissionPredicate = mode switch
		{
			IAttributeService.AttributeMode.Read => (who, target, path) => CheckReadAsync(() => ps.CanViewAttribute(who, target, path)),
			IAttributeService.AttributeMode.Execute => (who, target, path) => CheckReadAsync(() => ps.CanExecuteAttribute(who, target, path)),
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};
		var permissionFailureType = mode switch
		{
			IAttributeService.AttributeMode.Read => ErrorMessages.Returns.AttrPermissions,
			IAttributeService.AttributeMode.Execute => ErrorMessages.Returns.AttrEvalPermissions,
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};

		var attributeResult = mediator.CreateStream(
			new GetLazyAttributeWithInheritanceQuery(obj.Object().DBRef, attributePath, parent), ExecutionBudget.CurrentToken);

		var result = await attributeResult.FirstOrDefaultAsync(ExecutionBudget.CurrentToken);
		ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();

		// PennMUSH ancestor fall-through (lazy): see GetAttributeAsync for full semantics.
		if (result == null && parent)
		{
			var ancestor = await GetLazyAncestorAttributeAsync(obj, attributePath);
			if (ancestor == null)
			{
				return new None();
			}

			if (ReadWalkApplies(mode, attributePath, ancestor.Source))
			{
				return await AttributeAncestry.CanReadAsync(ancestor.Attributes[^1], ancestor.SourceObject,
						await AncestorTargetChainAsync(obj, ancestor.AncestorRef), obj.Object().DBRef,
						(target, parts) =>
							FetchLazyReadWalkAncestorAsync(target, parts, ancestor.SourceObject, ancestor.Attributes),
						path => CheckReadAsync(() => ps.CanViewAttribute(executor, obj, path)))
					? ancestor.Attributes
					: new Error<string>(permissionFailureType);
			}

			return await permissionPredicate(executor, obj, ancestor.Attributes)
				? ancestor.Attributes
				: new Error<string>(permissionFailureType);
		}

		if (result == null)
		{
			return new None();
		}

		// The lazy path is the same read as GetAttributeAsync's and must gate identically -
		// see ReadWalkApplies.
		if (ReadWalkApplies(mode, attributePath, result.Source))
		{
			var origin = obj.Object().DBRef;
			var source = result.SourceObject;
			var resolved = result.Attributes;

			return await AttributeAncestry.CanReadAsync(resolved[^1], source,
					source.SameObjectAs(origin) ? [origin] : await ParentChainAsync(obj), origin,
					(target, parts) => FetchLazyReadWalkAncestorAsync(target, parts, source, resolved),
					path => CheckReadAsync(() => ps.CanViewAttribute(executor, obj, path)))
				? resolved
				: new Error<string>(permissionFailureType);
		}

		return await permissionPredicate(executor, obj, result.Attributes)
			? result.Attributes
			: new Error<string>(permissionFailureType);
	}

	/// <summary>
	/// Lazy variant of <see cref="GetAncestorAttributeAsync"/>.
	/// </summary>
	private async ValueTask<AncestorHit<LazySharpAttribute>?> GetLazyAncestorAttributeAsync(AnySharpObject obj,
		string[] attributePath)
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
			.CreateStream(new GetLazyAttributeWithInheritanceQuery(ancestorRef.Value, attributePath, true), ExecutionBudget.CurrentToken)
			.FirstOrDefaultAsync(ExecutionBudget.CurrentToken);
		ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();

		if (ancestorResult == null)
		{
			return null;
		}

		// See GetAncestorAttributeAsync: no_inherit anywhere on the branch blocks the whole path.
		if (ancestorResult.Attributes.Any(a => a.IsNoInherit()))
		{
			return null;
		}

		return new AncestorHit<LazySharpAttribute>(ancestorResult.Attributes, ancestorResult.SourceObject,
			ancestorResult.Source, ancestorRef.Value);
	}


	public async ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj,
		int depth = 1)
	{
		var token = ExecutionBudget.CurrentToken;
		token.ThrowIfCancellationRequested();
		return await GetVisibleAttributesAsync(obj.Object().Attributes.Value, executor, obj, Math.Max(1, depth), token).ToArrayAsync(token);
	}

	public ValueTask<LazySharpAttributesOrError> LazilyGetVisibleAttributesAsync(AnySharpObject executor,
		AnySharpObject obj, int depth = 1)
	{
		var token = ExecutionBudget.CurrentToken;
		token.ThrowIfCancellationRequested();
		return ValueTask.FromResult(LazySharpAttributesOrError.FromAsync(ReadWithDeferredBudget(
			budget => GetVisibleLazyAttributesAsync(obj.Object().LazyAttributes.Value, executor, obj, Math.Max(1, depth), budget),
			ExecutionBudget.Current, token)));
	}


	/// <summary>
	/// Every visible attribute at this level, then each one's visible subtree in turn,
	/// down to the requested depth. Each level is materialized only once.
	/// </summary>
	private async IAsyncEnumerable<SharpAttribute> GetVisibleAttributesAsync(
		IAsyncEnumerable<SharpAttribute> attributes, AnySharpObject executor, AnySharpObject obj, int depth,
		[EnumeratorCancellation] CancellationToken token = default)
	{
		token.ThrowIfCancellationRequested();
		if (depth <= 0) yield break;
		var visible = await attributes
			.Where((x, _) => CheckReadAsync(() => ps.CanViewAttribute(executor, obj, x), token))
			.ToListAsync(token);
		foreach (var attribute in visible)
		{
			token.ThrowIfCancellationRequested();
			yield return attribute;
		}
		if (depth == 1) yield break;
		foreach (var attribute in visible)
		{
			var leaves = await attribute.Leaves.WithCancellation(token);
			await foreach (var descendant in GetVisibleAttributesAsync(leaves, executor, obj, depth - 1, token).WithCancellation(token))
				yield return descendant;
		}
	}

	/// <summary>
	/// Breadth-first: each level is yielded as it is filtered, and its leaves are gathered on the way
	/// so the next level never re-runs the permission test on its parents.
	/// </summary>
	private async IAsyncEnumerable<LazySharpAttribute> GetVisibleLazyAttributesAsync(
		IAsyncEnumerable<LazySharpAttribute> attributes, AnySharpObject executor, AnySharpObject obj, int depth, ExecutionBudget budget,
		[EnumeratorCancellation] CancellationToken token = default)
	{
		for (var remaining = depth; remaining > 0; remaining--)
		{
			var nextLevel = new List<IAsyncEnumerable<LazySharpAttribute>>();
			await foreach (var attribute in attributes.WithCancellation(token))
			{
				bool visible;
				using (budget.Enter())
					visible = await CheckReadAsync(() => ps.CanViewAttribute(executor, obj, attribute), token);
				if (!visible) continue;
				yield return attribute;
				if (remaining > 1)
				{
					using (budget.Enter())
						nextLevel.Add(await attribute.Leaves.WithCancellation(token));
				}
			}
			if (nextLevel.Count == 0) yield break;
			attributes = nextLevel.ToAsyncEnumerable().SelectMany(x => x);
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
			new GetAttributesQuery(obj.Object().DBRef, attributePattern.ToUpper(), checkParents, mode), ExecutionBudget.CurrentToken);

		var results = await attributes.ToArrayAsync(ExecutionBudget.CurrentToken);

		if (executor.IsGod() || await executor.IsWizard(ExecutionBudget.CurrentToken))
		{
			// PennMUSH's Can_Read_Attr macro (hdrs/mushdb.h:100-101) is
			// `!AF_Internal(a) && (See_All(p) || can_read_attr_internal(...))`: See_All skips the
			// whole ancestor walk (and with it mortal_dark, visual and nearby), but it does NOT
			// skip the leaf's own internal flag, which is tested first and denies everyone
			// including God. Without this the privileged early-out listed internal attributes.
			return results
				.Where(x => !x.Attribute.IsInternal())
				.Select(x => x.Attribute)
				.OrderBy(x => x.LongName, _attributeSort)
				.ToArray();
		}

		// A pattern can name a leaf without matching any of its ancestors, so the result
		// set alone never proves a branch is safe to reveal. Walk the real root..leaf path
		// for each match - PennMUSH re-checks every level, so a mortal_dark (or non-visual)
		// branch hides its leaves however narrow the pattern was.
		//
		// Penn re-walks the ancestor path over TARGETS, from `obj` outward along the @parent
		// chain, not over a single object (AttributeAncestry.CanReadAsync). Both ends matter: the
		// branch nodes of an INHERITED tree attribute live on the parent the leaf came from, and a
		// restrictively-flagged branch of the same name on a NEARER object denies before the walk
		// ever reaches that parent. Attributes matched on a given object are reused as free
		// ancestor data for that object only - one object's FOO must never vouch for another
		// object's FOO`BAR.
		var knownBySource = results
			.GroupBy(x => x.SourceObject)
			.ToDictionary(g => g.Key, g => IndexByLongName(g.Select(x => x.Attribute), static x => x.LongName));

		var origin = obj.Object().DBRef;
		List<DBRef>? parentChain = null;

		var permitted = new List<SharpAttribute>();
		foreach (var (attr, source) in results)
		{
			ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();
			// A self-sourced match returns at the very first target, so it never needs the chain -
			// which keeps every checkParents:false caller, and the common case of lattrp on an
			// object that inherits nothing, at zero extra queries. The chain is built at most once
			// per call, on the first genuinely inherited match.
			var chain = source.SameObjectAs(origin)
				? [origin]
				: parentChain ??= await ParentChainAsync(obj);

			if (await AttributeAncestry.CanReadAsync(attr, source, chain, origin,
					(target, parts) => FetchAncestorAsync(target, parts, knownBySource),
					path => CheckReadAsync(() => ps.CanViewAttribute(executor, obj, path))))
			{
				permitted.Add(attr);
			}
		}

		return permitted
			.OrderBy(x => x.LongName, _attributeSort)
			.ToArray();
	}

	/// <summary>
	/// <paramref name="obj"/> followed by its <c>@parent</c> chain, capped at
	/// <c>Limit.MaxParents</c> - PennMUSH's <c>MAX_PARENTS</c> (<c>hdrs/conf.h:458</c>), the same
	/// bound its own <c>can_read_attr_internal</c> loop uses.
	/// </summary>
	/// <remarks>
	/// KNOWN, PRE-EXISTING, near-unreachable: the providers' own inheritance traversals run
	/// <c>MaxParents</c>, so a leaf resolved BEYOND this cap has a source that is not in this chain.
	/// On the read walk that now means the caller reports a permission error where Penn - whose
	/// <c>atr_get_with_parent</c> is bounded by the same <c>MAX_PARENTS</c> - would simply not find
	/// the attribute and return nothing. Wrong failure mode, right refusal. Reaching it requires a
	/// chain deeper than <c>MaxParents</c>, which <see cref="ExceedsMaxParentDepthAsync"/> refuses to
	/// build; only legacy or hand-edited data could. The fix belongs in the three providers (bound
	/// their traversals to <c>MaxParents</c>), not here.
	/// </remarks>
	private ValueTask<List<DBRef>> ParentChainAsync(AnySharpObject obj)
		=> ParentChainAsync(obj, ExecutionBudget.CurrentToken);

	private async ValueTask<List<DBRef>> ParentChainAsync(AnySharpObject obj, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		var chain = await AttributeAncestry.ChainAsync(obj.Object(), (int)configuration.CurrentValue.Limit.MaxParents, token);
		token.ThrowIfCancellationRequested();
		return [.. chain];
	}

	/// <inheritdoc/>
	public async ValueTask<bool> ExceedsMaxParentDepthAsync(AnySharpObject prospectiveParent, CancellationToken cancellationToken = default)
	{
		var maxParents = (int)configuration.CurrentValue.Limit.MaxParents;
		var seen = new HashSet<int> { prospectiveParent.Object().DBRef.Number };
		var current = prospectiveParent.Object();

		for (var count = 0; count < maxParents; count++)
		{
			if (await current.Parent.WithCancellation(cancellationToken) is not AnySharpObject parent) return false;

			var parentObj = parent.Object();

			// A pre-existing cycle isn't this method's concern (SafeToAddParent's reachability
			// check owns that); stop rather than spin so this can't itself hang on legacy/bad data.
			if (!seen.Add(parentObj.DBRef.Number)) return false;

			current = parentObj;
		}

		// Walked maxParents hops above prospectiveParent without reaching the end of the chain:
		// PennMUSH's do_parent refuses here even though the loop can't tell whether the chain
		// was about to terminate on the very next hop (src/set.c:1432-1446).
		return true;
	}

	/// <summary>
	/// Resolves one ancestor node on <paramref name="target"/> for the read walk, serving it from
	/// <paramref name="knownBySource"/> when that target already produced it as a pattern match and
	/// querying only otherwise. Returns null when no such attribute exists on that target, which
	/// <see cref="AttributeAncestry"/> turns into "abandon this target", not into a grant.
	/// </summary>
	private async ValueTask<SharpAttribute?> FetchAncestorAsync(DBRef target, string[] path,
		IReadOnlyDictionary<DBRef, Dictionary<string, SharpAttribute>> knownBySource)
	{
		if (knownBySource.TryGetValue(target, out var known)
				&& known.TryGetValue(string.Join('`', path), out var attribute))
		{
			return attribute;
		}

		return await mediator
			.CreateStream(new GetAttributeQuery(target, path), ExecutionBudget.CurrentToken)
			.LastOrDefaultAsync(ExecutionBudget.CurrentToken);
	}

	/// <summary>
	/// The single-attribute read walk's ancestor resolver. The source object's own prefixes are
	/// already in hand - <paramref name="resolved"/> IS that object's root..leaf path, root-first -
	/// so they are served from it at zero cost, which keeps the overwhelmingly common self-sourced
	/// tree read at exactly the query count it had before the walk existed. Only the genuinely new
	/// work, resolving the same prefix names against targets NEARER than the source, reaches the
	/// database.
	/// </summary>
	private ValueTask<SharpAttribute?> FetchReadWalkAncestorAsync(DBRef target, string[] path,
		DBRef source, SharpAttribute[] resolved)
		=> target.SameObjectAs(source) && ResolvedPrefix(path, resolved, static x => x.LongName) is { } known
			? ValueTask.FromResult<SharpAttribute?>(known)
			: FetchAncestorAsync(target, path, NoKnownAttributes);

	/// <inheritdoc cref="FetchReadWalkAncestorAsync"/>
	private ValueTask<LazySharpAttribute?> FetchLazyReadWalkAncestorAsync(DBRef target, string[] path,
		DBRef source, LazySharpAttribute[] resolved)
		=> target.SameObjectAs(source) && ResolvedPrefix(path, resolved, static x => x.LongName) is { } known
			? ValueTask.FromResult<LazySharpAttribute?>(known)
			: FetchLazyAncestorAsync(target, path, NoKnownLazyAttributes);

	/// <summary>
	/// The already-materialised node for <paramref name="path"/> within a root..leaf
	/// <paramref name="resolved"/> path, or null to fall back to a query. Positional, because the
	/// providers return the path root-first, but the name is verified rather than assumed - a
	/// provider that ever returned a different order (or a short path) must cost an extra query,
	/// never hand the flag test the WRONG node under the right name.
	/// </summary>
	private static T? ResolvedPrefix<T>(string[] path, T[] resolved, Func<T, string> longNameOf)
		where T : class
	{
		if (path.Length > resolved.Length)
		{
			return null;
		}

		var candidate = resolved[path.Length - 1];
		return AttributePathEquals(longNameOf(candidate), path)
			? candidate
			: null;
	}

	/// <summary>
	/// Whether <paramref name="longName"/> is exactly <paramref name="path"/> joined with <c>`</c>,
	/// case-insensitively, without building the joined string - this runs on every tree read.
	/// </summary>
	private static bool AttributePathEquals(ReadOnlySpan<char> longName, string[] path)
	{
		var depth = 0;
		foreach (var segment in longName.Split('`'))
		{
			if (depth == path.Length || !longName[segment].Equals(path[depth], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			depth++;
		}

		return depth == path.Length;
	}

	/// <summary>
	/// The single-attribute read walk holds only ONE object's materialised path (served by
	/// <see cref="ResolvedPrefix{T}"/>), so every other target it visits starts from nothing -
	/// unlike the pattern paths, which can reuse sibling matches.
	/// </summary>
	private static readonly Dictionary<DBRef, Dictionary<string, SharpAttribute>> NoKnownAttributes = [];

	/// <inheritdoc cref="NoKnownAttributes"/>
	private static readonly Dictionary<DBRef, Dictionary<string, LazySharpAttribute>> NoKnownLazyAttributes = [];

	/// <inheritdoc cref="FetchAncestorAsync"/>
	private ValueTask<LazySharpAttribute?> FetchLazyAncestorAsync(DBRef target, string[] path,
		IReadOnlyDictionary<DBRef, Dictionary<string, LazySharpAttribute>> knownBySource)
		=> FetchLazyAncestorAsync(target, path, knownBySource, ExecutionBudget.CurrentToken);

	private async ValueTask<LazySharpAttribute?> FetchLazyAncestorAsync(DBRef target, string[] path,
		IReadOnlyDictionary<DBRef, Dictionary<string, LazySharpAttribute>> knownBySource, CancellationToken token)
	{
		if (knownBySource.TryGetValue(target, out var known)
				&& known.TryGetValue(string.Join('`', path), out var attribute))
		{
			return attribute;
		}

		return await mediator
			.CreateStream(new GetLazyAttributeQuery(target, path), token)
			.LastOrDefaultAsync(token);
	}

	/// <summary>
	/// PennMUSH's <c>wildcard(s)</c> (<c>hdrs/externs.h:529</c>), which is
	/// <c>wildcard_count(s, 0) == -1</c> (<c>src/wild.c:713-729</c>): true when the string
	/// contains an unescaped <c>*</c> or <c>?</c>. A backslash escapes the character after it,
	/// and a trailing backslash escapes nothing.
	/// </summary>
	internal static bool HasUnescapedWildcard(string pattern)
	{
		for (var i = 0; i < pattern.Length; i++)
		{
			switch (pattern[i])
			{
				case '?':
				case '*':
					return true;
				case '\\':
					i++;
					break;
			}
		}

		return false;
	}

	/// <summary>
	/// Indexes already-materialised attributes by long name for the ancestor walk.
	/// Case-insensitive, as attribute names are; last write wins on a duplicate rather
	/// than throwing the way <c>ToDictionary</c> would.
	/// </summary>
	internal static Dictionary<string, T> IndexByLongName<T>(IEnumerable<T> attributes, Func<T, string> longNameOf)
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
		var token = ExecutionBudget.CurrentToken;
		token.ThrowIfCancellationRequested();
		var isPrivileged = executor.IsGod() || await executor.IsWizard(token);
		return LazySharpAttributesOrError.FromAsync(ReadWithDeferredBudget(
			budget => ReadLazyPattern(executor, obj, attributePattern, checkParents, mode, isPrivileged, budget),
			ExecutionBudget.Current, token));
	}

	private async IAsyncEnumerable<T> ReadWithDeferredBudget<T>(Func<ExecutionBudget, IAsyncEnumerable<T>> read,
		ExecutionBudget? originatingBudget, CancellationToken executionToken,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// Every deferred read retains its origin and the consumer's shorter lifetime.
		var consumerBudget = ExecutionBudget.Current;
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(executionToken, cancellationToken, ExecutionBudget.CurrentToken);
		var remaining = originatingBudget?.Remaining ?? TimeSpan.MaxValue;
		var consumerRemaining = consumerBudget?.Remaining ?? TimeSpan.MaxValue;
		if (consumerRemaining < remaining) remaining = consumerRemaining;
		using var budget = new ExecutionBudget(remaining == TimeSpan.MaxValue ? Timeout.InfiniteTimeSpan : remaining, linked.Token);
		budget.ThrowIfExceeded();
		await foreach (var item in read(budget).WithCancellation(budget.Token))
		{
			budget.ThrowIfExceeded();
			yield return item;
		}
	}

	private IAsyncEnumerable<LazySharpAttribute> ReadLazyPattern(AnySharpObject executor,
		AnySharpObject obj, string attributePattern, bool checkParents, IAttributeService.AttributePatternMode mode,
		bool isPrivileged, ExecutionBudget budget)
	{
		var attributes = mediator.CreateStream(
			new GetLazyAttributesQuery(obj.Object().DBRef, attributePattern.ToUpper(), checkParents, mode), budget.Token);
		// Privilege skips the ancestor walk but never the leaf's own internal flag.
		return isPrivileged
			? attributes.Where(x => !x.Attribute.IsInternal()).Select(x => x.Attribute).OrderBy(x => x.LongName, _attributeSort)
			: FilterLazyAttributes(executor, obj, attributes, budget);
	}

	private async IAsyncEnumerable<LazySharpAttribute> FilterLazyAttributes(
		AnySharpObject executor, AnySharpObject obj, IAsyncEnumerable<LazyAttributeWithSource> attributes, ExecutionBudget budget,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// See GetAttributePatternAsync: permission follows the real root..leaf path, re-walked
		// over the target chain from `obj` outward - not whatever subset of the tree the pattern
		// happened to match, and not a single object.
		var results = await attributes.ToArrayAsync(cancellationToken);
		var knownBySource = results
			.GroupBy(x => x.SourceObject)
			.ToDictionary(g => g.Key, g => IndexByLongName(g.Select(x => x.Attribute), static x => x.LongName));

		var ordered = results.OrderBy(x => x.Attribute.LongName, _attributeSort);
		var origin = obj.Object().DBRef;
		List<DBRef>? parentChain = null;

		foreach (var (attr, source) in ordered)
		{
			cancellationToken.ThrowIfCancellationRequested();
			bool canRead;
			// Async iterators do not retain an ambient scope across yield boundaries.
			// Enter it only around this item's legacy permission reads, and pass the
			// iterator token explicitly to every token-aware read-walk helper.
			using (budget.Enter())
			{
				var chain = source.SameObjectAs(origin)
					? [origin]
					: parentChain ??= await ParentChainAsync(obj, cancellationToken);
				canRead = await AttributeAncestry.CanReadAsync(attr, source, chain, origin,
					(target, parts) => FetchLazyAncestorAsync(target, parts, knownBySource, cancellationToken),
					path => CheckReadAsync(() => ps.CanViewAttribute(executor, obj, path), cancellationToken));
			}
			if (canRead) yield return attr;
		}

	}

	public ValueTask<Result<Success>> SetAttributeFlagAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute, string flag)
		=> SetAttributeFlagsAsync(executor, obj, attribute, [flag]);

	public ValueTask<Result<Success>> UnsetAttributeFlagAsync(AnySharpObject executor,
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
	public async ValueTask<Result<Success>> SetAttributeFlagsAsync(AnySharpObject executor,
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
		return await GetAttributeAsync(executor, obj, attribute, IAttributeService.AttributeMode.SystemSet, false) switch
		{
			SharpAttribute[] chain => await _flagWriter.ApplyAsync(executor, obj, chain, flagTokens),
			None => new Error<string>(ErrorMessages.Returns.ObjectAttributeString),
			Error<string> error => error
		};
	}

}
