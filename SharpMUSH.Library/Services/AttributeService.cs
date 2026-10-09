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
using SharpMUSH.Library.Common;

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
	private readonly AttributeWriter _writer = new(mediator, ps, notifyService, validateService, configuration, serviceProvider);

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
	private AttributeEvaluator Evaluator => _evaluator ??= new AttributeEvaluator(this, ps, mediator, locateService,
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
		=> await ReadAttributeAsync(executor, obj, attribute, mode, parent, EagerShape) switch
		{
			{ Found: { } found } => found,
			{ Error: { } error } => new Error<string>(error),
			_ => new None()
		};

	/// <summary>
	/// What <see cref="ReadAttributeAsync{T}"/> needs to know about one attribute representation:
	/// how to resolve a path with inheritance, how to fetch one prefix on one target, and which
	/// <see cref="IPermissionService"/> overloads gate it. One instance each for
	/// <see cref="SharpAttribute"/> and <see cref="LazySharpAttribute"/>, so the eager and lazy
	/// reads run the same walk rather than two copies of it.
	/// </summary>
	/// <param name="ServesWriteModes">
	/// Whether <c>Set</c>/<c>SystemSet</c> are served. The lazy read is not: a write needs the value
	/// it is gating, so deferring it buys nothing (see <see cref="IAttributeService.LazilyGetAttributeAsync"/>).
	/// </param>
	private sealed record AttributeReadShape<T>(
		Func<DBRef, string[], bool, InheritanceWalk?, CancellationToken, ValueTask<ResolvedAttribute<T>?>> Resolve,
		Func<DBRef, string[], ValueTask<T?>> FetchPrefix,
		Func<AnySharpObject, AnySharpObject, T[], ValueTask<bool>> CanView,
		Func<AnySharpObject, AnySharpObject, T[], ValueTask<bool>> CanExecute,
		Func<T, string> LongNameOf,
		Func<T, bool> IsNoInherit,
		Func<T, bool> IsInternal,
		bool ServesWriteModes)
		where T : class;

	/// <summary>An attribute path resolved with inheritance: the element-type-neutral part of
	/// <see cref="AttributeWithInheritance"/> and <see cref="LazyAttributeWithInheritance"/>.</summary>
	private sealed record ResolvedAttribute<T>(T[] Attributes, DBRef SourceObject, AttributeSource Source);

	/// <summary>The outcome of one read: the resolved path, a permission/validation error, or neither (none).</summary>
	private readonly record struct AttributeRead<T>(T[]? Found, string? Error);

	private AttributeReadShape<SharpAttribute> EagerShape => _eagerShape ??= new(
		async (dbref, path, parent, walk, token) =>
			await mediator.CreateStream(new GetAttributeWithInheritanceQuery(dbref, path, parent, walk), token)
				.FirstOrDefaultAsync(token) is { } hit
				? new ResolvedAttribute<SharpAttribute>(hit.Attributes, hit.SourceObject, hit.Source)
				: null,
		(target, path) => FetchAncestorAsync(target, path, NoKnownAttributes),
		(who, target, path) => ps.CanViewAttribute(who, target, path),
		(who, target, path) => ps.CanExecuteAttribute(who, target, path),
		static x => x.LongName,
		static x => x.IsNoInherit(),
		static x => x.IsInternal(),
		ServesWriteModes: true);

	private AttributeReadShape<SharpAttribute>? _eagerShape;

	private AttributeReadShape<LazySharpAttribute> LazyShape => _lazyShape ??= new(
		async (dbref, path, parent, walk, token) =>
			await mediator.CreateStream(new GetLazyAttributeWithInheritanceQuery(dbref, path, parent, walk), token)
				.FirstOrDefaultAsync(token) is { } hit
				? new ResolvedAttribute<LazySharpAttribute>(hit.Attributes, hit.SourceObject, hit.Source)
				: null,
		(target, path) => FetchLazyAncestorAsync(target, path, NoKnownLazyAttributes),
		(who, target, path) => ps.CanViewAttribute(who, target, path),
		(who, target, path) => ps.CanExecuteAttribute(who, target, path),
		static x => x.LongName,
		static x => x.IsNoInherit(),
		static x => x.IsInternal(),
		ServesWriteModes: false);

	private AttributeReadShape<LazySharpAttribute>? _lazyShape;

	/// <summary>
	/// The single-attribute read gate behind both <see cref="GetAttributeAsync"/> and
	/// <see cref="LazilyGetAttributeAsync"/>: validate the name, resolve it with inheritance (PennMUSH's
	/// <c>atr_get_with_parent</c>, the type ancestor and the alias retry included, all in the provider),
	/// and gate the result on the mode's permission - re-walking the branch per
	/// <see cref="ReadWalkApplies"/> where PennMUSH does.
	/// </summary>
	private async ValueTask<AttributeRead<T>> ReadAttributeAsync<T>(
		AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		IAttributeService.AttributeMode mode,
		bool parent,
		AttributeReadShape<T> shape)
		where T : class
	{
		var cancellationToken = ExecutionBudget.CurrentToken;
		cancellationToken.ThrowIfCancellationRequested();
		var attributePath = attribute.Split('`');

		if (!await CheckReadAsync(() => validateService.Valid(IValidateService.ValidationType.AttributeName, MarkupText.Plain(attribute), obj)))
		{
			return new AttributeRead<T>(null, ErrorMessages.Returns.ObjectAttributeString);
		}

		if (!shape.ServesWriteModes && mode is not (IAttributeService.AttributeMode.Read or IAttributeService.AttributeMode.Execute))
		{
			throw new InvalidOperationException(nameof(IAttributeService.AttributeMode));
		}

		Func<AnySharpObject, AnySharpObject, T[], ValueTask<bool>> permissionPredicate = mode switch
		{
			IAttributeService.AttributeMode.Read => (who, target, path) => CheckReadAsync(() => shape.CanView(who, target, path)),
			IAttributeService.AttributeMode.Execute => (who, target, path) => CheckReadAsync(() => shape.CanExecute(who, target, path)),
			IAttributeService.AttributeMode.Set => (who, target, path) => CheckReadAsync(() => shape.CanExecute(who, target, path)),
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

		var walk = parent ? await InheritanceWalkAsync(obj) : (InheritanceWalk?)null;
		var result = await shape.Resolve(obj.Object().DBRef, attributePath, parent, walk, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		if (result == null)
		{
			return default;
		}

		var allowed = ReadWalkApplies(mode, result.Attributes, shape.LongNameOf)
			? await CanReadThroughTargetsAsync(obj, result.Attributes[^1], result.SourceObject, walk,
				(target, parts) => FetchReadWalkAncestorAsync(target, parts, result.SourceObject, result.Attributes, shape),
				path => CheckReadAsync(() => shape.CanView(executor, obj, path)), shape.LongNameOf, shape.IsNoInherit)
			: await permissionPredicate(executor, obj, result.Attributes);

		return allowed
			? new AttributeRead<T>(result.Attributes, null)
			: new AttributeRead<T>(null, permissionFailureType);
	}

	/// <summary>
	/// The reach of <paramref name="obj"/>'s inherited reads: its type ancestor (<c>Ancestor_Parent</c>:
	/// none when ORPHAN or disabled) and the configured <c>MAX_PARENTS</c>.
	/// </summary>
	private async ValueTask<InheritanceWalk> InheritanceWalkAsync(AnySharpObject obj)
		=> new(await obj.Ancestor(configuration), (int)configuration.CurrentValue.Limit.MaxParents);

	/// <summary>
	/// PennMUSH's <c>can_read_attr_internal</c> tree walk for <paramref name="leaf"/>, found on
	/// <paramref name="source"/>, over the targets <c>atr_get_with_parent</c> visits from
	/// <paramref name="obj"/> (<see cref="ReadTargetsAsync"/>). A leaf found on the object itself
	/// returns at the first target, so it never needs the chain.
	/// </summary>
	private async ValueTask<bool> CanReadThroughTargetsAsync<T>(AnySharpObject obj, T leaf, DBRef source,
		InheritanceWalk? walk, Func<DBRef, string[], ValueTask<T?>> fetch, Func<T[], ValueTask<bool>> permits,
		Func<T, string> longNameOf, Func<T, bool> isNoInherit)
		where T : class
	{
		var origin = obj.Object().DBRef;
		IReadOnlyList<DBRef> targets = source.SameObjectAs(origin)
			? [origin]
			: await ReadTargetsAsync(obj, walk ?? InheritanceWalk.ParentsOnly);
		return await AttributeAncestry.CanReadAsync(leaf, source, targets, origin, fetch, permits, longNameOf, isNoInherit);
	}

	/// <summary>
	/// The targets <c>atr_get_with_parent</c> and <c>can_read_attr_internal</c> visit from
	/// <paramref name="obj"/>, in order; see <see cref="AttributeAncestry.TargetsAsync"/>.
	/// </summary>
	private async ValueTask<DBRef[]> ReadTargetsAsync(AnySharpObject obj, InheritanceWalk walk)
	{
		var token = ExecutionBudget.CurrentToken;
		var ancestor = walk.Ancestor is { } ancestorRef
			&& await mediator.Send(new GetObjectNodeQuery(ancestorRef), token) is AnySharpObject found
				? found.Object()
				: null;
		var targets = await AttributeAncestry.TargetsAsync(obj.Object(), ancestor, walk.MaxParents, token);
		token.ThrowIfCancellationRequested();
		return targets;
	}

	/// <summary>PennMUSH's built-in attribute aliases (<c>attralias</c>, <c>hdrs/atr_tab.h</c>).</summary>
	public static readonly IReadOnlyDictionary<string, string> StandardAttributeAliases =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["DESC"] = "DESCRIBE",
			["IDESC"] = "IDESCRIBE",
			["SUCC"] = "SUCCESS",
			["ASUCC"] = "ASUCCESS",
			["OSUCC"] = "OSUCCESS",
			["FAIL"] = "FAILURE",
			["AFAIL"] = "AFAILURE",
			["OFAIL"] = "OFAILURE"
		};

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
	/// Penn re-walks on EVERY read, from <c>obj</c> outward along the targets
	/// <c>atr_get_with_parent</c> visits - not over the single object the leaf happened to resolve on.
	/// Testing only the source-resolved path fails OPEN two ways: a restrictively-flagged branch of the
	/// same name on a NEARER object is never tested at all (Penn's <c>return 0</c> is inline at
	/// <c>attrib.c:331</c> - it does not <c>continue_target</c>), and a nearer target that holds a
	/// failing prefix but not the leaf is skipped as merely "incomplete" instead of denying.
	/// </para>
	/// </summary>
	/// <remarks>
	/// <list type="bullet">
	/// <item>
	/// <b>Flat names short-circuit.</b> An attribute with no <c>`</c> IS its whole path, and Penn
	/// returns 1 before the walk even starts (<c>attrib.c:311-312</c>). Walking would cost a chain
	/// read on the hottest read path in the server for no decision at all. The resolved name is the
	/// one that counts: an alias such as <c>DESC</c> names a flat standard attribute.
	/// </item>
	/// <item>
	/// <b>Read only.</b> <c>Set</c>/<c>SystemSet</c> are <c>can_write_attr_internal</c>'s business
	/// (a different function with different rules - see <see cref="SetAttributeFlagsAsync"/>).
	/// Gating <c>Execute</c> on <c>Can_Read_Attr</c> is what Penn's <c>fun_ufun</c> actually does,
	/// but that is a behaviour change beyond this fix and is deliberately left alone.
	/// </item>
	/// </list>
	/// </remarks>
	private static bool ReadWalkApplies<T>(IAttributeService.AttributeMode mode, T[] resolved, Func<T, string> longNameOf)
		=> mode == IAttributeService.AttributeMode.Read
			&& longNameOf(resolved[^1]).Contains('`');

	/// <inheritdoc/>
	/// <remarks>
	/// The same read gate as <see cref="GetAttributeAsync"/> — one <see cref="ReadAttributeAsync{T}"/>
	/// walk, over <see cref="LazySharpAttribute"/> — minus the two write modes it does not serve.
	/// </remarks>
	public async ValueTask<OptionalLazySharpAttributeOrError> LazilyGetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute,
		IAttributeService.AttributeMode mode, bool parent = true)
		=> await ReadAttributeAsync(executor, obj, attribute, mode, parent, LazyShape) switch
		{
			{ Found: { } found } => found,
			{ Error: { } error } => new Error<string>(error),
			_ => new None()
		};


	public async ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj,
		int depth = 1)
	{
		var token = ExecutionBudget.CurrentToken;
		token.ThrowIfCancellationRequested();
		return await GetVisibleAttributesAsync(obj.Object().Attributes.Value, executor, obj, Math.Max(1, depth), new AttributeViewMemo(), token).ToArrayAsync(token);
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
		AttributeViewMemo memo, [EnumeratorCancellation] CancellationToken token = default)
	{
		token.ThrowIfCancellationRequested();
		if (depth <= 0) yield break;
		var visible = await attributes
			.Where((x, _) => CheckReadAsync(() => ps.CanViewAttribute(executor, obj, memo, x), token))
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
			await foreach (var descendant in GetVisibleAttributesAsync(leaves, executor, obj, depth - 1, memo, token).WithCancellation(token))
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
		var memo = new AttributeViewMemo();
		for (var remaining = depth; remaining > 0; remaining--)
		{
			var nextLevel = new List<IAsyncEnumerable<LazySharpAttribute>>();
			await foreach (var attribute in attributes.WithCancellation(token))
			{
				bool visible;
				using (budget.Enter())
					visible = await CheckReadAsync(() => ps.CanViewAttribute(executor, obj, memo, attribute), token);
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
	/// <remarks>
	/// PennMUSH's <c>atr_iter_get</c>/<c>atr_iter_get_parent</c> (<c>src/attrib.c:1330-1650</c>). A
	/// literal name - no unescaped wildcard, not regex, not ending in a backtick - is read the way
	/// <c>get()</c> reads it (<see cref="ReadLiteralPatternAsync{T}"/>); anything else is matched over
	/// the object and, with <paramref name="checkParents"/>, its <c>@parent</c> chain, and listed object
	/// by object, the object's own matches first (<see cref="InChainOrder{T}"/>).
	/// </remarks>
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
		var token = ExecutionBudget.CurrentToken;
		var isPrivileged = executor.IsGod() || await executor.IsWizard(token);

		if (IsLiteralPattern(attributePattern, mode))
		{
			SharpAttribute[] found = await ReadLiteralPatternAsync(executor, obj, attributePattern, checkParents, isPrivileged,
				EagerShape, token) is { } literal
				? [literal]
				: [];
			return found;
		}

		var attributes = mediator.CreateStream(
			new GetAttributesQuery(obj.Object().DBRef, attributePattern.ToUpper(), checkParents, mode), token);

		var results = InChainOrder(await attributes.ToArrayAsync(token),
			static x => x.SourceObject, static x => x.Attribute.LongName);

		if (isPrivileged)
		{
			// PennMUSH's Can_Read_Attr macro (hdrs/mushdb.h:100-101) is
			// `!AF_Internal(a) && (See_All(p) || can_read_attr_internal(...))`: See_All skips the
			// whole ancestor walk (and with it mortal_dark, visual and nearby), but it does NOT
			// skip the leaf's own internal flag, which is tested first and denies everyone
			// including God. Without this the privileged early-out listed internal attributes.
			return results
				.Where(x => !x.Attribute.IsInternal())
				.Select(x => x.Attribute)
				.ToArray();
		}

		// A pattern can name a leaf without matching any of its ancestors, so the result
		// set alone never proves a branch is safe to reveal. Walk the real root..leaf path
		// for each match - PennMUSH re-checks every level, so a mortal_dark (or non-visual)
		// branch hides its leaves however narrow the pattern was. See CanReadPatternMatchAsync
		// for which object the test is made against. Attributes matched on a given object are
		// reused as free ancestor data for that object only - one object's FOO must never vouch
		// for another object's FOO`BAR.
		// Indexed only when a branch walk first asks: a listing of flat names never needs it.
		Dictionary<DBRef, Dictionary<string, SharpAttribute>>? knownBySource = null;
		List<DBRef>? parentChain = null;
		var ancestors = new Dictionary<(DBRef Target, string Path), SharpAttribute?>();
		var holders = new Dictionary<DBRef, AnySharpObject?>();
		var memo = new AttributeViewMemo();
		Func<ValueTask<List<DBRef>>> chainOf = async () => parentChain ??= await ParentChainAsync(obj, token);
		Func<DBRef, string[], ValueTask<SharpAttribute?>> fetch = (target, parts) => MemoizedAncestorAsync(ancestors, target, parts,
			() => FetchAncestorAsync(target, parts, knownBySource ??= KnownBySource(results, static x => x.SourceObject, static x => x.Attribute, static x => x.LongName)));
		Func<AnySharpObject, SharpAttribute[], ValueTask<bool>> canView =
			(target, path) => CheckReadAsync(() => ps.CanViewAttribute(executor, target, memo, path), token);

		var permitted = new List<SharpAttribute>();
		foreach (var (attr, source) in results)
		{
			token.ThrowIfCancellationRequested();
			if (await CanReadPatternMatchAsync(obj, attr, source, chainOf, holders, fetch, canView,
					static x => x.LongName, static x => x.IsNoInherit(), token))
			{
				permitted.Add(attr);
			}
		}

		return permitted.ToArray();
	}

	/// <summary>
	/// Whether <paramref name="pattern"/> takes PennMUSH's literal fast path in <c>atr_iter_get</c>
	/// and <c>atr_iter_get_parent</c> (<c>src/attrib.c:1351</c>, <c>:1522</c>): not a regex, not ending
	/// in a backtick (which lists a branch's children), and no unescaped wildcard.
	/// </summary>
	internal static bool IsLiteralPattern(string pattern, IAttributeService.AttributePatternMode mode)
		=> mode != IAttributeService.AttributePatternMode.Regex
			&& pattern.Length > 0
			&& !pattern.EndsWith('`')
			&& !HasUnescapedWildcard(pattern);

	/// <summary>
	/// The literal fast path (<c>src/attrib.c:1351-1356</c>, <c>:1522-1529</c>): the name is read as
	/// <c>get()</c> reads it - <c>atr_get_with_parent</c> with <paramref name="checkParents"/> (parents,
	/// type ancestor, alias), <c>atr_get_noparent</c> without (the object, alias) - and shown when the
	/// viewer can read it on the object that holds it (<c>Can_Read_Attr(player, parent, ptr)</c>).
	/// </summary>
	private async ValueTask<T?> ReadLiteralPatternAsync<T>(AnySharpObject executor, AnySharpObject obj, string name,
		bool checkParents, bool isPrivileged, AttributeReadShape<T> shape, CancellationToken token)
		where T : class
	{
		var walk = checkParents ? await InheritanceWalkAsync(obj) : (InheritanceWalk?)null;
		if (await shape.Resolve(obj.Object().DBRef, name.Split('`'), checkParents, walk, token) is not { } resolved)
		{
			return null;
		}

		var leaf = resolved.Attributes[^1];
		if (isPrivileged)
		{
			return shape.IsInternal(leaf) ? null : leaf;
		}

		var source = resolved.SourceObject;
		if (await HolderAsync(obj, source, [], token) is not AnySharpObject holder)
		{
			return null;
		}

		// The leaf is on the holder, so the holder's own walk ends at its first target.
		return await AttributeAncestry.CanReadAsync(leaf, source, [source], source,
			(target, parts) => FetchReadWalkAncestorAsync(target, parts, source, resolved.Attributes, shape),
			path => CheckReadAsync(() => shape.CanView(executor, holder, path), token),
			shape.LongNameOf, shape.IsNoInherit)
			? leaf
			: null;
	}

	/// <summary>
	/// Whether a wildcard or regex match found on <paramref name="source"/> is listed
	/// (<c>atr_iter_get_parent</c>, <c>src/attrib.c:1580-1625</c>).
	/// <list type="bullet">
	/// <item>A match on the object itself is tested against it.</item>
	/// <item>
	/// An inherited match is tested against the object holding it,
	/// <c>Can_Read_Attr(player, parent, ptr)</c> (<c>:1584</c>). The leaf is on that object, so its walk
	/// ends there.
	/// </item>
	/// <item>
	/// A branch attribute is also reached by its root's sub-branch loop (<c>:1595-1622</c>), which tests
	/// it against the object the lookup was made on, <c>Can_Read_Attr(player, thing, ptr)</c>
	/// (<c>:1617</c>), and is listed if either test passes. That loop runs on the holder for every root
	/// no nearer object has, so it covers the attribute exactly when the branch directly above it is on
	/// no nearer object (a node on an object implies its own prefixes there).
	/// </item>
	/// </list>
	/// </summary>
	private async ValueTask<bool> CanReadPatternMatchAsync<T>(AnySharpObject obj, T attr, DBRef source,
		Func<ValueTask<List<DBRef>>> chainOf, Dictionary<DBRef, AnySharpObject?> holders,
		Func<DBRef, string[], ValueTask<T?>> fetch, Func<AnySharpObject, T[], ValueTask<bool>> canView,
		Func<T, string> longNameOf, Func<T, bool> isNoInherit, CancellationToken token)
		where T : class
	{
		var origin = obj.Object().DBRef;
		var isBranch = longNameOf(attr).Contains('`');
		if (source.SameObjectAs(origin))
		{
			return await CanReadOnHolderAsync(obj);
		}

		if (await HolderAsync(obj, source, holders, token) is AnySharpObject holder
				&& await CanReadOnHolderAsync(holder))
		{
			return true;
		}

		if (!isBranch)
		{
			return false;
		}

		var segments = longNameOf(attr).Split('`');
		var chain = await chainOf();
		foreach (var target in chain.TakeWhile(target => !target.SameObjectAs(source)))
		{
			if (await fetch(target, segments[..^1]) is not null)
			{
				return false;
			}
		}

		return await AttributeAncestry.CanReadAsync(attr, source, chain, origin, fetch,
			path => canView(obj, path), longNameOf, isNoInherit);

		// The leaf is on the holder, so the holder's own walk ends at its first target, and a flat name
		// has no prefixes to walk: its walk is the leaf's own test.
		async ValueTask<bool> CanReadOnHolderAsync(AnySharpObject holder)
			=> isBranch
				? await AttributeAncestry.CanReadAsync(attr, source, [source], source, fetch,
					path => canView(holder, path), longNameOf, isNoInherit)
				: await canView(holder, [attr]);
	}

	/// <summary>The object an attribute was found on: <paramref name="obj"/> itself, or one loaded once per call.</summary>
	private async ValueTask<AnySharpObject?> HolderAsync(AnySharpObject obj, DBRef source,
		Dictionary<DBRef, AnySharpObject?> holders, CancellationToken token)
	{
		if (source.SameObjectAs(obj.Object().DBRef))
		{
			return obj;
		}

		if (!holders.TryGetValue(source, out var holder))
		{
			holder = await mediator.Send(new GetObjectNodeQuery(source), token) is AnySharpObject found ? found : null;
			holders[source] = holder;
		}

		return holder;
	}

	/// <summary>
	/// PennMUSH's listing order for a pattern walk (<c>src/attrib.c:1574-1580</c>): object by object
	/// along the chain, the object's own matches first. The walk yields the objects in that order, so
	/// each object's block keeps its place and is sorted within itself.
	/// </summary>
	private T[] InChainOrder<T>(IEnumerable<T> matches, Func<T, DBRef> sourceOf, Func<T, string> longNameOf)
		=> [.. matches.GroupBy(sourceOf).SelectMany(block => block.OrderBy(longNameOf, _attributeSort))];

	/// <summary>
	/// <paramref name="obj"/> followed by up to <c>Limit.MaxParents</c> of its <c>@parent</c> chain -
	/// the objects PennMUSH's wildcard and regex pattern walk visits
	/// (<c>parent_depth = MAX_PARENTS + 1</c>, <c>src/attrib.c:1574-1576</c>), with no type ancestor.
	/// </summary>
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

			// A pre-existing cycle isn't this method's concern (SafeToAddParentAsync's reachability
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
	/// One pattern read's memo of the branch nodes its read walks have resolved, keyed by the target
	/// they were resolved on and the full upper-cased path. Leaves under one branch all walk that
	/// branch's prefixes over the same targets, so without it a hundred matched leaves of
	/// <c>FOO`*</c> resolve <c>FOO</c> a hundred times. A miss is remembered too: it is an answer
	/// ("abandon this target"), not a reason to ask again. Per call, never shared: it holds the
	/// answers of one read, not a cache that a write would have to invalidate.
	/// </summary>
	private static async ValueTask<T?> MemoizedAncestorAsync<T>(Dictionary<(DBRef Target, string Path), T?> memo,
		DBRef target, string[] path, Func<ValueTask<T?>> fetch)
		where T : class
	{
		var key = (target, string.Join('`', path).ToUpperInvariant());
		if (memo.TryGetValue(key, out var known))
		{
			return known;
		}

		var found = await fetch();
		memo[key] = found;
		return found;
	}

	/// <summary>
	/// The single-attribute read walk's ancestor resolver. The source object's own prefixes are
	/// already in hand - <paramref name="resolved"/> IS that object's root..leaf path, root-first -
	/// so they are served from it at zero cost, which keeps the overwhelmingly common self-sourced
	/// tree read at exactly the query count it had before the walk existed. Only the genuinely new
	/// work, resolving the same prefix names against targets NEARER than the source, reaches the
	/// database.
	/// </summary>
	private static ValueTask<T?> FetchReadWalkAncestorAsync<T>(DBRef target, string[] path,
		DBRef source, T[] resolved, AttributeReadShape<T> shape)
		where T : class
		=> target.SameObjectAs(source) && ResolvedPrefix(path, resolved, shape.LongNameOf) is { } known
			? ValueTask.FromResult<T?>(known)
			: shape.FetchPrefix(target, path);

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
	/// A pattern read's matches indexed by the object each was read from, then by long name: the
	/// ancestor walk's free data, each object vouching only for its own attributes.
	/// </summary>
	private static Dictionary<DBRef, Dictionary<string, T>> KnownBySource<TMatch, T>(IEnumerable<TMatch> matches,
		Func<TMatch, DBRef> sourceOf, Func<TMatch, T> attributeOf, Func<T, string> longNameOf)
		=> matches
			.GroupBy(sourceOf)
			.ToDictionary(g => g.Key, g => IndexByLongName(g.Select(attributeOf), longNameOf));

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
		if (IsLiteralPattern(attributePattern, mode))
		{
			return ReadLazyLiteralPattern(executor, obj, attributePattern, checkParents, isPrivileged, budget);
		}

		var attributes = mediator.CreateStream(
			new GetLazyAttributesQuery(obj.Object().DBRef, attributePattern.ToUpper(), checkParents, mode), budget.Token);
		// Privilege skips the ancestor walk but never the leaf's own internal flag.
		return isPrivileged
			? PrivilegedLazyAttributes(attributes, budget.Token)
			: FilterLazyAttributes(executor, obj, attributes, budget);
	}

	/// <inheritdoc cref="ReadLiteralPatternAsync{T}"/>
	private async IAsyncEnumerable<LazySharpAttribute> ReadLazyLiteralPattern(AnySharpObject executor,
		AnySharpObject obj, string name, bool checkParents, bool isPrivileged, ExecutionBudget budget,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		LazySharpAttribute? literal;
		using (budget.Enter())
		{
			literal = await ReadLiteralPatternAsync(executor, obj, name, checkParents, isPrivileged, LazyShape, cancellationToken);
		}

		if (literal is not null)
		{
			yield return literal;
		}
	}

	private async IAsyncEnumerable<LazySharpAttribute> PrivilegedLazyAttributes(IAsyncEnumerable<LazyAttributeWithSource> attributes,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var results = InChainOrder(await attributes.ToArrayAsync(cancellationToken),
			static x => x.SourceObject, static x => x.Attribute.LongName);
		foreach (var (attr, _) in results)
		{
			if (!attr.IsInternal())
			{
				yield return attr;
			}
		}
	}

	private async IAsyncEnumerable<LazySharpAttribute> FilterLazyAttributes(
		AnySharpObject executor, AnySharpObject obj, IAsyncEnumerable<LazyAttributeWithSource> attributes, ExecutionBudget budget,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// See GetAttributePatternAsync: permission follows the real root..leaf path, tested against
		// the object CanReadPatternMatchAsync names - not whatever subset of the tree the pattern
		// happened to match.
		var results = await attributes.ToArrayAsync(cancellationToken);
		var ordered = InChainOrder(results, static x => x.SourceObject, static x => x.Attribute.LongName);
		Dictionary<DBRef, Dictionary<string, LazySharpAttribute>>? knownBySource = null;
		List<DBRef>? parentChain = null;
		var ancestors = new Dictionary<(DBRef Target, string Path), LazySharpAttribute?>();
		var holders = new Dictionary<DBRef, AnySharpObject?>();
		var memo = new AttributeViewMemo();
		Func<ValueTask<List<DBRef>>> chainOf = async () => parentChain ??= await ParentChainAsync(obj, cancellationToken);
		Func<DBRef, string[], ValueTask<LazySharpAttribute?>> fetch = (target, parts) => MemoizedAncestorAsync(ancestors, target, parts,
			() => FetchLazyAncestorAsync(target, parts,
				knownBySource ??= KnownBySource(results, static x => x.SourceObject, static x => x.Attribute, static x => x.LongName), cancellationToken));
		Func<AnySharpObject, LazySharpAttribute[], ValueTask<bool>> canView =
			(target, path) => CheckReadAsync(() => ps.CanViewAttribute(executor, target, memo, path), cancellationToken);

		foreach (var (attr, source) in ordered)
		{
			cancellationToken.ThrowIfCancellationRequested();
			bool canRead;
			// Async iterators do not retain an ambient scope across yield boundaries.
			// Enter it only around this item's legacy permission reads, and pass the
			// iterator token explicitly to every token-aware read-walk helper.
			using (budget.Enter())
			{
				canRead = await CanReadPatternMatchAsync(obj, attr, source, chainOf, holders, fetch, canView,
					static x => x.LongName, static x => x.IsNoInherit(), cancellationToken);
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
