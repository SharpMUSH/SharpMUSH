using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// Portal controllers built to act as a chosen character. Over HTTP the shared test host authenticates
/// every request as God, who passes every lock, so a refusal path is only observable this way.
/// </summary>
public static class PortalControllers
{
	/// <summary>
	/// A principal for <paramref name="dbref"/> as the account session presents one: the account id
	/// and the acting character's objid, with the character linked to an account so that
	/// <see cref="IVisibleWorldProjection.ResolveCharacterAsync"/> accepts it.
	/// </summary>
	public static async Task<ClaimsIdentity> IdentityFor(ServerWebAppFactory factory, DBRef dbref)
	{
		var character = (await factory.Services.GetRequiredService<IMediator>().Send(new GetObjectNodeQuery(dbref)))
			.Expect<SharpPlayer>().Object.DBRef;
		var accounts = factory.Services.GetRequiredService<IAccountService>();
		var account = await accounts.GetAccountForCharacterAsync(character);
		if (account is null)
		{
			account = await accounts.CreateAccountAsync("portal_" + Guid.NewGuid().ToString("N"), null, "TestPassword123!") switch
			{
				SharpAccount created => created,
				Error<string> error => throw new InvalidOperationException($"Account creation failed: {error.Value}"),
			};
			await accounts.LinkCharacterAsync(account.Id!, character);
		}

		return new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, account.Id!), new Claim(GameHub.CharacterDbrefClaim, character.ToString())],
			"TestScheme");
	}

	/// <param name="ids">The line id source the controller bounds markers by; the host's own unless given.</param>
	public static async Task<CommController> CommControllerAs(ServerWebAppFactory factory, DBRef character,
		IChannelMessageIdSource? ids = null)
		=> CommControllerFor(factory, await IdentityFor(factory, character), ids);

	public static CommController CommControllerFor(ServerWebAppFactory factory, ClaimsIdentity identity,
		IChannelMessageIdSource? ids = null) =>
		new(
			factory.Services.GetRequiredService<IMediator>(),
			factory.Services.GetRequiredService<IChannelPermissionService>(),
			factory.Services.GetRequiredService<INotifyService>(),
			factory.Services.GetRequiredService<IVisibleWorldProjection>(),
			factory.Services.GetRequiredService<CommTextComposer>(),
			ids ?? factory.Services.GetRequiredService<IChannelMessageIdSource>(),
			factory.Services.GetRequiredService<IConnectionService>(),
			factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
			}
		};
}
