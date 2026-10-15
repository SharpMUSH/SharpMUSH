using System.Net;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Plugins.Discord.Storage;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>A Discord user the bot banned because their game account was banned, and the servers it banned them in.</summary>
public sealed record DiscordMirroredBan(ulong UserId, string AccountId, IReadOnlyList<ulong> Guilds, DateTimeOffset At);

public interface IDiscordBanStore
{
	IReadOnlyList<DiscordMirroredBan> List();

	DiscordMirroredBan? Get(ulong userId);

	ValueTask SaveAsync(DiscordMirroredBan ban, CancellationToken cancellationToken = default);

	ValueTask<bool> RemoveAsync(ulong userId, CancellationToken cancellationToken = default);
}

/// <summary>The plugin-owned table <c>discord.ban</c>, by Discord user id.</summary>
public sealed class LightningDiscordBanStore(ILightningStorageAccessor accessor) : IDiscordBanStore
{
	private readonly DiscordTable<DiscordMirroredBan> _bans = new(accessor, "discord.ban");

	public IReadOnlyList<DiscordMirroredBan> List() => _bans.List();

	public DiscordMirroredBan? Get(ulong userId) => _bans.Get(userId);

	public ValueTask SaveAsync(DiscordMirroredBan ban, CancellationToken cancellationToken = default)
		=> _bans.SaveAsync(ban.UserId, ban, cancellationToken);

	public ValueTask<bool> RemoveAsync(ulong userId, CancellationToken cancellationToken = default)
		=> _bans.RemoveAsync(userId, cancellationToken);
}

