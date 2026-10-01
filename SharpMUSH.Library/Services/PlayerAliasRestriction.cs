using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's validation of a player's alias list: the ALIAS branch of <c>do_set_atr</c>
/// (<c>src/attrib.c:2268-2316</c>) and <c>ok_player_alias</c> (<c>src/predicat.c:880</c>). A refusal is
/// the notification key Penn's wording lives under; its one argument is always the value being set.
/// </summary>
internal static class PlayerAliasRestriction
{
	/// <summary>
	/// Whether <paramref name="setter"/> may set <paramref name="player"/>'s ALIAS to
	/// <paramref name="value"/>. With an alias already set, an empty value is refused and a value
	/// differing from it only in case is not checked at all; with none, only a non-empty value is.
	/// </summary>
	public static async ValueTask<Result<Success>> CheckAsync(IMediator mediator, IValidateService validateService,
		uint maxAliases, AnySharpObject setter, AnySharpObject player, string value)
	{
		var current = await mediator
			.CreateStream(new GetAttributeQuery(player.Object().DBRef, [PlayerAliases.AttributeName]))
			.LastOrDefaultAsync();

		if (current is not null)
		{
			if (value.Length == 0)
			{
				return NotValid(value);
			}

			if (value.Equals(current.Value.ToPlainText(), StringComparison.OrdinalIgnoreCase))
			{
				return new Success();
			}
		}
		else if (value.Length == 0)
		{
			return new Success();
		}

		return await CheckListAsync(validateService, maxAliases, setter, player, value);
	}

	/// <summary>
	/// <c>ok_player_alias</c>: every <c>;</c>-separated entry, leading spaces skipped, is a name the player
	/// could take (<c>ok_player_name</c>: well formed, and no other player's name or alias), none is empty,
	/// and a setter who is not a wizard names no more than <c>max_aliases</c> of them.
	/// </summary>
	private static async ValueTask<Result<Success>> CheckListAsync(IValidateService validateService,
		uint maxAliases, AnySharpObject setter, AnySharpObject player, string value)
	{
		var count = 0;

		foreach (var alias in value.Split(PlayerAliases.Delimiter).Select(entry => entry.TrimStart(' ')))
		{
			if (alias.Length == 0)
			{
				return new Error<string>(ErrorMessages.Notifications.PlayerAliasNull);
			}

			if (!await validateService.ValidPlayerName(MarkupText.Plain(alias), setter, player))
			{
				return NotValid(value);
			}

			count++;
		}

		return count > maxAliases && !await setter.IsWizard()
			? new Error<string>(string.Format(ErrorMessages.Notifications.PlayerAliasTooManyFormat, value))
			: new Success();
	}

	private static Error<string> NotValid(string value)
		=> new(string.Format(ErrorMessages.Notifications.PlayerAliasNotValidFormat, value));
}
