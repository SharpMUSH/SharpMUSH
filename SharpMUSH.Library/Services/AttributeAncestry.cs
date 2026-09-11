using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's <c>can_read_attr_internal</c> tree walk (<c>src/attrib.c:318-356</c>), which decides
/// whether an attribute deep in a tree (<c>FOO`BAR`BAZ</c>) may be read by re-walking its
/// <c>`</c>-separated ancestor path on every access - a branch flagged e.g. mortal_dark hides its
/// leaves regardless of the leaf's own flags, and no computed flag is ever stored on the leaf.
/// </summary>
/// <remarks>
/// <para>
/// The walk is over TARGETS, not just over path segments. Penn starts at <c>target = obj</c> - the
/// object the lookup was made against - and moves outward along the <c>@parent</c> chain. At each
/// target it resolves the leaf's strict prefixes against THAT target and applies the flag test to
/// each one. Three distinct outcomes, and conflating any two of them is a permission bug:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Prefix missing</b> (or, on a target other than the original object, flagged
/// <c>no_inherit</c>/<c>AF_PRIVATE</c>): <c>goto continue_target</c> (<c>attrib.c:324-327</c>) -
/// abandon this target and try the next one outward. Not a denial.
/// </item>
/// <item>
/// <b>Prefix present but failing the flag test</b>: <c>return 0</c> inline
/// (<c>attrib.c:331-335</c>). This does NOT advance the walk - a restrictive branch on a NEARER
/// object denies even when the object that actually holds the leaf has a permissive one.
/// </item>
/// <item>
/// <b>All prefixes pass and the leaf is on this target</b>: <c>return 1</c>
/// (<c>attrib.c:339-341</c>). Falling off the end of the chain is <c>return 0</c>
/// (<c>attrib.c:356</c>) - a path that never resolved never grants.
/// </item>
/// </list>
/// <para>
/// Walking only the object the leaf came FROM gets the common inherited case right and the
/// shadowing case wrong (child holds its own restrictively-flagged <c>SECRETS</c> and no
/// <c>SECRETS`PUB</c>, parent holds both, permissively). Walking only the ORIGINAL object gets the
/// shadowing case right and every inherited tree attribute wrong. Both are needed, in order.
/// </para>
/// </remarks>
public static class AttributeAncestry
{
	/// <summary>
	/// The target chain a read walks: <paramref name="origin"/> itself, then its <c>@parent</c>
	/// chain outward, nearest first.
	/// </summary>
	/// <remarks>
	/// Cycle-guarded and depth-capped. A <c>@parent</c> cycle would otherwise spin here forever —
	/// defence in depth, since the write-side guards (<c>SafeToAddParent</c>,
	/// <c>ExceedsMaxParentDepthAsync</c>) should already prevent one from existing at all.
	/// <paramref name="maxDepth"/> counts parents, not targets, so the chain is at most
	/// <c>maxDepth + 1</c> long.
	/// </remarks>
	public static async ValueTask<DBRef[]> ChainAsync(
		SharpObject origin, int maxDepth, CancellationToken cancellationToken = default)
	{
		var chain = new List<DBRef> { origin.DBRef };
		var visited = new HashSet<int> { origin.DBRef.Number };
		var current = origin;

		for (var depth = 0; depth < maxDepth; depth++)
		{
			if (await current.Parent.WithCancellation(cancellationToken) is not AnySharpObject parent) break;

			var parentObj = parent.Object();
			if (!visited.Add(parentObj.DBRef.Number)) break;

			chain.Add(parentObj.DBRef);
			current = parentObj;
		}

		return chain.ToArray();
	}

