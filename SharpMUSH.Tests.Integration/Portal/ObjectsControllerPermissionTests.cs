using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;
using System.Security.Claims;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// That <c>api/objects</c> enforces MUSH permissions rather than merely being authenticated.
///
/// These drive <see cref="ObjectsController"/> directly instead of over HTTP, because the shared
/// test host runs in Development, where <c>DebugAuthenticationHandler</c> is the default scheme and
/// authenticates every request — including anonymous ones — as an admin. Over HTTP every caller
/// would be the same privileged identity, so an identity-dependent assertion there would pass no
/// matter what the controller did. Constructing the controller lets the test choose the acting
/// character while still exercising the real controller code against the real engine services.
///
/// The bypass this guards against is concrete: <c>PackageInstallService</c> legitimately writes
/// attributes straight to <c>ISharpDatabase</c> as a wizard, skipping permission checks. If someone
/// ever reaches for that shortcut here, these tests fail.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ObjectsControllerPermissionTests(ServerWebAppFactory factory)
{
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => factory.Services.GetRequiredService<IAttributeService>();

	/// <summary>A controller acting as <paramref name="actor"/>, wired exactly as DI would build it.</summary>
	private async Task<ObjectsController> ControllerAs(DBRef actor)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(actor))).Expect<SharpPlayer>();
		var fullIdentity = player.Object.DBRef;
		var accounts = factory.Services.GetRequiredService<IAccountService>();
		var account = await accounts.GetAccountForCharacterAsync(fullIdentity);
		if (account is null)
		{
			account = await accounts.CreateAccountAsync("objapi_" + Guid.NewGuid().ToString("N"), null, "TestPassword123!") switch
			{
				SharpAccount created => created,
				Error<string> error => throw new InvalidOperationException($"Account creation failed: {error.Value}"),
			};
			await accounts.LinkCharacterAsync(account.Id!, fullIdentity);
		}
		return ControllerFor(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, account.Id!), new Claim(GameHub.CharacterDbrefClaim, fullIdentity.ToString())], "TestScheme"));
	}

	private ObjectsController ControllerFor(ClaimsIdentity identity) =>
		new(
			Mediator,
			AttributeService,
			factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			factory.Services.GetRequiredService<IEngineCommandInvoker>(),
			factory.Services.GetRequiredService<IPermissionService>(),
			factory.Services.GetRequiredService<IRealityPolicy>(),
			factory.Services.GetRequiredService<IVisibleWorldProjection>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
			}
		};

	private async Task<DBRef> NewPlayerAsync(string prefix)
		=> await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, prefix);

	[Test]
	public async Task SetAttribute_OnAnObjectTheActorDoesNotControl_IsRefused()
	{
		var intruder = await NewPlayerAsync("ObjApiIntruder");
		var victim = await NewPlayerAsync("ObjApiVictim");

		var result = await (await ControllerAs(intruder)).SetAttribute(
			victim.Number, "PWNED", new SetAttributeRequest("intruder was here"), CancellationToken.None);

		await Assert.That(result).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
	}

	[Test]
	public async Task SetAttribute_RefusedWrite_LeavesNothingBehind()
	{
		var intruder = await NewPlayerAsync("ObjApiNoWrite");
		var victim = await NewPlayerAsync("ObjApiUntouched");

		await (await ControllerAs(intruder)).SetAttribute(
			victim.Number, "PWNED", new SetAttributeRequest("intruder was here"), CancellationToken.None);

		// Read back as the victim, who would be allowed to see it if it existed.
		var read = await (await ControllerAs(victim)).GetAttribute(victim.Number, "PWNED", CancellationToken.None);

		await Assert.That(read).IsTypeOf<NotFoundResult>()
			.Because("a refused write must not reach the database");
	}

	[Test]
	public async Task SetAttribute_OnTheActorsOwnObject_Succeeds()
	{
		var actor = await NewPlayerAsync("ObjApiSelf");

		var result = await (await ControllerAs(actor)).SetAttribute(
			actor.Number, "SELF_SET", new SetAttributeRequest("line one\nline two"), CancellationToken.None);

		await Assert.That(result).IsTypeOf<NoContentResult>()
			.Because("the refusal above must come from permissions, not from the endpoint refusing everything");
	}

	[Test]
	public async Task ClearAttribute_OnAnObjectTheActorDoesNotControl_IsRefused()
	{
		var owner = await NewPlayerAsync("ObjApiClearOwner");
		var intruder = await NewPlayerAsync("ObjApiClearIntruder");

		await (await ControllerAs(owner)).SetAttribute(
			owner.Number, "KEEP_ME", new SetAttributeRequest("still here"), CancellationToken.None);

		var attempt = await (await ControllerAs(intruder)).ClearAttribute(
			owner.Number, "KEEP_ME", CancellationToken.None);

		await Assert.That(attempt).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)attempt).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);

		var stillThere = await (await ControllerAs(owner)).GetAttribute(owner.Number, "KEEP_ME", CancellationToken.None);
		await Assert.That(stillThere).IsTypeOf<OkObjectResult>();
	}

	/// <summary>
	/// Reads are gated too, not just writes. An attribute the actor may not see must not come back
	/// through the API just because it can be addressed by dbref.
	/// </summary>
	[Test]
	public async Task GetAttribute_OnAnObjectTheActorCannotSee_DoesNotReturnTheValue()
	{
		var owner = await NewPlayerAsync("ObjApiReadOwner");
		var snooper = await NewPlayerAsync("ObjApiSnooper");

		await (await ControllerAs(owner)).SetAttribute(
			owner.Number, "PRIVATE_NOTE", new SetAttributeRequest("for my eyes only"), CancellationToken.None);

		var result = await (await ControllerAs(snooper)).GetAttribute(owner.Number, "PRIVATE_NOTE", CancellationToken.None);

		var leaked = result is OkObjectResult { Value: AttributeDto dto } && dto.Value.Contains("eyes only");
		await Assert.That(leaked).IsFalse()
			.Because("a read the engine refuses must not surface the stored value");
	}

	/// <summary>
	/// The object summary carries name, owner and flags, so it needs the same visibility check the
	/// attribute endpoints get for free from <see cref="IAttributeService"/>.
	/// </summary>
	[Test]
	public async Task GetObject_OnAnObjectTheActorCannotExamine_IsRefused()
	{
		var owner = await NewPlayerAsync("ObjApiExamineOwner");
		var snooper = await NewPlayerAsync("ObjApiExamineSnooper");

		var permissions = factory.Services.GetRequiredService<IPermissionService>();
		var ownerObject = (await Mediator.Send(new GetObjectNodeQuery(owner))).Expect<AnySharpObject>();
		var snooperObject = (await Mediator.Send(new GetObjectNodeQuery(snooper))).Expect<AnySharpObject>();

		// Only meaningful while the engine actually refuses this pairing.
		var mayExamine = await permissions.CanExamine(snooperObject, ownerObject);
		await Assert.That(mayExamine).IsFalse()
			.Because("two unrelated mortals must not be able to examine each other, or this test proves nothing");

		var result = await (await ControllerAs(snooper)).GetObject(owner.Number, CancellationToken.None);

		await Assert.That(result).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
	}

	[Test]
	public async Task NoCharacterClaim_IsUnauthorized()
	{
		var victim = await NewPlayerAsync("ObjApiNoClaim");

		var result = await ControllerFor(new ClaimsIdentity()).GetObject(victim.Number, CancellationToken.None);

		await Assert.That(result).IsTypeOf<UnauthorizedResult>();
	}
}
