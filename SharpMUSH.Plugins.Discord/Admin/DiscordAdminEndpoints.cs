using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Discord;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;
using SharpMUSH.Plugins.Discord.Outbox;
using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Plugins.Discord.Admin;

/// <summary>
/// The portal's <c>/admin/discord</c>: the connection, linked channels, role mappings and linked members. Every call
/// needs <see cref="DiscordPlugin.AdminPermission"/>; mapping a role also checks the role hierarchy, as
/// <c>@discord/map</c> does.
/// </summary>
public static class DiscordAdminEndpoints
{
	/// <summary>How long a call waits on Discord before it answers that Discord did not.</summary>
	private static readonly TimeSpan DiscordTimeout = TimeSpan.FromSeconds(30);

	public static void Map(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("api/discord/admin").RequireAuthorization().AddEndpointFilter(async (context, next) =>
		{
			var http = context.HttpContext;
			var capabilities = http.RequestServices.GetRequiredService<IAdministrativeCapabilityService>();
			return Account(http.User) is { } account
				&& await capabilities.AuthorizeAsync(new CapabilityActor(account), DiscordPlugin.AdminPermission, http.RequestAborted)
					? await next(context)
					: Results.Forbid();
		});

		group.MapGet("connection", (DiscordGatewayService gateway) => Connection(gateway.State));

		group.MapPost("connection", (DiscordGatewayService gateway) =>
		{
			gateway.Reconnect();
			return Answer("Connecting to Discord again with the stored token.", "ok");
		});

		group.MapGet("channels", (DiscordRelay relay, IDiscordChannelGate gate)
			=> relay.Links().Select(status => Channel(status, gate.OpenTo(status.Link.ChannelId))).ToList());

		group.MapGet("channels/available", (DiscordGatewayService gateway, IDiscordLinkStore links)
			=> gateway.TextChannels()
				.Select(channel => new DiscordTextChannelView(Id(channel.ChannelId), channel.Name, channel.GuildName, channel.Missing,
					links.Get(channel.ChannelId) is not null))
				.ToList());

		group.MapPost("channels", async (DiscordLinkChannelRequest request, ClaimsPrincipal user, DiscordRelay relay,
			IDiscordChannelGate gate, IFeedService feeds, CancellationToken cancellationToken) =>
		{
			if (Parse(request.ChannelId) is not { } channelId)
			{
				return Answer("That is not a Discord channel id.", "error");
			}

			using var timeout = Timeout(cancellationToken);
			var actor = Account(user)!;
			return await relay.LinkAsync(channelId, $"account:{actor}", new SecretAuthor(actor, user.Identity?.Name ?? actor), timeout.Token) switch
			{
				DiscordChannelLink link => await LinkedAsync(feeds, link, await gate.GateAsync(channelId, timeout.Token)),
				Error<string> error => Answer(error.Value, "error"),
			};
		});

		group.MapDelete("channels/{channelId}", async (string channelId, DiscordRelay relay, CancellationToken cancellationToken) =>
		{
			using var timeout = Timeout(cancellationToken);
			return Parse(channelId) is { } id && await relay.UnlinkAsync(id, timeout.Token) is DiscordChannelLink link
				? Answer($"Unlinked #{link.ChannelName}. Its feed discord/{link.FeedKey} stays, with its lines.", "ok")
				: Answer("That channel is not linked.", "warn");
		});

		group.MapPost("channels/{channelId}/resume", async (string channelId, DiscordRelay relay, CancellationToken cancellationToken)
			=> Parse(channelId) is { } id && await relay.ResumeAsync(id, cancellationToken) is DiscordChannelLink link
				? Answer($"#{link.ChannelName} posts again.", "ok")
				: Answer("That channel is not linked.", "warn"));

		group.MapPost("channels/{channelId}/gate", async (string channelId, IDiscordLinkStore links, IDiscordChannelGate gate,
			CancellationToken cancellationToken) =>
		{
			if (Parse(channelId) is not { } id || links.Get(id) is not { } link)
			{
				return Answer("That channel is not linked.", "warn");
			}

			using var timeout = Timeout(cancellationToken);
			return await gate.GateAsync(id, timeout.Token) is { } problem
				? Answer($"#{link.ChannelName} is still open to everyone: {problem}", "error")
				: Answer($"Only linked members may post in #{link.ChannelName} now.", "ok");
		});

		group.MapGet("roles", (DiscordRoleMaps maps, IDiscordRoleStore store) => Roles(maps, store));

		group.MapPost("roles", async (DiscordMapRequest request, ClaimsPrincipal user, DiscordRoleMaps maps,
			IAdministrativeCapabilityService capabilities, CancellationToken cancellationToken) =>
		{
			var actor = Account(user)!;
			var context = await capabilities.GetContextAsync(new CapabilityActor(actor), cancellationToken);
			return await maps.MapAsync(context, $"account:{actor}", request.Role, request.DiscordRole, request.Confirm, cancellationToken) switch
			{
				DiscordRoleMappingStatus { Problem: { } problem } status => Answer(
					$"{status.Mapping.RoleName} is mapped to {status.Mapping.Slug}, but nothing changes on Discord until it is fixed: {problem}.", "warn"),
				DiscordRoleMappingStatus status => Answer(
					$"{status.Mapping.RoleName} is mapped to {status.Mapping.Slug}. Linked members whose account holds {status.Mapping.Slug} get it, and anyone else loses it.", "ok"),
				Error<string> error => Answer(error.Value, "error"),
			};
		});

		group.MapDelete("roles/{roleId}", async (string roleId, DiscordRoleMaps maps, CancellationToken cancellationToken)
			=> await maps.UnmapAsync(roleId, cancellationToken) switch
			{
				DiscordRoleMapping mapping => Answer($"{mapping.RoleName} is no longer mapped to {mapping.Slug}. Members keep it as it is.", "ok"),
				Error<string> error => Answer(error.Value, "warn"),
			});

		group.MapPut("roles/{roleId}/mode", async (string roleId, DiscordModeRequest request, DiscordRoleMaps maps,
			CancellationToken cancellationToken) =>
		{
			if (!Enum.TryParse<DiscordRoleMode>(request.Mode, ignoreCase: true, out var mode))
			{
				return Answer("The mode is report or enforce.", "error");
			}

			return await maps.ModeAsync(roleId, mode, cancellationToken) switch
			{
				DiscordRoleMapping { Mode: DiscordRoleMode.Enforce } mapping => Answer(
					$"A change to {mapping.RoleName} made by hand on Discord is put back at once, and still listed.", "ok"),
				DiscordRoleMapping mapping => Answer(
					$"A change to {mapping.RoleName} made by hand on Discord is listed and left for you to decide.", "ok"),
				Error<string> error => Answer(error.Value, "warn"),
			};
		});

		// Puts the game's answer back for every member changed by hand, on one role or, with no id, on all.
		group.MapPost("roles/settle/{roleId?}", (string? roleId, DiscordRoleMaps maps, IDiscordRoles discord) =>
		{
			if (roleId is null)
			{
				discord.Settle(null);
				return Answer("Putting every role changed by hand back to the game's roles.", "ok");
			}

			if (maps.Lookup(roleId) is not { } mapping)
			{
				return Answer("That is not a mapped Discord role.", "warn");
			}

			discord.Settle(mapping.RoleId);
			return Answer($"Putting {mapping.RoleName} back to the game's roles wherever it was changed by hand.", "ok");
		});

		group.MapGet("members", async (string? search, IDiscordAccountStore store, IAccountService accounts,
			CancellationToken cancellationToken) =>
		{
			List<DiscordMemberView> members = [];
			foreach (var link in store.List().Where(link => link.Linked).OrderByDescending(link => link.LinkedAt))
			{
				var name = (await accounts.GetByIdAsync(link.AccountId, cancellationToken))?.Username;
				if (search is not { Length: > 0 } || Matches(search, link.Username, name, Id(link.UserId)))
				{
					members.Add(new DiscordMemberView(Id(link.UserId), link.Username, link.AccountId, name, link.LinkedAt));
				}
			}

			return members;
		});

		group.MapDelete("members/{userId}", async (string userId, DiscordAccounts accounts, CancellationToken cancellationToken)
			=> Parse(userId) is { } id && await accounts.UnlinkUserAsync(id, cancellationToken) is DiscordAccountLink link
				? Answer($"Unlinked {link.Username}. They lose the Linked role and every mapped role.", "ok")
				: Answer("That Discord user is not linked.", "warn"));
	}