	/// <summary>
	/// PennMUSH's <c>atr_iter_get_parent</c> (<c>src/attrib.c:1500-1622</c>): every attribute
	/// matching a pattern on <paramref name="originRef"/>, then on each <c>@parent</c> outward,
	/// each match paired with the object it was read from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Generic over the attribute representation so the eager and the lazy readers are one walk and
	/// cannot drift: the two share a documented contract (<c>GetLazyAttributesQuery</c> is an
	/// <c>&lt;inheritdoc&gt;</c> of <c>GetAttributesQuery</c>), and the lazy handler used to ignore
	/// <c>CheckParents</c> outright.
	/// </para>
	/// <para>
	/// <paramref name="originNode"/> is a callback rather than a parameter because the origin's own
	/// matches are streamed before the object node is ever loaded — an object that does not resolve
	/// still yields whatever the flat read found, exactly as Penn's iteration does.
	/// </para>
	/// </remarks>
	/// <param name="originRef">The object the lookup was made against.</param>
	/// <param name="originNode">Loads that object, or null when it does not resolve.</param>
	/// <param name="maxDepth">The configured <c>MaxParents</c> cap.</param>
	/// <param name="matches">Streams one target's matches for the caller's pattern and mode.</param>
	/// <param name="resolvePath">Resolves a full <c>`</c>-path on one target, for the no_inherit test.</param>
	/// <param name="longNameOf">The attribute's full <c>`</c>-separated name.</param>
	/// <param name="isNoInherit">Penn's <c>AF_PRIVATE</c> test.</param>
	public static async IAsyncEnumerable<(T Attribute, DBRef Source)> MatchesWithParentsAsync<T>(
		DBRef originRef,
		Func<ValueTask<SharpObject?>> originNode,
		int maxDepth,
		Func<DBRef, IAsyncEnumerable<T>> matches,
		Func<DBRef, string[], IAsyncEnumerable<T>> resolvePath,
		Func<T, string> longNameOf,
		Func<T, bool> isNoInherit,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		await foreach (var attr in matches(originRef).WithCancellation(cancellationToken))
		{
			if (seen.Add(longNameOf(attr)))
			{
				yield return (attr, originRef);
			}
		}

		var origin = await originNode();
		if (origin is null) yield break;

		var visited = new HashSet<int> { origin.DBRef.Number };
		var current = origin;
		for (var depth = 0; depth < maxDepth; depth++)
		{
			if (await current.Parent.WithCancellation(cancellationToken) is not AnySharpObject parent) break;

			var parentObj = parent.Object();

			// A @parent cycle would otherwise spin here forever - defence in depth, since the
			// write-side guards (SafeToAddParent, ExceedsMaxParentDepthAsync) should already
			// prevent one from existing at all. See ChainAsync, which mirrors this.
			if (!visited.Add(parentObj.DBRef.Number)) break;

			await foreach (var attr in matches(parentObj.DBRef).WithCancellation(cancellationToken))
			{
				// Penn's atr_iter_get_parent (attrib.c:1500-1622) has an early fast-path
				// (attrib.c:1522-1529): any literal, non-wildcarded pattern is routed straight
				// through atr_get_with_parent -- the same function backing get() -- and never
				// reaches the seen/st_insert iteration loop at all. Every pattern this walk
				// sees under AttributePatternMode.Exact is literal, so the operative reference
				// for those is atr_get_with_parent (attrib.c:1232-1252), identical to the fix
				// in GetAttributeWithInheritanceAsync: a private hit on a nearer ancestor
				// blocks resolution outright and never falls through to a farther ancestor's
				// unflagged copy. Recording membership before the flag check reproduces that
				// outcome for exact-mode lookups.
				//
				// The iteration loop (attrib.c:1580-1622, entered only for a genuine wildcard
				// or regex pattern) has its own st_insert-before-AF_Private ordering, which
				// gives the same shadowing property there too -- but that loop's private test
				// only continues the walk rather than aborting it, so a farther ancestor CAN
				// still surface under a different branch than the one that shadowed it. See
				// the task report for a known, narrow case where that leaves SharpMUSH
				// stricter than live Penn for a genuine wildcard pattern.
				var longName = longNameOf(attr);
				if (!seen.Add(longName))
				{
					continue;
				}

				// no_inherit on ANY level of the branch blocks the whole path when crossing
				// this parent boundary (Penn: AF_Private test in atr_get_with_parent,
				// attrib.c:1232-1252 -- checking the leaf's flags alone only covers the leaf).
				// Only pay for the full-path re-resolution when there's a branch to check at
				// all -- a flat (no backtick) attribute IS the whole path, so its own flags
				// suffice and the common case costs nothing extra.
				if (longName.Contains('`'))
				{
					var segments = longName.Split('`');
					var path = await resolvePath(parentObj.DBRef, segments).ToArrayAsync(cancellationToken);
					// Fail closed: if re-resolution doesn't return the full path (a race, or a
					// name-normalisation mismatch), deny rather than yield the attribute.
					if (path.Length != segments.Length || path.Any(isNoInherit))
					{
						continue;
					}
				}
				else if (isNoInherit(attr))
				{
					continue;
				}

				// The source object rides along with the match: it is what a downstream read
				// gate has to re-walk the ancestor path against.
				yield return (attr, parentObj.DBRef);
			}

			current = parentObj;
		}
	}

