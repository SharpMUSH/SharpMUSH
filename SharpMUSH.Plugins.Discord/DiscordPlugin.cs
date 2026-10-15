using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Outbox;

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
		services.AddSingleton(provider =>
		{
			var secrets = provider.GetRequiredService<ISecretStore>();
			return new DiscordRelay(provider.GetRequiredService<IDiscordLinkStore>(), secrets,
				provider.GetRequiredService<DiscordGatewayService>(),
				link => new DiscordWebhookPoster(link.ChannelId, link.WebhookId, secrets),
				new DiscordOutboxOptions(), TimeProvider.System, provider.GetRequiredService<ILogger<DiscordRelay>>());
		});
		services.AddSingleton<IFeedLineListener>(provider => provider.GetRequiredService<DiscordRelay>());
		services.AddHostedService(provider => provider.GetRequiredService<DiscordRelay>());
	}

	public void MapEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("api/discord").RequireAuthorization();

		group.MapGet("setup", async (ClaimsPrincipal user, DiscordGatewayService gateway,
			IAdministrativeCapabilityService capabilities, CancellationToken cancellationToken)
			=> await MayAdministerAsync(user, capabilities, cancellationToken)
				? Results.Ok(DiscordSetup.View(gateway.State))
				: Results.Forbid());

		// After the token is set or replaced, or an intent turned on in the developer portal.
		group.MapPost("connect", async (ClaimsPrincipal user, DiscordGatewayService gateway,
			IAdministrativeCapabilityService capabilities, CancellationToken cancellationToken) =>
		{
			if (!await MayAdministerAsync(user, capabilities, cancellationToken))
			{
				return Results.Forbid();
			}

			gateway.Reconnect();
			return Results.Accepted();
		});
	}

	private static async Task<bool> MayAdministerAsync(ClaimsPrincipal user, IAdministrativeCapabilityService capabilities,
		CancellationToken cancellationToken)
		=> user.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } account
			&& await capabilities.AuthorizeAsync(new CapabilityActor(account), AdminPermission, cancellationToken);
}
