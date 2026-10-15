using Discord.Net;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>Gives and takes the Linked role a linked member holds in every server the bot is in.</summary>
public interface IDiscordLinkedRole
{
	Task GiveAsync(ulong userId, CancellationToken cancellationToken);

	Task TakeAsync(ulong userId, CancellationToken cancellationToken);
}

/// <summary>
/// Links Discord users to game accounts. A member runs <c>/link</c> in Discord, which proves who they are there, and
/// confirms in the portal while signed in, which proves which account is theirs. Either side can unlink, which drops
/// the user's speaking identities too.
/// </summary>
public sealed class DiscordAccounts(
	IDiscordAccountStore store,
	IDiscordSpeakerStore speakers,
	DiscordLinkTickets tickets,
	IAccountService accounts,
	IDiscordLinkedRole linkedRole,
	TimeProvider time,
	ILogger<DiscordAccounts> logger)
{
	/// <summary>
	/// How long a Discord user unlinked from one account waits before linking another, so a mapped role cannot be
	/// handed from account to account.
	/// </summary>
	public static readonly TimeSpan RelinkCooldown = TimeSpan.FromDays(7);

	/// <summary>One link or unlink at a time, so each side keeps at most one link.</summary>
	private readonly SemaphoreSlim _changing = new(1, 1);

	public DiscordAccountLink? Find(ulong userId) => store.Get(userId) is { Linked: true } link ? link : null;

	public DiscordAccountLink? ForAccount(string accountId) => store.ForAccount(accountId);

	/// <summary><c>/link</c>: a ticket for the member to confirm in the portal, unless they are linked already.</summary>
	public Result<DiscordLinkTicket> StartLink(DiscordUserCard user)
		=> Find(user.UserId) is not null
			? new Error<string>("Your Discord account is already linked to a game account. Use /unlink first to link another.")
			: tickets.Mint(user);

	/// <summary>The ticket the portal is about to confirm, while it is good.</summary>
	public DiscordLinkTicket? Ticket(string id) => tickets.Find(id);

	/// <summary>The signed-in account confirms the ticket, which links the two.</summary>
	public async Task<Result<DiscordAccountLink>> ConfirmAsync(string ticketId, string accountId,
		CancellationToken cancellationToken)
	{
		await _changing.WaitAsync(cancellationToken);
		try
		{
			return await ConfirmLockedAsync(ticketId, accountId, cancellationToken);
		}
		finally
		{
			_changing.Release();
		}
	}

	/// <summary>Unlinks <paramref name="userId"/>, from Discord's <c>/unlink</c>.</summary>
	/// <returns>The link that was dropped, or <see cref="NotFound"/>.</returns>
	public Task<Found<DiscordAccountLink>> UnlinkUserAsync(ulong userId, CancellationToken cancellationToken)
		=> UnlinkAsync(() => Find(userId), cancellationToken);

	/// <summary>Unlinks whoever is linked to <paramref name="accountId"/>, from the portal.</summary>
	public Task<Found<DiscordAccountLink>> UnlinkAccountAsync(string accountId, CancellationToken cancellationToken)
		=> UnlinkAsync(() => store.ForAccount(accountId), cancellationToken);

	private async Task<Result<DiscordAccountLink>> ConfirmLockedAsync(string ticketId, string accountId,
		CancellationToken cancellationToken)
	{
		if (tickets.Find(ticketId) is not { } ticket)
		{
			return new Error<string>("That link has expired or was already used. Type /link in Discord for a new one.");
		}

		if (await accounts.GetByIdAsync(accountId, cancellationToken) is not { IsActive: true })
		{
			return new Error<string>("Only an active account can link Discord.");
		}

		if (store.ForAccount(accountId) is { } own)
		{
			return own.UserId == ticket.User.UserId
				? own
				: new Error<string>($"This account is already linked to Discord as {own.Username}. Unlink it first.");
		}

		var now = time.GetUtcNow();
		switch (store.Get(ticket.User.UserId))
		{
			case { Linked: true }:
				return new Error<string>("That Discord account is linked to another game account.");
			case { UnlinkedAt: { } unlinked } previous when previous.AccountId != accountId && now - unlinked < RelinkCooldown:
				return new Error<string>(
					$"That Discord account was linked to another game account until {unlinked:yyyy-MM-dd}. It can link again from {unlinked + RelinkCooldown:yyyy-MM-dd HH:mm} UTC.");
		}

		if (!tickets.Spend(ticket))
		{
			return new Error<string>("That link has expired or was already used. Type /link in Discord for a new one.");
		}

		var link = new DiscordAccountLink(ticket.User.UserId, accountId, ticket.User.Username, now);
		await store.SaveAsync(link, CancellationToken.None);
		logger.LogInformation("[Discord] {User} linked to account {Account}.", link.Username, accountId);
		await ChangeRoleAsync(() => linkedRole.GiveAsync(link.UserId, cancellationToken), link);
		return link;
	}

	private async Task<Found<DiscordAccountLink>> UnlinkAsync(Func<DiscordAccountLink?> find,
		CancellationToken cancellationToken)
	{
		await _changing.WaitAsync(cancellationToken);
		try
		{
			if (find() is not { } link)
			{
				return new NotFound();
			}

			await store.SaveAsync(link with { UnlinkedAt = time.GetUtcNow() }, CancellationToken.None);
			// Its speaking identities go with it; linking again starts with none.
			await speakers.ForgetAsync(link.UserId, CancellationToken.None);
			logger.LogInformation("[Discord] {User} unlinked from account {Account}.", link.Username, link.AccountId);
			await ChangeRoleAsync(() => linkedRole.TakeAsync(link.UserId, cancellationToken), link);
			return link;
		}
		finally
		{
			_changing.Release();
		}
	}

	/// <summary>
	/// The link is what counts; a role Discord would not change now is put right when the bot next goes over the
	/// server's members.
	/// </summary>
	private async Task ChangeRoleAsync(Func<Task> change, DiscordAccountLink link)
	{
		try
		{
			await change();
		}
		catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException)
		{
			logger.LogWarning(exception, "[Discord] Could not change {User}'s Linked role.", link.Username);
		}
	}
}
