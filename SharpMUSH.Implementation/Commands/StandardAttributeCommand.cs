using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// PennMUSH's <c>command_isattr</c>: <c>@&lt;attribute&gt; object=value</c> for a standard attribute
/// that is not itself a command, so <c>@describe me=...</c> sets DESCRIBE. Reached only when no
/// built-in command matched the name.
/// </summary>
internal sealed class StandardAttributeCommand(EvaluationServices services)
{
	/// <summary>
	/// The entry an <c>@attrname</c> command means when no entry has that exact name: an exact match
	/// (case-insensitive) first, then the shortest <c>prefixmatch</c> entry the name begins, ties broken
	/// alphabetically. One pass over the entries, keeping the best so far.
	/// </summary>
	private async ValueTask<SharpAttributeEntry?> BestStandardAttributeMatchAsync(string name)
	{
		SharpAttributeEntry? best = null;
		var bestExact = false;
		await foreach (var entry in services.Mediator.CreateStream(new GetAllAttributeEntriesQuery()))
		{
			var exact = entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
			if (!exact && !(entry.DefaultFlags.Contains("prefixmatch", StringComparer.OrdinalIgnoreCase)
					&& entry.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			// Strictly better only, so the first of equals is kept, as a stable sort would keep it.
			if (best is null
					|| (exact && !bestExact)
					|| (exact == bestExact && (entry.Name.Length < best.Name.Length
						|| (entry.Name.Length == best.Name.Length
							&& StringComparer.OrdinalIgnoreCase.Compare(entry.Name, best.Name) < 0))))
			{
				best = entry;
				bestExact = exact;
			}
		}

		return best;
	}

	/// <summary>
	/// Handles standard attribute commands like @describe, @success, @failure, etc.
	/// These commands set the named standard attribute on the target object.
	/// Supports prefix matching when the attribute has the "prefixmatch" flag.
	/// </summary>
	/// <param name="prs">The parser instance</param>
	/// <param name="src">The source MString being parsed</param>
	/// <param name="context">The command context</param>
	/// <param name="rootCommand">The root command (e.g., @DESCRIBE, @DESC)</param>
	/// <returns>Some(CallState) if handled, None if not a standard attribute command</returns>
	public async ValueTask<Option<CallState>> TryRunAsync(
		IMUSHCodeParser prs, MString src, ICommandContext context, string rootCommand)
	{
		if (!rootCommand.StartsWith('@'))
		{
			return new None();
		}

		var potentialAttrName = rootCommand[1..].ToUpperInvariant();
		if (string.IsNullOrEmpty(potentialAttrName))
		{
			return new None();
		}

		// Find matching standard attribute entry: exact match first, then shortest prefix match. The exact
		// name is one keyed lookup; only when it misses are the entries walked, once, for the best prefix.
		var matchedEntry = await services.Mediator.Send(new GetAttributeEntryQuery(potentialAttrName)) is { } exact
			&& exact.Name.Equals(potentialAttrName, StringComparison.OrdinalIgnoreCase)
				? exact
				: await BestStandardAttributeMatchAsync(potentialAttrName);

		if (matchedEntry == null)
		{
			return new None();
		}

		var evalString = context.evaluationString();
		if (evalString == null || evalString.IsEmpty)
		{
			var handle = prs.CurrentState.Handle;
			if (handle.HasValue)
			{
				await services.NotifyService.NotifyLocalized(handle.Value, nameof(ErrorMessages.Notifications.UsageAtCommandFormat),
					matchedEntry.Name.ToLower());
			}

			return CallState.Empty;
		}

		var fullText = src.Substring(evalString.Start.StartIndex, evalString.Stop.StopIndex - evalString.Start.StartIndex + 1);

		// The command was matched on the line's trimmed text; an indented line must be split the same way
		// or its first space is the indent and the command token lands in the object name.
		var commandStart = CommandArgumentSplitter.SkipSpaces(fullText, 0);
		if (commandStart > 0)
		{
			fullText = fullText.Substring(commandStart, fullText.Length - commandStart);
		}

		// The command format is: @attrname object=value
		var spaceIndex = fullText.IndexOf(" ");

		// command_isattr makes this ATTRIB_SET/<attribute>. The value is stored as written here, so
		// that is also what %u shows; evaluating it for %u alone would run it when the command does not.
		prs.CurrentState.CommandText?.EvaluatedFrom(() => spaceIndex == -1
			? MarkupText.Plain($"ATTRIB_SET/{matchedEntry.Name}")
			: MarkupText.Concat(MarkupText.Plain($"ATTRIB_SET/{matchedEntry.Name} "),
				fullText.Substring(spaceIndex + 1, fullText.Length - spaceIndex - 1)));

		if (spaceIndex == -1)
		{
			var handle = prs.CurrentState.Handle;
			if (handle.HasValue)
			{
				await services.NotifyService.NotifyLocalized(handle.Value, nameof(ErrorMessages.Notifications.UsageAtCommandFormat),
					matchedEntry.Name.ToLower());
			}

			return CallState.Empty;
		}

		var argsText = fullText.Substring(spaceIndex + 1, fullText.Length - spaceIndex - 1);
		var argsPlainText = argsText.ToPlainText();

		var equalsIndex = argsPlainText.IndexOf('=');
		if (equalsIndex < 0)
		{
			// No = sign - clear/unset the attribute
			var objectToClear = argsPlainText.Trim();

			var clearExecutor = await prs.CurrentState.KnownExecutorObject(services.Mediator);

			var clearLocateResult = await services.LocateService.LocateAndNotifyIfInvalid(
				prs, clearExecutor, clearExecutor, objectToClear, LocateFlags.All);

			if (clearLocateResult is not AnySharpObject clearTargetObject)
			{
				return CallState.Empty;
			}

			var clearResult = await services.AttributeService.ClearAttributeAsync(
				clearExecutor, clearTargetObject, matchedEntry.Name,
				IAttributeService.AttributePatternMode.Exact);

			var clearHandle = prs.CurrentState.Handle;

			// A player's alias list was reported by the write itself (PlayerAliases).
			if (!clearHandle.HasValue || PlayerAliases.Applies(clearTargetObject, matchedEntry.Name))
			{
				return CallState.Empty;
			}

			if (clearResult is not Error<string> clearError)
			{
				// do_set_atr's QUIET gate (src/attrib.c:2446).
				if (await AttributeWriteReport.IsSuppressedAsync(services.AttributeService, clearExecutor, clearTargetObject,
							matchedEntry.Name))
				{
					return CallState.Empty;
				}

				await services.NotifyService.NotifyLocalized(clearHandle.Value, nameof(ErrorMessages.Notifications.AttributeCleared),
					clearExecutor, clearTargetObject.Object().Name, matchedEntry.Name);
			}
			else
			{
				await services.NotifyService.NotifyLocalized(clearHandle.Value, nameof(ErrorMessages.Notifications.ErrorDetailFormat),
					clearExecutor, clearError.Value);
			}

			return CallState.Empty;
		}

		var objectPart = argsPlainText[..equalsIndex].Trim();
		var valuePart = equalsIndex + 1 < argsPlainText.Length
			? argsPlainText[(equalsIndex + 1)..]
			: string.Empty;

		// Preserve markup: the equals sign is at equalsIndex in argsText, so value starts at equalsIndex + 1
		var valueLength = argsText.Length - equalsIndex - 1;
		var valueMString = valueLength > 0
			? argsText.Substring(equalsIndex + 1, valueLength)
			: MarkupText.Plain(valuePart);

		var executor = await prs.CurrentState.KnownExecutorObject(services.Mediator);

		var locateResult = await services.LocateService.LocateAndNotifyIfInvalid(
			prs, executor, executor, objectPart, LocateFlags.All);

		if (locateResult is not AnySharpObject targetObject)
		{
			return CallState.Empty;
		}

		var setResult = await services.AttributeService.SetAttributeAsync(
			executor, targetObject, matchedEntry.Name, valueMString);

		var handle2 = prs.CurrentState.Handle;

		if (!handle2.HasValue || PlayerAliases.Applies(targetObject, matchedEntry.Name))
		{
			return CallState.Empty;
		}

		if (setResult is not Error<string> error)
		{
			// do_set_atr's QUIET gate (src/attrib.c:2446).
			if (await AttributeWriteReport.IsSuppressedAsync(services.AttributeService, executor, targetObject,
						matchedEntry.Name))
			{
				return CallState.Empty;
			}

			await services.NotifyService.NotifyLocalized(handle2.Value, nameof(ErrorMessages.Notifications.AttributeSet), executor,
				targetObject.Object().Name, matchedEntry.Name);
		}
		else
		{
			await services.NotifyService.NotifyLocalized(handle2.Value, nameof(ErrorMessages.Notifications.ErrorDetailFormat),
				executor, error.Value);
		}

		return CallState.Empty;
	}
}
