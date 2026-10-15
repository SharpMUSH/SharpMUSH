using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>Lets only linked members post in a linked channel.</summary>
public interface IDiscordChannelGate
{
	/// <summary>
	/// Denies Send Messages to <c>@everyone</c> in the channel and allows it to the Linked role, keeping the channel's
	/// other permissions as they are.
	/// </summary>
	/// <returns>Null once done, else why it could not be, said for the admin.</returns>
	Task<string?> GateAsync(ulong channelId, CancellationToken cancellationToken);

	/// <summary>
	/// Who besides the Linked role may post in the channel: <c>@everyone</c>, or a role the channel allows it to.
	/// Empty when only linked members may; null when the bot cannot tell, being disconnected or unable to see it.
	/// </summary>
	IReadOnlyList<string>? OpenTo(ulong channelId);
}

/// <summary>
/// The Linked role: the bot makes it in every server it is in, gives it to each member linked to an active game account
/// and takes it from everyone else, a member whose account is banned, disabled or closed among them. A linked channel lets only this role post, so an unlinked member cannot speak into the
/// game. Each time a server becomes available the bot goes over all its members, which repairs anything missed while
/// it was away.
/// </summary>
public sealed class DiscordLinkedRole(IDiscordAccountStore store, IDiscordStandings standings, ILogger<DiscordLinkedRole> logger)
	: IDiscordClientHandler, IDiscordLinkedRole, IDiscordChannelGate
{
	public const string RoleName = "Linked";

	private DiscordSocketClient? _client;

	public void Attach(DiscordSocketClient client)
	{
		_client = client;
		client.GuildAvailable += guild => Background(() => ReconcileAsync(guild), guild.Name);
		client.JoinedGuild += guild => Background(() => ReconcileAsync(guild), guild.Name);
		client.UserJoined += member => Background(() => JoinedAsync(member), member.Guild.Name);
	}

	/// <summary>The Linked role's id in <paramref name="guild"/>, made if the bot may and it is missing.</summary>
	public async Task<IRole?> RoleAsync(SocketGuild guild)
	{
		if (store.Guild(guild.Id) is { } known && guild.GetRole(known.LinkedRoleId) is { } existing)
		{
			return existing;
		}

		if (!guild.CurrentUser.GuildPermissions.ManageRoles)
		{
			logger.LogWarning("[Discord] The bot lacks Manage Roles in {Guild}, so it cannot make the {Role} role.", guild.Name, RoleName);
			return null;
		}

		var role = await guild.CreateRoleAsync(RoleName, GuildPermissions.None, isHoisted: false, isMentionable: false);
		await store.SaveGuildAsync(new DiscordGuildRecord(guild.Id, role.Id));
		logger.LogInformation("[Discord] Made the {Role} role in {Guild}.", RoleName, guild.Name);
		return role;
	}

	public async Task<string?> GateAsync(ulong channelId, CancellationToken cancellationToken)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return "The bot is not connected to Discord.";
		}

		if (client.GetChannel(channelId) is not SocketTextChannel channel)
		{
			return "The bot cannot see that channel.";
		}

		if (!channel.Guild.CurrentUser.GetPermissions(channel).ManageRoles)
		{
			return $"The bot lacks Manage Permissions in #{channel.Name}.";
		}

		if (await RoleAsync(channel.Guild) is not { } role)
		{
			return $"The bot lacks Manage Roles in {channel.Guild.Name}, so there is no {RoleName} role.";
		}

		var options = new RequestOptions { CancelToken = cancellationToken, AuditLogReason = "Only linked members post in a linked channel" };
		try
		{
			// The Linked role first: if the second change fails, the channel is left open rather than closed to everyone.
			var linked = channel.GetPermissionOverwrite(role) ?? OverwritePermissions.InheritAll;
			await channel.AddPermissionOverwriteAsync(role,
				linked.Modify(sendMessages: PermValue.Allow, sendMessagesInThreads: PermValue.Allow), options);
			var everyone = channel.GetPermissionOverwrite(channel.Guild.EveryoneRole) ?? OverwritePermissions.InheritAll;
			await channel.AddPermissionOverwriteAsync(channel.Guild.EveryoneRole,
				everyone.Modify(sendMessages: PermValue.Deny, sendMessagesInThreads: PermValue.Deny), options);
			return null;
		}
		catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException)
		{
			return $"Discord refused the change in #{channel.Name}: {exception.Message}";
		}
	}

	public IReadOnlyList<string>? OpenTo(ulong channelId)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client
			|| client.GetChannel(channelId) is not SocketTextChannel channel)
		{
			return null;
		}

		var guild = channel.Guild;
		var linkedRole = store.Guild(guild.Id)?.LinkedRoleId;
		List<string> open = [];
		if (EveryoneMaySend(guild, channel))
		{
			open.Add("@everyone");
		}

		// A role the channel lets post on purpose. A bot's own role is left out: bots are never relayed.
		open.AddRange(guild.Roles
			.Where(role => role.Id != guild.EveryoneRole.Id && role.Id != linkedRole && !role.IsManaged
				&& channel.GetPermissionOverwrite(role)?.SendMessages == PermValue.Allow)
			.Select(role => "@" + role.Name));
		return open;
	}

	/// <summary>Whether a member with no role may post: the server's <c>@everyone</c> permissions, then the channel's.</summary>
	private static bool EveryoneMaySend(SocketGuild guild, SocketTextChannel channel)
	{
		var permissions = guild.EveryoneRole.Permissions;
		if (permissions.Administrator)
		{
			return true;
		}

		return channel.GetPermissionOverwrite(guild.EveryoneRole)?.SendMessages switch
		{
			PermValue.Deny => false,
			PermValue.Allow => true,
			_ => permissions.SendMessages,
		};
	}

	public Task GiveAsync(ulong userId, CancellationToken cancellationToken)
		=> ChangeAsync(userId, give: true, cancellationToken);

	public Task TakeAsync(ulong userId, CancellationToken cancellationToken)
		=> ChangeAsync(userId, give: false, cancellationToken);

	private async Task ChangeAsync(ulong userId, bool give, CancellationToken cancellationToken)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return;
		}

		foreach (var guild in client.Guilds)
		{
			if (guild.GetUser(userId) is not { } member || await RoleAsync(guild) is not { } role)
			{
				continue;
			}

			var options = new RequestOptions { CancelToken = cancellationToken, AuditLogReason = give ? "Linked to an active game account" : "Unlinked, or the game account is not active" };
			if (give && member.Roles.All(held => held.Id != role.Id))
			{
				await member.AddRoleAsync(role.Id, options);
			}
			else if (!give && member.Roles.Any(held => held.Id == role.Id))
			{
				await member.RemoveRoleAsync(role.Id, options);
			}
		}
	}

	private async Task JoinedAsync(SocketGuildUser member)
	{
		if (store.Get(member.Id) is { Linked: true } link
			&& await standings.StandingAsync(link.AccountId, CancellationToken.None) == DiscordStanding.Active
			&& await RoleAsync(member.Guild) is { } role)
		{
			await member.AddRoleAsync(role.Id, new RequestOptions { AuditLogReason = "Linked to a game account" });
		}
	}

	/// <summary>Gives the role to every member linked to an active account who lacks it, and takes it from everyone else who holds it.</summary>
	private async Task ReconcileAsync(SocketGuild guild)
	{
		if (await RoleAsync(guild) is not { } role)
		{
			return;
		}

		await guild.DownloadUsersAsync();
		foreach (var member in guild.Users.Where(member => !member.IsBot))
		{
			// Read per member, not once up front: a /link or /unlink during the pass must not be undone by it.
			var linked = store.Get(member.Id) is { Linked: true } link
				&& await standings.StandingAsync(link.AccountId, CancellationToken.None) == DiscordStanding.Active;
			var holds = member.Roles.Any(held => held.Id == role.Id);
			if (linked && !holds)
			{
				await member.AddRoleAsync(role.Id, new RequestOptions { AuditLogReason = "Linked to a game account" });
			}
			else if (!linked && holds)
			{
				await member.RemoveRoleAsync(role.Id, new RequestOptions { AuditLogReason = "Not linked to an active game account" });
			}
		}
	}

	/// <summary>Off the gateway's own thread, which a slow role change must not hold up.</summary>
	private Task Background(Func<Task> work, string guild)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await work();
			}
			catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException)
			{
				logger.LogWarning("[Discord] Could not update the {Role} role in {Guild}: {Reason}", RoleName, guild, exception.Message);
			}
			catch (Exception exception)
			{
				// Nothing awaits this task; a bug here must reach the log rather than vanish.
				logger.LogError(exception, "[Discord] Updating the {Role} role in {Guild} failed.", RoleName, guild);
			}
		});
		return Task.CompletedTask;
	}
}
