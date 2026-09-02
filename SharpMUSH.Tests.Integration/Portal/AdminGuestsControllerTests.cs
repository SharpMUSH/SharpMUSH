using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;
using System.Security.Claims;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// That <c>api/admin/guests</c> — the panel that lets an admin stock a game with the guest
/// characters <c>connect guest</c> needs — creates real guests, lists them, and refuses a caller
/// who is not a wizard.
///
/// Driven directly rather than over HTTP for the same reason as
/// <see cref="ObjectsControllerPermissionTests"/>: the shared test host runs in Development, where
/// <c>DebugAuthenticationHandler</c> authenticates every request as an admin, so an
/// identity-dependent assertion made over HTTP would pass no matter what the controller did.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AdminGuestsControllerTests(ServerWebAppFactory factory)
{
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();

	private AdminGuestsController ControllerAs(DBRef actor) =>
		new(
			Mediator,
			factory.Services.GetRequiredService<IEngineCommandInvoker>(),
			factory.Services.GetRequiredService<IConnectionService>(),
			factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			factory.Services.GetRequiredService<IPasswordService>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(
						[new Claim(GameHub.CharacterDbrefClaim, actor.ToString())], "TestScheme"))
				}
			}
		};

	private async Task<DBRef> NewPlayerAsync(string prefix)
		=> await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, prefix);

	private async Task<DBRef> NewWizardAsync(string prefix)
	{
		var player = await NewPlayerAsync(prefix);
		var wizardFlag = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		var node = await Mediator.Send(new GetObjectNodeQuery(player));
		await Mediator.Send(new SetObjectFlagCommand(new AnySharpObject(node.AsPlayer), wizardFlag!));
		return player;
	}

	private static T Body<T>(IActionResult result)
	{
		var ok = result as OkObjectResult;
		if (ok is null) throw new InvalidOperationException($"expected 200, got {result.GetType().Name}");
		return (T)ok.Value!;
	}

	[Test]
	public async Task Create_MakesAPlayerCarryingTheGuestPower()
	{
		var wizard = await NewWizardAsync("GuestAdminWiz");
		var name = $"GuestA{Guid.NewGuid():N}"[..14];

		var created = await ControllerAs(wizard)
			.Create(new AdminGuestsController.CreateGuestRequest(name), CancellationToken.None);

		var row = Body<AdminGuestsController.GuestRow>(created);
		await Assert.That(row.Name).IsEqualTo(name);

		// The power is what `connect guest` actually selects on, so creating a player without it
		// would leave the panel reporting success while guest login stayed broken.
		var node = await Mediator.Send(new GetObjectNodeQuery(new DBRef(row.DbrefNumber, row.CreationTime)));
		await Assert.That(await node.AsPlayer.Object.HasPower("Guest")).IsTrue();
	}

	[Test]
	public async Task List_IncludesACreatedGuest()
	{
		var wizard = await NewWizardAsync("GuestAdminList");
		var name = $"GuestL{Guid.NewGuid():N}"[..14];

		await ControllerAs(wizard)
			.Create(new AdminGuestsController.CreateGuestRequest(name), CancellationToken.None);

		var listed = Body<AdminGuestsController.GuestListResponse>(
			await ControllerAs(wizard).List(CancellationToken.None));

		await Assert.That(listed.Guests.Any(g => g.Name == name)).IsTrue();
	}

	[Test]
	public async Task Create_AsANonWizard_IsRefused()
	{
		var mortal = await NewPlayerAsync("GuestAdminMortal");
		var name = $"GuestM{Guid.NewGuid():N}"[..14];

		var result = await ControllerAs(mortal)
			.Create(new AdminGuestsController.CreateGuestRequest(name), CancellationToken.None);

		await Assert.That(result).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);

		// And the refusal must not have created it anyway.
		var found = await Mediator.CreateStream(new GetPlayerQuery(name)).FirstOrDefaultAsync();
		await Assert.That(found).IsNull();
	}

	[Test]
	public async Task Delete_RemovesTheGuestFromTheDatabase()
	{
		var wizard = await NewWizardAsync("GuestAdminDel");
		var name = $"GuestD{Guid.NewGuid():N}"[..14];

		var row = Body<AdminGuestsController.GuestRow>(await ControllerAs(wizard)
			.Create(new AdminGuestsController.CreateGuestRequest(name), CancellationToken.None));

		var deleted = await ControllerAs(wizard).Delete(row.DbrefNumber, CancellationToken.None);
		await Assert.That(deleted).IsTypeOf<NoContentResult>();

		// PennMUSH destroys in two stages — @nuke marks GOING, and only a second pass frees the
		// object. A panel whose button says "delete" has to finish both, or the guest stays in the
		// database and `connect guest` keeps handing it out.
		var node = await Mediator.Send(new GetObjectNodeQuery(new DBRef(row.DbrefNumber, row.CreationTime)));
		await Assert.That(node.IsNone).IsTrue();
	}

	[Test]
	public async Task List_SuggestsAFreeNameForTheNextGuest()
	{
		var wizard = await NewWizardAsync("GuestAdminNext");

		var listed = Body<AdminGuestsController.GuestListResponse>(
			await ControllerAs(wizard).List(CancellationToken.None));

		// Whatever it suggests must not already be taken, or the create it pre-fills fails.
		var clash = await Mediator.CreateStream(new GetPlayerQuery(listed.NextFreeName)).FirstOrDefaultAsync();
		await Assert.That(clash).IsNull();
	}
}