	public static DiscordConnectionView Connection(DiscordGatewayState state)
	{
		var setup = DiscordSetup.View(state);
		return new DiscordConnectionView(JsonName(state.Status.ToString()), state.BotName, setup.InviteUrl, state.Guilds,
			setup.Steps.Select(step => new DiscordSetupStepView(step.Key, step.Title, step.Done, step.Todo, step.Link)).ToList());
	}

	public static DiscordChannelView Channel(DiscordLinkStatus status, IReadOnlyList<string>? openTo)
		=> new(Id(status.Link.ChannelId), status.Link.ChannelName, status.Link.GuildName,
			$"{DiscordChannelLink.FeedKind}/{status.Link.FeedKey}",
			status.State switch
			{
				DiscordLinkState.Faulted => "stopped",
				DiscordLinkState.Backlogged => "backlogged",
				_ => "ok",
			},
			status.Depth, status.Link.Fault?.Reason, openTo, status.Link.LinkedAt);

	public static DiscordRolesView Roles(DiscordRoleMaps maps, IDiscordRoleStore store)
	{
		var mappings = maps.List()
			.Select(status => new DiscordRoleMappingView(Id(status.Mapping.RoleId), status.Mapping.RoleName, status.Mapping.Slug,
				JsonName(status.Mapping.Mode.ToString()), status.Problem, status.Drift, status.Mapping.MappedAt))
			.ToList();
		var drift = store.Drift()
			.SelectMany(record => store.Mapping(record.RoleId) is { } mapping
				? record.Members.Select(member => new DiscordDriftView(Id(mapping.RoleId), mapping.RoleName, mapping.Slug,
					Id(member.UserId), member.UserName, member.Added, member.At))
				: [])
			.OrderBy(member => member.At)
			.ToList();
		return new DiscordRolesView(mappings, drift);
	}

