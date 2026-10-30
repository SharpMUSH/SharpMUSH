using Mediator;
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
	private readonly StandardAttributeWriter _writer = new(services.Mediator, services.LocateService,
		services.AttributeService, services.NotifyService);

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

		var fullText = CommandDispatcher.CommandSlice(src, context);

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
			return await _writer.UsageAsync(prs, matchedEntry.Name);
		}

		var argsText = fullText.Substring(spaceIndex + 1, fullText.Length - spaceIndex - 1);
		return await _writer.WriteAsync(prs, argsText, matchedEntry.Name);
	}
}

/// <summary>
/// The write behind every <c>@&lt;attribute&gt; object[=value]</c> command: PennMUSH's <c>command_atrset</c>
/// as <c>command_isattr</c> reaches it. The text after the command name is taken as written, never
/// evaluated; with no <c>=</c> the attribute is cleared. The executor is the permission subject, and the
/// confirmation passes <c>do_set_atr</c>'s QUIET gate. <c>@gender</c> uses it too, so it cannot set an
/// attribute any differently than <c>@sex</c> does.
/// </summary>
internal sealed class StandardAttributeWriter(
	IMediator mediator,
	ILocateService locateService,
	IAttributeService attributeService,
	INotifyService notifyService)
{
	/// <summary>Tells the enactor how the command named for <paramref name="attributeName"/> is used.</summary>
	public async ValueTask<Option<CallState>> UsageAsync(IMUSHCodeParser prs, string attributeName)
	{
		var handle = prs.CurrentState.Handle;
		if (handle.HasValue)
		{
			await notifyService.NotifyLocalized(handle.Value, nameof(ErrorMessages.Notifications.UsageAtCommandFormat),
				attributeName.ToLower());
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Sets <paramref name="attributeName"/> on the object <paramref name="argsText"/> names, to the text after
	/// its <c>=</c>, or clears it when there is no <c>=</c>.
	/// </summary>
	/// <param name="prs">The command's parser.</param>
	/// <param name="argsText">The command's arguments as typed: <c>object=value</c> or <c>object</c>.</param>
	/// <param name="attributeName">The attribute to write.</param>
	public async ValueTask<Option<CallState>> WriteAsync(IMUSHCodeParser prs, MString argsText, string attributeName)
	{
		var argsPlainText = argsText.ToPlainText();

		var equalsIndex = argsPlainText.IndexOf('=');
		if (equalsIndex < 0)
		{
			// No = sign - clear/unset the attribute
			var objectToClear = argsPlainText.Trim();

			var clearExecutor = await prs.CurrentState.KnownExecutorObject(mediator);

			var clearLocateResult = await locateService.LocateAndNotifyIfInvalid(
				prs, clearExecutor, clearExecutor, objectToClear, LocateFlags.All);

			if (clearLocateResult is not AnySharpObject clearTargetObject)
			{
				return CallState.Empty;
			}

			var clearResult = await attributeService.ClearAttributeAsync(
				clearExecutor, clearTargetObject, attributeName,
				IAttributeService.AttributePatternMode.Exact);

			var clearHandle = prs.CurrentState.Handle;

			// A player's alias list was reported by the write itself (PlayerAliases).
			if (!clearHandle.HasValue || PlayerAliases.Applies(clearTargetObject, attributeName))
			{
				return CallState.Empty;
			}

			if (clearResult is not Error<string> clearError)
			{
				// do_set_atr's QUIET gate (src/attrib.c:2446).
				if (await AttributeWriteReport.IsSuppressedAsync(attributeService, clearExecutor, clearTargetObject,
							attributeName))
				{
					return CallState.Empty;
				}

				await notifyService.NotifyLocalized(clearHandle.Value, nameof(ErrorMessages.Notifications.AttributeCleared),
					clearExecutor, clearTargetObject.Object().Name, attributeName);
			}
			else
			{
				await notifyService.NotifyLocalized(clearHandle.Value, nameof(ErrorMessages.Notifications.ErrorDetailFormat),
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

		var executor = await prs.CurrentState.KnownExecutorObject(mediator);

		var locateResult = await locateService.LocateAndNotifyIfInvalid(
			prs, executor, executor, objectPart, LocateFlags.All);

		if (locateResult is not AnySharpObject targetObject)
		{
			return CallState.Empty;
		}

		var setResult = await attributeService.SetAttributeForReportAsync(
			executor, targetObject, attributeName, valueMString);

		var handle2 = prs.CurrentState.Handle;

		if (!handle2.HasValue || PlayerAliases.Applies(targetObject, attributeName))
		{
			return CallState.Empty;
		}

		switch (setResult)
		{
			// do_set_atr's QUIET gate (src/attrib.c:2446).
			case WrittenAttribute written:
				if (!await AttributeWriteReport.IsSuppressedAsync(executor, targetObject, written))
				{
					await notifyService.NotifyLocalized(handle2.Value, nameof(ErrorMessages.Notifications.AttributeSet),
						executor, targetObject.Object().Name, attributeName);
				}

				break;
			case Error<string> error:
				await notifyService.NotifyLocalized(handle2.Value, nameof(ErrorMessages.Notifications.ErrorDetailFormat),
					executor, error.Value);
				break;
		}

		return CallState.Empty;
	}
}
