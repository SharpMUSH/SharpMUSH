using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>A character in the game, as choosing a speaking identity needs it.</summary>
/// <param name="Objid">Its objid, which the identity is stored by.</param>
public sealed record DiscordCharacter(DBRef Objid, string Name, bool IsPlayer);

/// <summary>
/// Speaking identities, chosen by the character itself (<c>@discord/speak</c>): the account the character is on must
/// be linked to Discord, and the character must pass the linked channel's send lock.
/// </summary>
public sealed class DiscordIdentities(
	IDiscordLinkStore links,
	DiscordAccounts accounts,
	IDiscordSpeakerStore speakers,
	IAccountService accountService)
{
	/// <summary>Makes <paramref name="character"/> its account's Discord user's identity in the channel.</summary>
	/// <param name="maySend">Whether the character passes the channel's feed send lock, asked once the rest holds.</param>
	/// <returns>What to tell the character.</returns>
	public async Task<Result<string>> SpeakAsync(DiscordCharacter character, ulong channelId, Func<Task<bool>> maySend,
		CancellationToken cancellationToken)
	{
		return await LinkedAsync(character, channelId, cancellationToken) switch
		{
			Linked(var link, var user) => await maySend()
				? await ChosenAsync(character, link, user, cancellationToken)
				: new Error<string>($"{character.Name} may not send to {DiscordChannelLink.FeedKind}/{link.FeedKey}, so it cannot speak there."),
			Error<string> error => error,
		};
	}

	/// <summary>Stops <paramref name="character"/> speaking in the channel, if it is who its Discord user speaks as there.</summary>
	public async Task<Result<string>> StopAsync(DiscordCharacter character, ulong channelId, CancellationToken cancellationToken)
	{
		return await LinkedAsync(character, channelId, cancellationToken) switch
		{
			Linked(var link, var user) when speakers.Character(user.UserId, channelId) == character.Objid.ToString()
				=> await StoppedAsync(character, link, user, cancellationToken),
			Linked(var link, _) => new Error<string>($"{character.Name} does not speak in #{link.ChannelName}."),
			Error<string> error => error,
		};
	}

	private async Task<Result<string>> ChosenAsync(DiscordCharacter character, DiscordChannelLink link, DiscordAccountLink user,
		CancellationToken cancellationToken)
	{
		await speakers.SetAsync(user.UserId, link.ChannelId, character.Objid.ToString(), cancellationToken);
		return $"Your messages as {user.Username} in #{link.ChannelName} now reach the game as {character.Name}. "
			+ $"Anyone who reads both can tell that {user.Username} plays {character.Name}.";
	}

	private async Task<Result<string>> StoppedAsync(DiscordCharacter character, DiscordChannelLink link, DiscordAccountLink user,
		CancellationToken cancellationToken)
	{
		await speakers.ClearAsync(user.UserId, link.ChannelId, cancellationToken);
		return $"{character.Name} no longer speaks for {user.Username} in #{link.ChannelName}.";
	}

	private async Task<LinkedOrError> LinkedAsync(DiscordCharacter character, ulong channelId, CancellationToken cancellationToken)
	{
		if (!character.IsPlayer)
		{
			return new Error<string>("Only a character speaks in Discord.");
		}

		if (links.Get(channelId) is not { } link)
		{
			return new Error<string>("That channel is not linked.");
		}

		if (await accountService.GetAccountForCharacterAsync(character.Objid, cancellationToken) is not { Id: { } accountId })
		{
			return new Error<string>($"{character.Name} is not on a game account, so it cannot speak in Discord.");
		}

		return accounts.ForAccount(accountId) is { } user
			? new Linked(link, user)
			: new Error<string>("Your game account is not linked to Discord. Type /link in Discord to link it.");
	}

	private readonly record struct Linked(DiscordChannelLink Link, DiscordAccountLink User);

	private union LinkedOrError(Linked, Error<string>);
}
