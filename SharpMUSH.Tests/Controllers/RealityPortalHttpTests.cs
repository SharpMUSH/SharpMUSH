using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Controllers;

public class RealityPortalHttpTests
{
	[Test]
	public async Task StateRouteRequiresAuthenticationAndFreshVisibleWorld()
	{
		var objects = new TestObjectFactory();
		var room = objects.CreateRoom(10, "Room metadata");
		var player = objects.CreatePlayer(11, "Viewer", room).Expect<SharpPlayer>();
		var actor = new CapabilityActor("account", player.Object.DBRef, player.Object.DBRef);
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGameActorAsync(player.Object.DBRef, Arg.Any<CancellationToken>()).Returns(actor);
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult<AnyOptionalSharpObject>(call.Arg<GetObjectNodeQuery>().DBRef == player.Object.DBRef ? player : new None()));
		mediator.Send(Arg.Any<GetLocationQuery>(), Arg.Any<CancellationToken>()).Returns(room);
		mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<AnySharpContent>());
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Services.AddAuthentication("reality-test").AddScheme<AuthenticationSchemeOptions, TestIdentity>("reality-test", _ => { });
		builder.Services.AddAuthorization();
		builder.Services.AddControllers().AddApplicationPart(typeof(GameStateController).Assembly);
		builder.Services.AddSingleton<IVisibleWorldProjection>(new VisibleWorldProjection(capabilities, mediator,
			Substitute.For<IPermissionService>(), reality, Substitute.For<IConnectionService>()));
		await using var app = builder.Build();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		await app.StartAsync();
		using var client = app.GetTestClient();

		using var anonymous = await client.GetAsync("/api/game/state");
		await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		client.DefaultRequestHeaders.Add("X-Test-Character", player.Object.DBRef.ToString());
		using var visible = await client.GetAsync("/api/game/state");
		await Assert.That(visible.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await visible.Content.ReadAsStringAsync()).Contains("Room metadata");

		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(false);
		using var hidden = await client.GetAsync("/api/game/state");
		await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(await hidden.Content.ReadAsStringAsync()).DoesNotContain("Room metadata");
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		capabilities.GetGameActorAsync(player.Object.DBRef, Arg.Any<CancellationToken>()).Returns((CapabilityActor?)null);
		using var revoked = await client.GetAsync("/api/game/state");
		await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	public sealed class TestIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
		UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-Character", out var character))
				return Task.FromResult(AuthenticateResult.NoResult());
			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.NameIdentifier, "account"),
				new Claim(GameHub.CharacterDbrefClaim, character.ToString())
			}, Scheme.Name);
			return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
		}
	}
}
