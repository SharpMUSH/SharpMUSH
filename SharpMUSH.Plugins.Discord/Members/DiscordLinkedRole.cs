using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>
/// The Linked role: the bot makes it in every server it is in, gives it to each member linked to a game account and
/// takes it from everyone else. A linked channel lets only this role post, so an unlinked member cannot speak into the
/// game. Each time a server becomes available the bot goes over all its members, which repairs anything missed while
/// it was away.
/// </summary>
public sealed class DiscordLinkedRole(IDiscordAccountStore store, ILogger<DiscordLinkedRole> logger)
	: IDiscordClientHandler, IDiscordLinkedRole
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

			var options = new RequestOptions { CancelToken = cancellationToken, AuditLogReason = give ? "Linked to a game account" : "Unlinked from the game" };
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
		if (store.Get(member.Id) is { Linked: true } && await RoleAsync(member.Guild) is { } role)
		{
			await member.AddRoleAsync(role.Id, new RequestOptions { AuditLogReason = "Linked to a game account" });
		}
	}

	/// <summary>Gives the role to every linked member who lacks it, and takes it from every unlinked one who holds it.</summary>
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
			var linked = store.Get(member.Id) is { Linked: true };
			var holds = member.Roles.Any(held => held.Id == role.Id);
			if (linked && !holds)
			{
				await member.AddRoleAsync(role.Id, new RequestOptions { AuditLogReason = "Linked to a game account" });
			}
			else if (!linked && holds)
			{
				await member.RemoveRoleAsync(role.Id, new RequestOptions { AuditLogReason = "Not linked to a game account" });
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