	/// <summary>
	/// Whether <paramref name="leaf"/>, found on <paramref name="source"/>, may be read through
	/// the target chain <paramref name="chain"/>.
	/// </summary>
	/// <param name="leaf">The attribute being read. Its <c>LongName</c> supplies the path.</param>
	/// <param name="source">The object the leaf was actually read from.</param>
	/// <param name="chain">
	/// Targets to walk, nearest first: the original object, then its <c>@parent</c> chain outward.
	/// Must contain <paramref name="source"/>, or the walk falls off the end and denies.
	/// </param>
	/// <param name="origin">
	/// The object the lookup was made against - <c>obj</c> in Penn's terms. The
	/// <c>no_inherit</c> escape applies only to targets other than this one
	/// (<c>target != obj</c>, <c>attrib.c:325</c>).
	/// </param>
	/// <param name="fetch">
	/// Resolves one prefix (given as its split segments) on a given target, returning null when no
	/// such attribute exists there. Callers are expected to serve already-materialised attributes
	/// from memory here rather than querying.
	/// </param>
	/// <param name="permits">
	/// The flag test, applied to one or more nodes at once. Must be all-must-pass, matching Penn's
	/// per-node <c>return 0</c>.
	/// </param>
	public static ValueTask<bool> CanReadAsync(
		SharpAttribute leaf,
		DBRef source,
		IReadOnlyList<DBRef> chain,
		DBRef origin,
		Func<DBRef, string[], ValueTask<SharpAttribute?>> fetch,
		Func<SharpAttribute[], ValueTask<bool>> permits)
		=> CanReadAsync(leaf, source, chain, origin, fetch, permits,
			static x => x.LongName, static x => x.IsNoInherit());

	/// <inheritdoc cref="CanReadAsync(SharpAttribute,DBRef,IReadOnlyList{DBRef},DBRef,Func{DBRef,string[],ValueTask{SharpAttribute}},Func{SharpAttribute[],ValueTask{bool}})"/>
	public static ValueTask<bool> CanReadAsync(
		LazySharpAttribute leaf,
		DBRef source,
		IReadOnlyList<DBRef> chain,
		DBRef origin,
		Func<DBRef, string[], ValueTask<LazySharpAttribute?>> fetch,
		Func<LazySharpAttribute[], ValueTask<bool>> permits)
		=> CanReadAsync(leaf, source, chain, origin, fetch, permits,
			static x => x.LongName, static x => x.IsNoInherit());

	private static async ValueTask<bool> CanReadAsync<T>(
		T leaf,
		DBRef source,
		IReadOnlyList<DBRef> chain,
		DBRef origin,
		Func<DBRef, string[], ValueTask<T?>> fetch,
		Func<T[], ValueTask<bool>> permits,
		Func<T, string> longNameOf,
		Func<T, bool> isNoInherit)
		where T : class
	{
		var segments = longNameOf(leaf).Split('`');

		foreach (var target in chain)
		{
			var isOrigin = target.SameObjectAs(origin);
			var resolved = new List<T>(segments.Length);
			var abandonTarget = false;

			// Penn's inner loop walks the strict prefixes root-first, resolving each against THIS
			// target (attrib.c:328-341). Resolution is incremental on purpose: a prefix that is
			// present and restrictive must deny even when a LATER prefix is missing, so the
			// prefixes cannot be gathered all-or-nothing before any of them is tested.
			for (var i = 1; i < segments.Length; i++)
			{
				var prefix = await fetch(target, segments[..i]);

				// `if (!atr || (target != obj && AF_Private(atr))) goto continue_target;`
				if (prefix is null || (!isOrigin && isNoInherit(prefix)))
				{
					abandonTarget = true;
					break;
				}

				resolved.Add(prefix);
			}

			// The flag test is `return 0` inline, never a continue (attrib.c:331-335). Batched
			// over the prefixes resolved BEFORE the walk was abandoned - exactly the set Penn had
			// already tested by the time it hit the missing/private one.
			if (resolved.Count > 0 && !await permits(resolved.ToArray()))
			{
				return false;
			}

			if (abandonTarget)
			{
				continue;
			}

			// `atr = find_atr_in_list(target, name); if (atr) return 1;`. The leaf lives on
			// `source` and on no nearer target - the caller's match set is deduplicated to the
			// nearest copy, so a nearer copy would have been the match instead of this one.
			if (target.SameObjectAs(source))
			{
				resolved.Add(leaf);
				return await permits(resolved.ToArray());
			}
		}

		// Ran off the end of the chain without ever reaching the leaf: attrib.c:356.
		return false;
	}
}
