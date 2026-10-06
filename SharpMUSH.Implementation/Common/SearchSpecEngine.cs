using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// Shared PennMUSH-compatible search-spec engine behind both <c>lsearch()</c>/<c>lsearchr()</c>
/// and the <c>@search</c> command: turns a flat list of <c>class=restriction</c> pairs (PennMUSH's
/// <c>fill_search_spec</c>) into an <see cref="ObjectSearchFilter"/> plus application-level criteria
/// (locks, evals, $-commands, ^-listens) that can't be pushed to the database, then executes it
/// (PennMUSH's <c>raw_search</c>). Each caller parses its own surface syntax into
/// <see cref="SearchPair"/>s — lsearch()'s comma-separated function args, @search's
/// space/comma/equals command syntax — and shares this engine from there on.
/// </summary>
public static class SearchSpecEngine
{
	public readonly record struct SearchPair(string ClassType, string Restriction);

	public readonly record struct SearchResult(IReadOnlyList<SharpObject> Matches, bool HadErrors);

	/// <summary>
	/// Runs the search, or names the notification that rejects its spec — a START or COUNT below one,
	/// as PennMUSH's <c>fill_search_spec</c> rejects them (<c>src/wiz.c:2388-2399</c>). The caller sends
	/// that notification to the searcher; <c>@search</c> then stops and the functions return <c>#-1</c>.
	/// </summary>
	public static async ValueTask<Result<SearchResult>> ExecuteResultAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		ILocateService locateService,
		IAttributeService attributeService,
		IBooleanExpressionParser booleanExpressionParser,
		IPermissionService permissionService,
		AnySharpObject executor,
		DBRef? ownerFilter,
		IReadOnlyList<SearchPair> pairs,
		bool useRegex)
	{
		var hadErrors = false;
		var types = new List<string>();
		var namePattern = (string?)null;
		int? minDbRef = null;
		int? maxDbRef = null;
		DBRef? zone = null;
		DBRef? parent = null;
		string? hasFlag = null;
		string? hasPower = null;
		var start = 1;
		int? count = null;

		var appLevelCriteria = new List<(string key, string value)>();

		foreach (var pair in pairs)
		{
			var classType = pair.ClassType.Trim().ToUpperInvariant();
			var restriction = pair.Restriction.Trim();

			if (classType.Equals("NONE", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			switch (classType)
			{
				case "TYPE":
					types.Add(restriction.ToUpperInvariant());
					break;
				case "NAME":
					namePattern = restriction;
					break;
				case "EXITS":
					types.Add("EXIT");
					namePattern = restriction;
					break;
				case "THINGS":
				case "OBJECTS":
					types.Add("THING");
					namePattern = restriction;
					break;
				case "ROOMS":
					types.Add("ROOM");
					namePattern = restriction;
					break;
				case "PLAYERS":
					types.Add("PLAYER");
					namePattern = restriction;
					break;
				case "MINDBREF":
				case "MINDB":
					if (int.TryParse(restriction, out var min)) minDbRef = min;
					break;
				case "MAXDBREF":
				case "MAXDB":
					if (int.TryParse(restriction, out var max)) maxDbRef = max;
					break;
				// START is 1-based: the first match is result 1 (init_search_spec, src/wiz.c:2270).
				case "START":
					start = LeadingInteger(restriction);
					if (start < 1) return new Error<string>(nameof(ErrorMessages.Notifications.SearchInvalidStart));
					break;
				case "COUNT":
					count = LeadingInteger(restriction);
					if (count < 1) return new Error<string>(nameof(ErrorMessages.Notifications.SearchInvalidCount));
					break;
				case "ZONE":
					var maybeZone = await locateService.Locate(parser, executor, executor, restriction, LocateFlags.All);
					if (maybeZone is AnySharpObject zoneObject) zone = zoneObject.Object().DBRef;
					break;
				case "PARENT":
					var maybeParent = await locateService.Locate(parser, executor, executor, restriction, LocateFlags.All);
					if (maybeParent is AnySharpObject parentObject) parent = parentObject.Object().DBRef;
					break;
				case "FLAG":
				case "FLAGS":
					hasFlag = restriction;
					break;
				case "LFLAGS":
					// LFLAGS uses space-separated flag names instead of single characters
					hasFlag = restriction;
					break;
				case "POWER":
				case "POWERS":
					hasPower = restriction;
					break;
				default:
					// Lock evaluation, COMMAND, LISTEN, and other criteria must happen in application code
					appLevelCriteria.Add((classType, restriction));
					break;
			}
		}

		// Pre-compile lock strings and eval expressions for efficiency (compile once, evaluate many times)
		// This avoids re-compiling the same lock string or expression for every object in the result set
		var compiledLocks = new List<Func<AnySharpObject, AnySharpObject, ValueTask<bool>>>();
		var compiledEvals = new List<(string evalExpression, string? typeFilter)>();
		var hasEvaluatingLock = false;

		foreach (var (key, value) in appLevelCriteria)
		{
			switch (key)
			{
				case "LOCK" or "ELOCK":
					// Optimize #TRUE - no need to compile
					if (value is "#TRUE" or "")
					{
						compiledLocks.Add((_, _) => ValueTask.FromResult(true));
					}
					else
					{
						compiledLocks.Add(booleanExpressionParser.Compile(value));
						hasEvaluatingLock = true;
					}
					break;

				case "EVAL":
					compiledEvals.Add((value, null));
					break;
				case "EPLAYER":
					compiledEvals.Add((value, "PLAYER"));
					break;
				case "EROOM":
					compiledEvals.Add((value, "ROOM"));
					break;
				case "EEXIT":
					compiledEvals.Add((value, "EXIT"));
					break;
				case "ETHING" or "EOBJECT":
					compiledEvals.Add((value, "THING"));
					break;

				case "LISTEN":
				case "COMMAND":
					// These require attribute pattern matching - store for app-level evaluation
					break;
			}
		}

		var listenPattern = appLevelCriteria.FirstOrDefault(x => x.key == "LISTEN").value;
		var commandPattern = appLevelCriteria.FirstOrDefault(x => x.key == "COMMAND").value;
		var hasListenCriteria = !string.IsNullOrEmpty(listenPattern);
		var hasCommandCriteria = !string.IsNullOrEmpty(commandPattern);

		// WIZARD, ROYALTY and the built-in powers are held through roles, which no index lists, so they are
		// tested on each object rather than in the database filter.
		var heldFlag = hasFlag is not null && RoleFlags.Find(hasFlag) is not null ? hasFlag : null;
		var heldPower = hasPower is not null && GamePowers.Find(hasPower) is not null ? hasPower : null;
		if (heldFlag is not null) hasFlag = null;
		if (heldPower is not null) hasPower = null;

		var hasAppLevelCriteria = compiledLocks.Count > 0 || compiledEvals.Count > 0 || hasListenCriteria || hasCommandCriteria
			|| heldFlag is not null || heldPower is not null;

		// PennMUSH's raw_search: a non-wizard (non-See_All/Search_All) searcher only sees objects
		// they could @examine, unless they're searching only their own objects. The one exception is
		// a named owner who is a Zone Master Player (has the SHARED flag) whose Zone lock the searcher
		// passes — that owner's objects count as visible even to mortals.
		var visOnly = await IsVisibilityRestrictedAsync(mediator, permissionService, executor, ownerFilter);
		var needsPerObjectEvaluation = hasAppLevelCriteria || visOnly;

		// IMPORTANT: Only apply START/COUNT at database level if there are NO app-level criteria
		// (including visibility filtering). Otherwise we must apply START/COUNT after filtering in
		// application code, since the database can't know in advance which rows will be excluded.
		var filter = new ObjectSearchFilter
		{
			Types = types.Count > 0 ? [.. types] : null,
			NamePattern = namePattern,
			UseRegex = useRegex,
			MinDbRef = minDbRef,
			MaxDbRef = maxDbRef,
			Zone = zone,
			Parent = parent,
			HasFlag = hasFlag,
			HasPower = hasPower,
			Owner = ownerFilter,
			Skip = needsPerObjectEvaluation ? null : start - 1,  // Only skip at DB level if no app-level filtering
			Limit = needsPerObjectEvaluation ? null : count  // Only limit at DB level if no app-level filtering
		};

		var filteredObjects = mediator.CreateStream(new GetFilteredObjectsQuery(filter));

		if (!needsPerObjectEvaluation)
		{
			return new SearchResult(await filteredObjects.ToListAsync(), false);
		}

		// Resolve each row through GetObjectNodeQuery rather than hydrating it from the scan: locks and
		// permissions must be judged against the canonical cached object, and the full objid check drops
		// a row recycled since the scan. A row destroyed or recycled in between no longer resolves and is
		// skipped, as @find skips it.
		//
		// PennMUSH's raw_search (src/wiz.c:2612-2618) keeps evaluating after the page is full: a match
		// past START+COUNT is counted and skipped with `continue`, never `break`. While an EVAL, a
		// lock that is not #TRUE, or the visibility check is present, every candidate is evaluated here
		// too — each can run softcode (visibility is Can_Examine, which evaluates Control, Zone and
		// Examine locks; src/wiz.c:2542, hdrs/mushdb.h:80), and its side effects and its HadErrors
		// belong to the whole search, not to the page. Without one, what remains ($-/^-patterns,
		// @listen) reads and never writes, so once the page is full nothing a later candidate does can
		// be observed, and the scan stops there. Either way only the requested window is stored.
		var window = new ResultWindow(start, count);
		var pageEndsScan = compiledEvals.Count == 0 && !hasEvaluatingLock && !visOnly;
		await foreach (var obj in filteredObjects)
		{
			if (pageEndsScan && window.IsFull)
			{
				break;
			}

			if (await mediator.Send(new GetObjectNodeQuery(obj.DBRef)) is not AnySharpObject typedObj)
			{
				continue;
			}

			bool matches = true;

			if (visOnly && !await permissionService.CanExamine(executor, typedObj))
			{
				matches = false;
			}

			if (matches && heldFlag is not null && !await typedObj.HasFlag(heldFlag))
			{
				matches = false;
			}

			if (matches && heldPower is not null && !await typedObj.HasPower(heldPower))
			{
				matches = false;
			}

			foreach (var compiledLock in compiledLocks)
			{
				if (!matches)
				{
					break;
				}

				if (!await compiledLock(typedObj, executor))
				{
					matches = false;
					break;
				}
			}

			if (matches)
			{
				foreach (var (evalExpression, typeFilter) in compiledEvals)
				{
					if (typeFilter != null && !typedObj.Object().Type.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
					{
						matches = false;
						break;
					}

					// Replace ## with the object's dbref number in the expression
					var objectDbRefNum = typedObj.Object().DBRef.Number.ToString();
					var expression = evalExpression.Replace("##", objectDbRefNum);

					var evalResult = await parser.FunctionParse(MarkupText.Plain(expression));
					hadErrors |= evalResult?.HadErrors == true;
					if (evalResult == null || !evalResult.Message.Truthy(parser))
					{
						matches = false;
						break;
					}
				}
			}

			// raw_search (src/wiz.c:2568-2586) tests both with atr_comm_match, locks unchecked: $-commands
			// through the @parent chain, ^-patterns through it only for a LISTEN_PARENT object, never the
			// type ancestor. COMMAND is tested first, as Penn tests it.
			if (matches && hasCommandCriteria)
			{
				var commands = await mediator.Send(new GetCommandAttributesQuery(typedObj));
				matches = commands.Any(command => SoftcodeRegex.Match(command.CompiledRegex, commandPattern!) is { Success: true });
			}

			if (matches && hasListenCriteria)
			{
				// @listen itself is atr_get_noparent (src/wiz.c:2575).
				var listen = await attributeService.GetAttributeAsync(typedObj, typedObj, "LISTEN",
					IAttributeService.AttributeMode.Read, parent: false);
				matches = (listen is SharpAttribute[] { Length: > 0 } chain && PatternAccepts(chain[^1], chain[^1].Value.ToPlainText(), listenPattern!))
					|| (await parser.ServiceProvider.GetRequiredService<IListenPatternMatcher>().MatchListenPatternsAsync(
						typedObj, listenPattern!, executor, checkParents: await typedObj.HasFlag("LISTEN_PARENT"))).Length > 0;
			}

			if (matches)
			{
				window.Offer(typedObj.Object());
			}
		}

		return new SearchResult(window.Results, hadErrors);
	}

	/// <summary>
	/// PennMUSH's <c>parse_integer</c> (<c>strtol</c>, <c>src/parse.c:674</c>): leading whitespace,
	/// an optional sign and the digits that follow; anything else ends the number, and a string with
	/// no digits is 0. Out-of-range values clamp to the <see cref="int"/> bounds.
	/// </summary>
	internal static int LeadingInteger(string text)
	{
		var span = text.AsSpan().TrimStart();
		var negative = false;
		if (span.Length > 0 && span[0] is '+' or '-')
		{
			negative = span[0] == '-';
			span = span[1..];
		}

		long value = 0;
		foreach (var c in span)
		{
			if (!char.IsAsciiDigit(c)) break;
			value = Math.Min(value * 10 + (c - '0'), (long)int.MaxValue + 1);
		}

		return (int)Math.Clamp(negative ? -value : value, int.MinValue, int.MaxValue);
	}

	/// <summary>
	/// START/COUNT applied to the matches as they arrive, after every runtime restriction, as
	/// PennMUSH's <c>raw_search</c> applies them (<c>src/wiz.c:2612-2618</c>): matches before the
	/// 1-based <paramref name="start"/>th are counted and dropped, the next <paramref name="count"/> kept
	/// (all of them when there is no COUNT), the rest dropped. Both are at least one; the spec parse
	/// rejects anything lower.
	/// </summary>
	internal sealed class ResultWindow(int start, int? count)
	{
		/// <summary>The most a window reserves up front; a larger page grows as its matches arrive.</summary>
		private const int InitialCapacityCeiling = 64;

		private readonly int _skip = start - 1;
		private readonly int _take = count ?? int.MaxValue;
		private int _seen;

		public List<SharpObject> Results { get; } = new(Math.Min(count ?? int.MaxValue, InitialCapacityCeiling));

		/// <summary>Whether the page holds every match it asked for, so no later match can be kept.</summary>
		public bool IsFull => Results.Count >= _take;

		public void Offer(SharpObject match)
		{
			var position = _seen++;
			if (position >= _skip && Results.Count < _take)
			{
				Results.Add(match);
			}
		}
	}

	/// <summary>
	/// <paramref name="pattern"/> compiled the way the attribute's flags say — a regexp under
	/// <c>REGEXP</c>, otherwise the general MUSH wildcard (<see cref="SoftcodeRegex.Wildcard"/>), and
	/// case-insensitive unless <c>CASE</c> is set — then matched against <paramref name="text"/>.
	/// A pattern that does not compile accepts nothing.
	/// </summary>
	private static bool PatternAccepts(SharpAttribute attribute, string pattern, string text)
	{
		var caseSensitive = attribute.IsCase();
		try
		{
			var regex = attribute.IsRegexp()
				? SoftcodeRegex.Create(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
				: SoftcodeRegex.Wildcard(pattern, caseSensitive: caseSensitive);
			return SoftcodeRegex.IsMatch(regex, text);
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	/// <summary>
	/// Ports PennMUSH's <c>raw_search</c> visibility gate: a non-wizard (non-See_All/Search_All)
	/// searcher only sees objects they could <c>@examine</c>, unless they're restricting the search to
	/// their own objects. The exception is a named owner who is a Zone Master Player (has the
	/// <c>SHARED</c> flag) whose Zone lock the searcher passes — that owner's objects stay fully
	/// visible even to mortals, matching PennMUSH's <c>ZMaster</c>/<c>Zone_Lock</c> carve-out.
	/// </summary>
	private static async ValueTask<bool> IsVisibilityRestrictedAsync(
		IMediator mediator,
		IPermissionService permissionService,
		AnySharpObject executor,
		DBRef? ownerFilter)
	{
		if (await executor.IsSee_All() || await executor.HasPower("Search"))
		{
			return false;
		}

		if (ownerFilter is { } owner && owner == executor.Object().DBRef)
		{
			// Searching only their own objects — nothing to restrict.
			return false;
		}

		if (ownerFilter is { } namedOwner)
		{
			if (await mediator.Send(new GetObjectNodeQuery(namedOwner)) is AnySharpObject ownerObject)
			{
				if (await ownerObject.HasFlag("SHARED") && await permissionService.PassesLock(executor, ownerObject, LockType.Zone))
				{
					return false;
				}
			}
		}

		return true;
	}
}