/// <summary>
/// Follows a linked account's standing on Discord: the Linked role goes while the account is banned, disabled or
/// closed and comes back when it is active again, and with <see cref="DiscordSettings.MirrorBans"/> on, a game ban
/// bans the member in every server the bot is in (<see cref="DiscordStandingPlan"/>). It hears every ban, lift and
/// status change through <see cref="IAccountChangeListener"/>, and goes over everyone when a server becomes available,
/// when the bot joins one or is granted Ban Members, and when mirroring is turned on, to catch what it missed.
/// </summary>
/// <remarks>Mapped roles follow the account's standing in <see cref="Roles.DiscordRoleSync"/>.</remarks>
public sealed class DiscordStandingSync(
	IDiscordAccountStore accounts,
	IDiscordBanStore bans,
	IDiscordSettingsStore settings,
	IDiscordStandings standings,
	DiscordLinkedRole linkedRole,
	TimeProvider time,
	ILogger<DiscordStandingSync> logger)
	: IDiscordClientHandler, IAccountChangeListener
{
	private const string BanReason = "Banned in the game";

	/// <summary>One change at a time, so two quick changes to one account cannot ban and lift out of order.</summary>
	private readonly SemaphoreSlim _changing = new(1, 1);

	private DiscordSocketClient? _client;

	public void Attach(DiscordSocketClient client)
	{
		_client = client;
		client.GuildAvailable += _ => PassSoon();
		client.JoinedGuild += _ => PassSoon();
		// A bot refused Ban Members bans nobody until it is granted, so whatever grants it starts a pass: a role the bot
		// holds gaining it, or the bot being given a role.
		client.RoleUpdated += (_, role) => role.Guild.CurrentUser is { } bot && bot.Roles.Any(held => held.Id == role.Id)
			? BanMembersGranted(bot)
			: Task.CompletedTask;
		client.GuildMemberUpdated += (_, member) => member.Id == client.CurrentUser?.Id ? BanMembersGranted(member) : Task.CompletedTask;
	}

	private Task PassSoon()
	{
		Background(PassAsync);
		return Task.CompletedTask;
	}

	private Task BanMembersGranted(SocketGuildUser bot)
		=> settings.Get().MirrorBans && bot.GuildPermissions.BanMembers ? PassSoon() : Task.CompletedTask;

	public ValueTask AccountChangedAsync(string accountId, CancellationToken cancellationToken)
	{
		var users = bans.List().Where(ban => ban.AccountId == accountId).Select(ban => ban.UserId).ToHashSet();
		if (accounts.ForAccount(accountId) is { } link)
		{
			users.Add(link.UserId);
		}

		foreach (var user in users)
		{
			Background(token => UpdateAsync(user, accountId, token));
		}

		return ValueTask.CompletedTask;
	}

	/// <summary>Turns mirroring on or off. Turning it on bans every member whose account is banned now.</summary>
	public async ValueTask SetMirrorBansAsync(bool on, CancellationToken cancellationToken)
	{
		await settings.SaveAsync(settings.Get() with { MirrorBans = on }, cancellationToken);
		if (on)
		{
			Background(PassAsync);
		}
	}

	/// <summary>Everyone linked, and everyone the bot banned, against their account's standing now.</summary>
	private async Task PassAsync(CancellationToken cancellationToken)
	{
		var users = accounts.List().Where(link => link.Linked).Select(link => (link.UserId, link.AccountId))
			.Concat(bans.List().Select(ban => (ban.UserId, ban.AccountId)))
			.Distinct()
			.ToList();
		foreach (var (user, account) in users)
		{
			await UpdateAsync(user, account, cancellationToken);
		}
	}

	private async Task UpdateAsync(ulong userId, string accountId, CancellationToken cancellationToken)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return;
		}

		await _changing.WaitAsync(cancellationToken);
		try
		{
			var linked = accounts.Get(userId) is { Linked: true } link && link.AccountId == accountId;
			var mirrored = bans.Get(userId);
			var plan = DiscordStandingPlan.For(await standings.StandingAsync(accountId, cancellationToken), linked,
				settings.Get().MirrorBans, [.. client.Guilds.Select(guild => guild.Id)], mirrored?.Guilds ?? []);
			switch (plan.HoldLinked)
			{
				case true:
					await linkedRole.GiveAsync(userId, cancellationToken);
					break;
				case false:
					await linkedRole.TakeAsync(userId, cancellationToken);
					break;
			}

			// A ban the bot cannot lift now, in a server it is not in, stays on record to lift once it is back.
			var bannedIn = (mirrored?.Guilds ?? []).ToList();
			foreach (var guildId in plan.Unban)
			{
				if (await UnbanAsync(client.GetGuild(guildId), userId, cancellationToken))
				{
					bannedIn.Remove(guildId);
				}
			}

			foreach (var guildId in plan.Ban)
			{
				if (await BanAsync(client.GetGuild(guildId), userId, cancellationToken))
				{
					bannedIn.Add(guildId);
				}
			}

			if (bannedIn.Count == 0)
			{
				await bans.RemoveAsync(userId, cancellationToken);
			}
			else if (!bannedIn.SequenceEqual(mirrored?.Guilds ?? []))
			{
				await bans.SaveAsync(new DiscordMirroredBan(userId, accountId, bannedIn, mirrored?.At ?? time.GetUtcNow()), cancellationToken);
			}
		}
		finally
		{
			_changing.Release();
		}
	}

	/// <returns>Whether the bot banned the member there, which it lifts when the game ban ends.</returns>
	private async Task<bool> BanAsync(SocketGuild? guild, ulong userId, CancellationToken cancellationToken)
	{
		if (guild is null)
		{
			return false;
		}

		if (!guild.CurrentUser.GuildPermissions.BanMembers)
		{
			logger.LogWarning("[Discord] Game bans are mirrored, but the bot lacks Ban Members in {Guild}.", guild.Name);
			return false;
		}

		var options = new RequestOptions { CancelToken = cancellationToken };
		if (await guild.GetBanAsync(userId, options) is not null)
		{
			// Staff banned them already; that ban is theirs, and stays when the game ban ends.
			return false;
		}

		await guild.AddBanAsync(userId, 0, BanReason, new RequestOptions { CancelToken = cancellationToken, AuditLogReason = BanReason });
		logger.LogInformation("[Discord] Banned a member in {Guild} whose game account is banned.", guild.Name);
		return true;
	}

	/// <returns>Whether the ban is gone: lifted now, or by staff already. False while the bot is not in the server.</returns>
	private async Task<bool> UnbanAsync(SocketGuild? guild, ulong userId, CancellationToken cancellationToken)
	{
		if (guild is null)
		{
			return false;
		}

		try
		{
			await guild.RemoveBanAsync(userId, new RequestOptions { CancelToken = cancellationToken, AuditLogReason = "Game ban lifted" });
			logger.LogInformation("[Discord] Lifted the ban in {Guild} that mirrored a game ban.", guild.Name);
		}
		catch (HttpException exception) when (exception.HttpCode == HttpStatusCode.NotFound)
		{
			// Staff already lifted it on Discord.
		}

		return true;
	}

	/// <summary>Off the gateway's own thread and the caller's, which a slow Discord request must not hold up.</summary>
	private void Background(Func<CancellationToken, Task> work)
		=> _ = Task.Run(async () =>
		{
			try
			{
				await work(CancellationToken.None);
			}
			catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException)
			{
				logger.LogWarning("[Discord] Could not follow a game account's standing on Discord: {Reason}", exception.Message);
			}
			catch (Exception exception)
			{
				// Nothing awaits this task; a bug here must reach the log rather than vanish.
				logger.LogError(exception, "[Discord] Following a game account's standing on Discord failed.");
			}
		});
}
