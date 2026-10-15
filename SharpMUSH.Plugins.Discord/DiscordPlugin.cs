using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Discord;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Admin;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;
using SharpMUSH.Plugins.Discord.Outbox;
using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Plugins.Discord;

/// <summary>
/// Links the game to Discord through a bot the game runs itself: logs the bot in, walks the admin through adding
/// it to a server, and posts the lines of each linked channel's feed there (<c>@discord</c>).
/// </summary>
[SharpPlugin]
public sealed class DiscordPlugin : PluginBase, IServiceRegistrar, IEndpointContributor
{
	/// <summary>Who runs the bot: its token, its setup, and later its links and mappings.</summary>
	public const string AdminPermission = "discord.admin";

	public static readonly SecretDefinition Token = new("discord.token",
		"The Discord bot's token, from the Bot page of its application in the Discord developer portal.", AdminPermission);

	public override string Id => "discord";

	public void RegisterServices(IServiceCollection services)
	{
		services.AddSingleton(Token);
		services.AddSingleton<DiscordGatewayService>();
		services.AddHostedService(provider => provider.GetRequiredService<DiscordGatewayService>());
		services.AddSingleton<IDiscordLinkStore, LightningDiscordLinkStore>();
		services.AddSingleton<DiscordMessages>();
		services.AddSingleton<IDiscordMessages>(provider => provider.GetRequiredService<DiscordMessages>());
		services.AddSingleton<IDiscordClientHandler>(provider => provider.GetRequiredService<DiscordMessages>());
		services.AddSingleton<IDiscordMessagePairs, LightningDiscordMessagePairs>();
		services.AddSingleton(provider =>
		{
			var secrets = provider.GetRequiredService<ISecretStore>();
			return new DiscordRelay(provider.GetRequiredService<IDiscordLinkStore>(), secrets,
				provider.GetRequiredService<DiscordGatewayService>(), provider.GetRequiredService<IDiscordMessagePairs>(),
				provider.GetRequiredService<IDiscordMessages>(),
				link => new DiscordWebhookPoster(link.ChannelId, link.WebhookId, secrets),
				new DiscordOutboxOptions(), TimeProvider.System, provider.GetRequiredService<ILogger<DiscordRelay>>());
		});
		services.AddSingleton<IFeedLineListener>(provider => provider.GetRequiredService<DiscordRelay>());
		services.AddHostedService(provider => provider.GetRequiredService<DiscordRelay>());

		services.AddSingleton<IDiscordAccountStore, LightningDiscordAccountStore>();
		services.AddSingleton<IDiscordSpeakerStore, LightningDiscordSpeakerStore>();
		services.AddSingleton(provider => new DiscordLinkTickets(TimeProvider.System));
		services.AddSingleton<IDiscordSettingsStore, LightningDiscordSettingsStore>();
		services.AddSingleton<IDiscordStandings, DiscordStandings>();
		services.AddSingleton<DiscordLinkedRole>();
		services.AddSingleton<IDiscordLinkedRole>(provider => new DiscordLinkedMember(
			provider.GetRequiredService<DiscordLinkedRole>(), provider.GetRequiredService<DiscordRoleSync>()));
		services.AddSingleton(provider => new DiscordAccounts(provider.GetRequiredService<IDiscordAccountStore>(),
			provider.GetRequiredService<IDiscordSpeakerStore>(), provider.GetRequiredService<DiscordLinkTickets>(),
			provider.GetRequiredService<IAccountService>(), provider.GetRequiredService<IDiscordLinkedRole>(), TimeProvider.System,
			provider.GetRequiredService<ILogger<DiscordAccounts>>()));
		services.AddSingleton<IDiscordChannelGate>(provider => provider.GetRequiredService<DiscordLinkedRole>());
		services.AddSingleton<DiscordSlashCommands>();
		services.AddSingleton<IDiscordClientHandler>(provider => provider.GetRequiredService<DiscordLinkedRole>());
		services.AddSingleton<IDiscordClientHandler>(provider => provider.GetRequiredService<DiscordSlashCommands>());
		services.AddSingleton<IDiscordBanStore, LightningDiscordBanStore>();
		services.AddSingleton(provider => new DiscordStandingSync(provider.GetRequiredService<IDiscordAccountStore>(),
			provider.GetRequiredService<IDiscordBanStore>(), provider.GetRequiredService<IDiscordSettingsStore>(),
			provider.GetRequiredService<IDiscordStandings>(), provider.GetRequiredService<DiscordLinkedRole>(), TimeProvider.System,
			provider.GetRequiredService<ILogger<DiscordStandingSync>>()));
		services.AddSingleton<IDiscordClientHandler>(provider => provider.GetRequiredService<DiscordStandingSync>());
		services.AddSingleton<IAccountChangeListener>(provider => provider.GetRequiredService<DiscordStandingSync>());

		services.AddSingleton<DiscordIdentities>();
		services.AddSingleton<IDiscordFeedInput, DiscordFeedInput>();
		services.AddSingleton<DiscordInbound>();
		services.AddSingleton<DiscordInboundHandler>();
		services.AddSingleton<IDiscordClientHandler>(provider => provider.GetRequiredService<DiscordInboundHandler>());
		services.AddHostedService(provider => provider.GetRequiredService<DiscordInboundHandler>());

		services.AddSingleton<IDiscordRoleStore, LightningDiscordRoleStore>();
		services.AddSingleton<IDiscordRoleHolders, DiscordRoleHolders>();
		services.AddSingleton(provider => new DiscordRoleSync(provider.GetRequiredService<IDiscordRoleStore>(),
			provider.GetRequiredService<IDiscordAccountStore>(), provider.GetRequiredService<IDiscordRoleHolders>(), TimeProvider.System,
			provider.GetRequiredService<ILogger<DiscordRoleSync>>()));
		services.AddSingleton<IDiscordRoles>(provider => provider.GetRequiredService<DiscordRoleSync>());
		services.AddSingleton<IDiscordClientHandler>(provider => provider.GetRequiredService<DiscordRoleSync>());
		services.AddSingleton<IAccountChangeListener>(provider => provider.GetRequiredService<DiscordRoleSync>());
		services.AddHostedService(provider => provider.GetRequiredService<DiscordRoleSync>());
		services.AddSingleton(provider => new DiscordRoleMaps(provider.GetRequiredService<IDiscordRoleStore>(),
			provider.GetRequiredService<IDiscordRoles>(), provider, TimeProvider.System));
	}

