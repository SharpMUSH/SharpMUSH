using System.Threading.Channels;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Plugins.Discord.Roles;

/// <summary>A Discord role, as mapping one needs to know it.</summary>
/// <param name="Problem">Why the bot cannot give or take it, or null when it can.</param>
public sealed record DiscordRoleInfo(ulong RoleId, ulong GuildId, string Name, string GuildName, bool Everyone, bool Managed,
	bool Linked, string? Problem);

/// <summary>The Discord side of role mapping.</summary>
public interface IDiscordRoles
{
	/// <summary>A role in a server the bot is in, by its id or its name.</summary>
	Result<DiscordRoleInfo> Find(string idOrName);

	/// <summary>Why the bot cannot apply the mapping now, or null when it can.</summary>
	string? Problem(DiscordRoleMapping mapping);

	/// <summary>Goes over every member of the mapping's server, as the daily pass does.</summary>
	void Pass(ulong guildId);

	/// <summary>Applies the game's answer again to each member with open drift, on one role or on all, and settles it.</summary>
	void Settle(ulong? roleId);
}

/// <summary>Tells role mapping that a Discord user linked or unlinked.</summary>
public interface IDiscordMemberSync
{
	void UserChanged(ulong userId);
}

/// <summary>
/// Keeps mapped Discord roles in step with the game. A member's roles are put right when something changes for them in
/// the game (any change to the account's roles, characters or standing reaches <see cref="AccountChangedAsync"/>), when
/// they link or unlink, and when they join a server. Every server is gone over when it becomes available and once a
/// day, to repair what was missed while the bot was away. A mapped role changed by hand on Discord is drift: recorded,
/// and put back only on an enforced mapping.
/// </summary>
/// <remarks>
/// The work runs one item at a time off the gateway's own thread, in the order it came. A member's roles are read
/// afresh from the game each time, so a burst of changes to one account ends in its latest state.
/// </remarks>
public sealed class DiscordRoleSync(
	IDiscordRoleStore roles,
	IDiscordAccountStore accounts,
	IDiscordRoleHolders holders,
	TimeProvider time,
	ILogger<DiscordRoleSync> logger)
	: BackgroundService, IDiscordClientHandler, IAccountChangeListener, IDiscordMemberSync, IDiscordRoles
{
	/// <summary>Past this many waiting items, new ones are dropped and logged; the daily pass repairs what they missed.</summary>
	public const int MaxWaiting = 1000;

	public static readonly TimeSpan PassEvery = TimeSpan.FromDays(1);

	private readonly Channel<Func<CancellationToken, Task>> _work = Channel.CreateBounded<Func<CancellationToken, Task>>(
		new BoundedChannelOptions(MaxWaiting) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

	private DiscordSocketClient? _client;
	private ITimer? _daily;

	public void Attach(DiscordSocketClient client)
	{
		_client = client;
		client.GuildAvailable += guild => Queue(cancellationToken => PassAsync(guild, cancellationToken));
		client.JoinedGuild += guild => Queue(cancellationToken => PassAsync(guild, cancellationToken));
		client.UserJoined += member => Queue(cancellationToken => ChangedAsync(member, cancellationToken));
		client.GuildMemberUpdated += (_, member) => Mapped(member.Guild.Id)
			? Queue(cancellationToken => ObservedAsync(member, cancellationToken))
			: Task.CompletedTask;
		client.RoleDeleted += role =>
		{
			if (roles.Mapping(role.Id) is { } mapping)
			{
				logger.LogWarning("[Discord] The mapped role {Role} was deleted in {Guild}; its mapping to {Slug} is broken.",
					role.Name, role.Guild.Name, mapping.Slug);
			}

			return Task.CompletedTask;
		};
	}

	public ValueTask AccountChangedAsync(string accountId, CancellationToken cancellationToken)
	{
		if (accounts.ForAccount(accountId) is { } link)
		{
			UserChanged(link.UserId);
		}

		return ValueTask.CompletedTask;
	}

	public void UserChanged(ulong userId)
		=> Queue(async cancellationToken =>
		{
			foreach (var guild in Guilds())
			{
				if (guild.GetUser(userId) is { } member)
				{
					await ChangedAsync(member, cancellationToken);
				}
			}
		});

	public void Pass(ulong guildId)
		=> Queue(cancellationToken => _client?.GetGuild(guildId) is { } guild ? PassAsync(guild, cancellationToken) : Task.CompletedTask);

	public void Settle(ulong? roleId)
		=> Queue(async cancellationToken =>
		{
			foreach (var record in roles.Drift().Where(record => roleId is null || record.RoleId == roleId))
			{
				if (roles.Mapping(record.RoleId) is not { } mapping || _client?.GetGuild(mapping.GuildId) is not { } guild)
				{
					continue;
				}

				foreach (var drift in record.Members)
				{
					if (guild.GetUser(drift.UserId) is { } member)
					{
						var held = await HeldAsync(member.Id, cancellationToken);
						await ApplyAsync(guild, DiscordRolePlan.Changed(Member(member), [mapping], held).Changes, "Put back to the game's roles");
					}

					await roles.SettleAsync(record.RoleId, drift.UserId, cancellationToken);
				}
			}
		});

	public Result<DiscordRoleInfo> Find(string idOrName)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return new Error<string>("The bot is not connected to Discord.");
		}

		var found = ulong.TryParse(idOrName, out var id)
			? client.Guilds.Select(guild => guild.GetRole(id)).OfType<SocketRole>().ToList()
			: client.Guilds.SelectMany(guild => guild.Roles)
				.Where(role => string.Equals(role.Name, idOrName.TrimStart('@'), StringComparison.OrdinalIgnoreCase)).ToList();
		return found switch
		{
			[var role] => Info(role),
			[] => new Error<string>($"No Discord role called {idOrName} in a server the bot is in."),
			_ => new Error<string>($"More than one Discord role is called {idOrName}. Give its id instead: in Discord, turn on Developer Mode, then right-click the role and Copy Role ID."),
		};
	}

	public string? Problem(DiscordRoleMapping mapping)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return "the bot is not connected";
		}

		if (client.GetGuild(mapping.GuildId) is not { } guild)
		{
			return "the bot is not in that server";
		}

		return guild.GetRole(mapping.RoleId) is { } role ? Unmanageable(role) : "the Discord role was deleted";
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_daily = time.CreateTimer(_ => Queue(async cancellationToken =>
		{
			foreach (var guild in Guilds())
			{
				await PassAsync(guild, cancellationToken);
			}
		}), null, PassEvery, PassEvery);

		try
		{
			await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken))
			{
				try
				{
					await work(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException)
				{
					logger.LogWarning("[Discord] Could not update mapped roles: {Reason}", exception.Message);
				}
				catch (Exception exception)
				{
					// One member that fails must not stop the ones after it.
					logger.LogError(exception, "[Discord] Updating mapped roles failed.");
				}
			}
		}
		finally
		{
			_daily.Dispose();
		}
	}

	private bool Mapped(ulong guildId) => roles.Mappings().Any(mapping => mapping.GuildId == guildId);

	private IReadOnlyList<SocketGuild> Guilds()
		=> _client is { ConnectionState: ConnectionState.Connected } client
			? [.. client.Guilds.Where(guild => Mapped(guild.Id))]
			: [];

	private IReadOnlyList<DiscordRoleMapping> MappingsIn(SocketGuild guild)
		=> [.. roles.Mappings().Where(mapping => mapping.GuildId == guild.Id && Unmanageable(guild.GetRole(mapping.RoleId)) is null)];

	private async Task<IReadOnlySet<string>?> HeldAsync(ulong userId, CancellationToken cancellationToken)
		=> accounts.Get(userId) is { Linked: true } link ? await holders.HeldAsync(link.AccountId, cancellationToken) : null;

	private async Task ChangedAsync(SocketGuildUser member, CancellationToken cancellationToken)
	{
		var mappings = MappingsIn(member.Guild);
		if (member.IsBot || mappings.Count == 0)
		{
			return;
		}

		var decision = DiscordRolePlan.Changed(Member(member), mappings, await HeldAsync(member.Id, cancellationToken));
		await ApplyAsync(member.Guild, decision.Changes, "Roles held in the game");
		foreach (var mapping in mappings)
		{
			await roles.SettleAsync(mapping.RoleId, member.Id, cancellationToken);
		}
	}

	private async Task PassAsync(SocketGuild guild, CancellationToken cancellationToken)
	{
		var mappings = MappingsIn(guild);
		if (mappings.Count == 0)
		{
			return;
		}

		await guild.DownloadUsersAsync();
		foreach (var member in guild.Users.Where(member => !member.IsBot))
		{
			var linked = accounts.Get(member.Id) is { Linked: true };
			var decision = DiscordRolePlan.Pass(Member(member), mappings, await HeldAsync(member.Id, cancellationToken), linked,
				roleId => roles.Drifted(roleId, member.Id));
			await RecordAsync(decision.Drift, cancellationToken);
			await ApplyAsync(guild, decision.Changes, "Roles held in the game");
		}
	}

	private async Task ObservedAsync(SocketGuildUser member, CancellationToken cancellationToken)
	{
		var mappings = MappingsIn(member.Guild);
		if (member.IsBot || mappings.Count == 0)
		{
			return;
		}

		var decision = DiscordRolePlan.Observed(Member(member), mappings, await HeldAsync(member.Id, cancellationToken));
		await RecordAsync(decision.Drift, cancellationToken);
		await ApplyAsync(member.Guild, decision.Changes, "Enforced: roles held in the game");
	}

	private async Task RecordAsync(IReadOnlyList<DiscordDriftSeen> drift, CancellationToken cancellationToken)
	{
		foreach (var seen in drift)
		{
			if (!roles.Drifted(seen.RoleId, seen.UserId))
			{
				logger.LogInformation("[Discord] {Member} was {Change} a mapped role by hand.", seen.Name, seen.Added ? "given" : "stripped of");
			}

			await roles.RecordAsync(seen.RoleId, new DiscordDrift(seen.UserId, seen.Name, seen.Added, time.GetUtcNow()), cancellationToken);
		}
	}

	private static async Task ApplyAsync(SocketGuild guild, IReadOnlyList<DiscordRoleChange> changes, string reason)
	{
		foreach (var change in changes)
		{
			if (guild.GetUser(change.UserId) is not { } member)
			{
				continue;
			}

			var options = new RequestOptions { AuditLogReason = reason };
			if (change.Give)
			{
				await member.AddRoleAsync(change.RoleId, options);
			}
			else
			{
				await member.RemoveRoleAsync(change.RoleId, options);
			}
		}
	}

	private static DiscordMember Member(SocketGuildUser member)
		=> new(member.Id, member.DisplayName, member.Roles.Select(role => role.Id).ToHashSet());

	private static string? Unmanageable(SocketRole? role) => role switch
	{
		null => "the Discord role was deleted",
		_ when !role.Guild.CurrentUser.GuildPermissions.ManageRoles => "the bot lacks Manage Roles",
		_ when role.Position >= role.Guild.CurrentUser.Hierarchy => "the role is not below the bot's highest role",
		_ => null,
	};

	private DiscordRoleInfo Info(SocketRole role)
		=> new(role.Id, role.Guild.Id, role.Name, role.Guild.Name, role.Id == role.Guild.EveryoneRole.Id, role.IsManaged,
			accounts.Guild(role.Guild.Id)?.LinkedRoleId == role.Id, Unmanageable(role));

	private Task Queue(Func<CancellationToken, Task> work)
	{
		if (!_work.Writer.TryWrite(work))
		{
			logger.LogWarning("[Discord] {Count} role updates are waiting; one was dropped.", MaxWaiting);
		}

		return Task.CompletedTask;
	}
}
