using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// The one <c>$</c>-command a command hook names. Dispatch runs it; the onboarding guide asks whether it would run.
/// </summary>
public static class HookedCommandMatch
{
	/// <summary>
	/// PennMUSH <c>one_comm_match</c> (<c>src/attrib.c:2132-2181</c>): the one <c>$</c>-command
	/// <paramref name="attributeName"/> names on <paramref name="thing"/>, read through its parents and
	/// type ancestor as <c>atr_get_with_parent</c> reads it, matched against <paramref name="input"/>.
	/// A HALT or NO_COMMAND object, a no_command attribute or branch, a value that is not a
	/// <c>$</c>-command, and a refusal by <paramref name="player"/>'s @lock/command or @lock/use on
	/// <paramref name="thing"/> are all no match.
	/// </summary>
	public static async ValueTask<Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>> MatchAsync(
		IAttributeService attributes, ILockService locks, AnySharpObject thing, AnySharpObject player, string attributeName, MString input)
	{
		if (await thing.HasFlag("HALT") || await thing.HasFlag("NO_COMMAND"))
		{
			return new None();
		}

		// atr_get_with_parent with cmd set refuses AF_NOPROG on the attribute and on every branch above it.
		if (await attributes.GetAttributeAsync(thing, thing, attributeName,
					IAttributeService.AttributeMode.Read, parent: true) is not SharpAttribute[] { Length: > 0 } chain
				|| chain.Any(segment => segment.IsNoprog())
				|| CommandAttributeScanner.Compile(chain[^1]) is not CommandAttributeCache command)
		{
			return new None();
		}

		// run_cmd_hook matches the evaluated command as process_command built it, trailing spaces and all.
		var trimmed = input.Trim(TrimType.TrimStart);
		if (SoftcodeRegex.Match(command.CompiledRegex, trimmed.ToPlainText()) is not { Success: true } match
				|| !await locks.Evaluate(LockType.Command, thing, player)
				|| !await locks.Evaluate(LockType.Use, thing, player))
		{
			return new None();
		}

		return Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>
			.FromOption([(thing, command.Attribute, PatternArguments.Capture(command.CompiledRegex, match, command.IsRegexFlag, trimmed))]);
	}
}