	public void MapEndpoints(IEndpointRouteBuilder endpoints)
	{
		DiscordAdminEndpoints.Map(endpoints);
		var group = endpoints.MapGroup("api/discord").RequireAuthorization();

		// A member confirming the ticket /link gave them in Discord, while signed in to their game account.
		group.MapGet("link/{ticket}", (string ticket, DiscordAccounts accounts)
			=> accounts.Ticket(ticket) is { } found
				? Results.Ok(new DiscordLinkTicketView(found.User.Username, found.User.AvatarUrl, found.ExpiresAt))
				: Results.NotFound("That link has expired or was already used. Type /link in Discord for a new one."));

		group.MapPost("link/{ticket}", async (string ticket, ClaimsPrincipal user, DiscordAccounts accounts,
			CancellationToken cancellationToken) =>
		{
			if (Account(user) is not { } account)
			{
				return Results.Forbid();
			}

			return await accounts.ConfirmAsync(ticket, account, cancellationToken) switch
			{
				DiscordAccountLink link => Results.Ok(View(link)),
				Error<string> error => Results.Conflict(error.Value),
			};
		});

		group.MapGet("account", (ClaimsPrincipal user, DiscordAccounts accounts)
			=> Account(user) is { } account ? Results.Ok(View(accounts.ForAccount(account))) : Results.Forbid());

		group.MapDelete("account", async (ClaimsPrincipal user, DiscordAccounts accounts, CancellationToken cancellationToken) =>
		{
			if (Account(user) is not { } account)
			{
				return Results.Forbid();
			}

			return await accounts.UnlinkAccountAsync(account, cancellationToken) switch
			{
				DiscordAccountLink => Results.Ok(View(null)),
				_ => Results.NotFound("This account is not linked to Discord."),
			};
		});
	}

	private static string? Account(ClaimsPrincipal user)
		=> user.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } account ? account : null;

	private static DiscordAccountView View(DiscordAccountLink? link) => new(link?.Username, link?.LinkedAt);
}
