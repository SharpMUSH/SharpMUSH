using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

public static class SetHelpers
{
	/// <summary>
	/// PennMUSH's <c>do_set</c> (<c>src/set.c:611</c>), which is the whole of both spellings:
	/// <c>cmd_set</c> is <c>do_set(executor, arg_left, arg_right)</c> (<c>src/cmds.c:1410</c>) and
	/// <c>fun_set</c> is <c>do_set(executor, args[0], args[1])</c> (<c>src/fundb.c:2251</c>) — which
	/// is what <c>help set()</c> means by "This function is equivalent to @set". Three things follow
	/// from there being one routine, and each of them was written twice here and differed:
	/// <list type="bullet">
	/// <item>the flag argument is split on spaces and applied one token at a time, function or not;</item>
	/// <item><c>set_flag</c> reports what it did to <c>player</c> either way — there is no quiet
	/// variant on the function side (<c>src/flags.c:1855,1914</c>);</item>
	/// <item><c>player</c> is the executor, and it is both the search origin
	/// (<c>match_controlled(player, name)</c>, <c>src/set.c:635</c>) and the permission subject. A
	/// forced object that searches from its ENACTOR is looking somewhere it may not even be allowed
	/// to look.</item>
	/// </list>
	/// </summary>
	/// <param name="executor">Penn's <c>player</c>: the search origin and the permission subject.</param>
	/// <param name="objectAndOptionalAttribute">Penn's <c>xname</c> — the left of the <c>=</c>.</param>
	/// <param name="flagOrAttributeValue">Penn's <c>flag</c> — the right of the <c>=</c>.</param>
	/// <returns>Empty on success, otherwise the failure's <c>#-1</c> string.</returns>
	public static async ValueTask<CallState> DoSet(
		IMUSHCodeParser parser,
		ILocateService locateService,
		IAttributeService attributeService,
		IManipulateSharpObjectService manipulateSharpObjectService,
		INotifyService notifyService,
		AnySharpObject executor,
		MString objectAndOptionalAttribute,
		MString flagOrAttributeValue)
	{
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objectAndOptionalAttribute.ToPlainText());

		if (!split.IsT0)
		{
			return new CallState(ErrorMessages.Returns.BadArgumentFormatToSet);
		}

		var (name, maybeAttribute) = split.AsT0;

		var locate = await locateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor, executor, name, LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var target = locate.AsSharpObject;

		// PennMUSH gates every confirmation this routine emits on AreQuiet(player, thing)
		// (hdrs/dbdefs.h:198): the player is QUIET, or the thing is QUIET and the player owns it.
		// set_flag (src/flags.c:1855,1914) and do_set_atr (src/attrib.c:2446) both test it.
		var areQuiet = await target.Object().AreQuietAsync(executor);

		if (!string.IsNullOrEmpty(maybeAttribute))
		{
			return await SetAttributeFlags(target, maybeAttribute);
		}

		var colon = flagOrAttributeValue.IndexOf(":");

		return colon > -1
			? await SetAttributeValue(target, colon)
			: await SetFlags(target);

		// do_attrib_flags: every token is applied as ONE batch, because Penn checks permission once
		// for the whole flag argument rather than once per flag, so the result does not depend on
		// which token is processed first.
		async ValueTask<CallState> SetAttributeFlags(AnySharpObject found, string attribute)
		{
			var flagTokens = MushText.SplitList(MarkupText.Space, flagOrAttributeValue)
				.Select(x => x.ToPlainText())
				.ToList();

			var result = await attributeService.SetAttributeFlagsAsync(executor, found, attribute, flagTokens);

			if (result.IsT1)
			{
				await notifyService.Notify(executor, result.AsT1.Value, executor);
			}

			return new CallState(result.Match(_ => string.Empty, failure => failure.Value));
		}

		// do_set_atr(thing, flag, p, player, 1) — the trailing 1 is what makes it report the write
		// (`flags & 0x01`, src/attrib.c:2445).
		async ValueTask<CallState> SetAttributeValue(AnySharpObject found, int colonIndex)
		{
			var attribute = flagOrAttributeValue.Substring(0, colonIndex);
			var content = flagOrAttributeValue.Substring(colonIndex + 1, flagOrAttributeValue.Length - (colonIndex + 1));

			var result = await attributeService.SetAttributeAsync(executor, found, attribute.ToPlainText(), content);

			if (result.IsT0)
			{
				// do_set_atr (src/attrib.c:2446-2451) has a second gate the flag path does not: the
				// written attribute's own AF_Quiet suppresses the line as well.
				var written = await attributeService.GetAttributeAsync(executor, found, attribute.ToPlainText(),
					IAttributeService.AttributeMode.Read, false);
				var attributeIsQuiet = written.IsAttribute && written.AsAttribute.Last().IsQuiet();

				if (!areQuiet && !attributeIsQuiet)
				{
					await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeSet), executor,
						found.Object().Name, attribute.ToPlainText());
				}
			}
			else
			{
				await notifyService.Notify(executor, result.AsT1.Value, executor);
			}

			return new CallState(result.Match(_ => string.Empty, failure => failure.Value));
		}

		// `do { f = split_token(&p, ' '); … set_flag(player, thing, f, negate, …) } while (p)`. The
		// loop runs to the end whatever any one token does; the first failure is what the caller is
		// told about, since a function has one return value and Penn has none at all.
		async ValueTask<CallState> SetFlags(AnySharpObject found)
		{
			CallState? failure = null;

			foreach (var flagName in MushText.SplitList(MarkupText.Space, flagOrAttributeValue)
								 .Select(token => token.ToPlainText()))
			{
				// set_flag reports when `is_flag(f, "QUIET") || !AreQuiet(player, thing)` — touching the
				// QUIET flag itself always reports, so you can see what you just made quiet.
				var togglesQuiet = flagName.TrimStart('!').Equals("QUIET", StringComparison.OrdinalIgnoreCase);
				var result = await manipulateSharpObjectService.SetOrUnsetFlag(executor, found, flagName,
					togglesQuiet || !areQuiet);

				if (failure is null && result.Message?.ToPlainText().StartsWith("#-1", StringComparison.Ordinal) == true)
				{
					failure = result;
				}
			}

			return failure ?? CallState.Empty;
		}
	}
}
