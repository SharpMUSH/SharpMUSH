using Mediator;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

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

	public static async ValueTask<IReadOnlyList<SharpObject>> ExecuteAsync(
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
		var types = new List<string>();
		var namePattern = (string?)null;
		int? minDbRef = null;
		int? maxDbRef = null;
		DBRef? zone = null;
		DBRef? parent = null;
		string? hasFlag = null;
		string? hasPower = null;
		int? start = null;
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
				case "START":
					if (int.TryParse(restriction, out var startVal)) start = startVal;
					break;
				case "COUNT":
					if (int.TryParse(restriction, out var countVal)) count = countVal;
					break;
				case "ZONE":
					var maybeZone = await locateService.Locate(parser, executor, executor, restriction, LocateFlags.All);
					if (maybeZone.IsValid()) zone = maybeZone.AsAnyObject.Object().DBRef;
					break;
				case "PARENT":
					var maybeParent = await locateService.Locate(parser, executor, executor, restriction, LocateFlags.All);
					if (maybeParent.IsValid()) parent = maybeParent.AsAnyObject.Object().DBRef;
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

		var hasAppLevelCriteria = compiledLocks.Count > 0 || compiledEvals.Count > 0 || hasListenCriteria || hasCommandCriteria;

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
			Skip = needsPerObjectEvaluation ? null : start,  // Only skip at DB level if no app-level filtering
			Limit = needsPerObjectEvaluation ? null : count  // Only limit at DB level if no app-level filtering
		};

		var filteredObjects = mediator.CreateStream(new GetFilteredObjectsQuery(filter));

		if (!needsPerObjectEvaluation)
		{
			return await filteredObjects.ToListAsync();
		}

		// Optimize: Convert to AnySharpObject once per object and evaluate all criteria
		var finalResults = new List<SharpObject>();
		await foreach (var obj in filteredObjects)
		{
			var typedObj = await CreateAnySharpObjectFromSharpObject(mediator, obj);
			bool matches = true;

			if (visOnly && !await permissionService.CanExamine(executor, typedObj))
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
					if (evalResult == null || !evalResult.Message.Truthy(parser))
					{
						matches = false;
						break;
					}
				}
			}

			if (matches && hasListenCriteria)
			{
				var attributesResult = await attributeService.GetVisibleAttributesAsync(executor, typedObj);
				if (!attributesResult.IsError)
				{
					var hasMatchingListen = false;

					foreach (var attr in attributesResult.AsAttributes.Where(a => a.Name.Equals("LISTEN", StringComparison.OrdinalIgnoreCase) ||
																										 a.Name.StartsWith("LISTEN`", StringComparison.OrdinalIgnoreCase)))
					{
						var attrValue = attr.Value?.ToPlainText() ?? "";
						if (IsWildcardMatch(attrValue, listenPattern!))
						{
							hasMatchingListen = true;
							break;
						}
					}

					if (!hasMatchingListen)
					{
						matches = false;
					}
				}
				else
				{
					matches = false;
				}
			}

			if (matches && hasCommandCriteria)
			{
				var attributesResult = await attributeService.GetVisibleAttributesAsync(executor, typedObj);
				if (!attributesResult.IsError)
				{
					var hasMatchingCommand = false;

					foreach (var attr in attributesResult.AsAttributes)
					{
						var attrValue = attr.Value?.ToPlainText() ?? "";
						// $-commands are in format: $command-pattern:action
						var dollarIndex = attrValue.IndexOf('$');
						if (dollarIndex >= 0)
						{
							var colonIndex = attrValue.IndexOf(':', dollarIndex);
							if (colonIndex > dollarIndex)
							{
								var commandPart = attrValue.AsSpan(dollarIndex + 1, colonIndex - dollarIndex - 1).ToString();
								if (IsWildcardMatch(commandPart, commandPattern))
								{
									hasMatchingCommand = true;
									break;
								}
							}
						}
					}

					if (!hasMatchingCommand)
					{
						matches = false;
					}
				}
				else
				{
					matches = false;
				}
			}

			if (matches)
			{
				finalResults.Add(typedObj.Object());
			}
		}

		// This ensures pagination happens AFTER all runtime filters are applied
		if (start.HasValue || count.HasValue)
		{
			var skipCount = start ?? 0;
			var takeCount = count ?? int.MaxValue;
			finalResults = [.. finalResults.Skip(skipCount).Take(takeCount)];
		}

		return finalResults;
	}

	/// <summary>
	/// Simple wildcard pattern matching for LISTEN and COMMAND searches.
	/// Supports * as a wildcard that matches any sequence of characters.
	/// </summary>
	private static bool IsWildcardMatch(string value, string pattern)
	{
		var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
		return System.Text.RegularExpressions.Regex.IsMatch(value, regexPattern,
			System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
			var ownerNode = await mediator.Send(new GetObjectNodeQuery(namedOwner));
			if (!ownerNode.IsNone)
			{
				var ownerObject = ownerNode.Known;
				if (await ownerObject.HasFlag("SHARED") && await permissionService.PassesLock(executor, ownerObject, LockType.Zone))
				{
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>
	/// Creates an AnySharpObject from a SharpObject based on its Type property.
	/// This is needed when we have a raw SharpObject from the database but need to work with the discriminated union.
	/// </summary>
	private static async Task<AnySharpObject> CreateAnySharpObjectFromSharpObject(IMediator mediator, SharpObject obj)
	{
		var dbref = new DBRef(obj.Key, obj.CreationTime);
		var result = await mediator.Send(new GetObjectNodeQuery(dbref));

		if (result.IsNone)
		{
			// This shouldn't happen in normal operation, but handle it gracefully
			throw new InvalidOperationException($"Object {dbref} not found when evaluating lock criteria");
		}

		return result.Known;
	}
}
