using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Secrets;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// <c>api/secrets</c>: <c>config.admin</c> manages every secret, a declaration's own permission manages that one, and
/// no answer carries a value.
/// </summary>
public class SecretsControllerTests
{
	private static readonly SecretDefinition Token = new("discord.token", "The Discord bot's token.", "discord.admin");

	private static (SecretsController Controller, ISecretStore Store) Create(params string[] granted)
	{
		var store = new SecretStore(new SecretStoreTests.InMemoryWorld(), new EphemeralDataProtectionProvider(),
			[SecretDefinition.SqlPassword, Token], new SecretStoreOptions(Path.Join(Path.GetTempPath(), $"no-secrets-{Guid.NewGuid():N}"), _ => null),
			new SecretStoreTests.FixedClock(DateTimeOffset.UnixEpoch), NullLogger<SecretStore>.Instance);

		var authorization = Substitute.For<IAuthorizationService>();
		authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<IEnumerable<IAuthorizationRequirement>>())
			.Returns(call => call.ArgAt<IEnumerable<IAuthorizationRequirement>>(2).OfType<PermissionRequirement>().All(r => granted.Contains(r.Scope))
				? AuthorizationResult.Success()
				: AuthorizationResult.Failed());

		var user = new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, "account-1"), new Claim(ClaimTypes.Name, "Grave")], "test"));
		var controller = new SecretsController(store, authorization, Substitute.For<IAuditLog>())
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
		};
		return (controller, store);
	}

	private static T Ok<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

	[Test]
	public async Task ConfigAdminSetsASecretAndTheAnswerHoldsNoValue()
	{
		var (controller, store) = Create(PortalPermission.ConfigAdmin);

		var set = await controller.Set("sql.password", new SecretSetRequest("hunter2"));
		var list = await controller.List();

		await Assert.That(Ok(set).SetBy).IsEqualTo(new SecretAuthor("account-1", "Grave"));
		await Assert.That(JsonSerializer.Serialize(Ok(set))).DoesNotContain("hunter2");
		await Assert.That(JsonSerializer.Serialize(Ok(list))).DoesNotContain("hunter2");
		await Assert.That((await store.GetAsync("sql.password")).Expect<string>()).IsEqualTo("hunter2");
	}

	[Test]
	public async Task ADeclaredPermissionManagesOnlyItsOwnSecret()
	{
		var (controller, _) = Create("discord.admin");

		var list = Ok(await controller.List());
		var token = await controller.Set("discord.token", new SecretSetRequest("token"));
		var sql = await controller.Set("sql.password", new SecretSetRequest("hunter2"));

		await Assert.That(list.Select(secret => secret.Name)).IsEquivalentTo(new[] { "discord.token" });
		await Assert.That(token.Result).IsTypeOf<OkObjectResult>();
		await Assert.That(sql.Result).IsTypeOf<ForbidResult>();
	}

	[Test]
	public async Task WithNoPermissionTheListIsForbidden()
	{
		var (controller, _) = Create();

		await Assert.That((await controller.List()).Result).IsTypeOf<ForbidResult>();
	}

	[Test]
	public async Task ClearingWhatIsNotStoredIsNotFound()
	{
		var (controller, _) = Create(PortalPermission.ConfigAdmin);

		await Assert.That((await controller.Clear("sql.password")).Result).IsTypeOf<NotFoundObjectResult>();
	}

	[Test]
	public async Task ConfigAdminNamesTheHostsASecretMayGoTo()
	{
		var (controller, store) = Create(PortalPermission.ConfigAdmin);

		var set = await controller.SetHosts("http.github.token", new SecretHostsRequest(["uploads.github.com", "API.github.com"]));
		var bad = await controller.SetHosts("http.github.token", new SecretHostsRequest(["not a host"]));

		await Assert.That(Ok(set).Hosts).IsEqualTo("api.github.com uploads.github.com");
		await Assert.That(bad.Result).IsTypeOf<BadRequestObjectResult>();
		await Assert.That(await store.HostsAsync("http.github.token")).IsEquivalentTo(new[] { "api.github.com", "uploads.github.com" });
	}

	[Test]
	public async Task HostsNeedThePermissionThatManagesTheSecret()
	{
		var (controller, store) = Create("discord.admin");

		var token = await controller.SetHosts("discord.token", new SecretHostsRequest(["discord.com"]));
		var other = await controller.SetHosts("http.github.token", new SecretHostsRequest(["api.github.com"]));

		await Assert.That(token.Result).IsTypeOf<OkObjectResult>();
		await Assert.That(other.Result).IsTypeOf<ForbidResult>();
		await Assert.That(await store.HostsAsync("http.github.token")).IsEmpty();
	}

	[Test]
	public async Task TheControllerNeedsASignedInUser()
		=> await Assert.That(Attribute.IsDefined(typeof(SecretsController), typeof(AuthorizeAttribute))).IsTrue();
}