	private static async Task<IResult> LinkedAsync(IFeedService feeds, DiscordChannelLink link, string? gateProblem)
	{
		var linked = $"Linked #{link.ChannelName} in {link.GuildName}. Lines sent on the feed discord/{link.FeedKey} are posted there.";
		if (await feeds.GetKindAsync(DiscordChannelLink.FeedKind) is Error<string>)
		{
			return Answer($"{linked} Install the discord package for the feed kind they are sent on.", "warn");
		}

		return gateProblem is null
			? Answer($"{linked} Only linked members may post there.", "ok")
			: Answer($"{linked} It is still open to everyone: {gateProblem} Fix that, then gate it again.", "warn");
	}

	private static bool Matches(string search, params string?[] values)
		=> values.Any(value => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

	private static IResult Answer(string message, string kind) => Results.Ok(new DiscordAdminAnswer(message, kind));

	private static CancellationTokenSource Timeout(CancellationToken cancellationToken)
	{
		var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(DiscordTimeout);
		return timeout;
	}

	private static string? Account(ClaimsPrincipal user)
		=> user.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } account ? account : null;

	private static ulong? Parse(string id)
		=> ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

	private static string Id(ulong id) => id.ToString(CultureInfo.InvariantCulture);

	/// <summary>An enum member's name as the views carry it: camel case.</summary>
	private static string JsonName(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
