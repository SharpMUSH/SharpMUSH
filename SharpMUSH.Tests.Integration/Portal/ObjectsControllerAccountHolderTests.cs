using SharpMUSH.Library.ParserInterfaces;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;
using System.Security.Claims;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// <c>api/objects</c> on an account's holder (<c>#acct:&lt;key&gt;</c>, addressed by its reserved number), as the
/// Softcode Editor reaches it: the account's characters read it, staff write it, and nobody else sees it. Driven
/// through the controller for the reason <see cref="ObjectsControllerPermissionTests"/> gives.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ObjectsControllerAccountHolderTests(ServerWebAppFactory factory)
{
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();
	private IAccountService Accounts => factory.Services.GetRequiredService<IAccountService>();

	private record Cast(DBRef Character, DBRef Stranger, DBRef Wizard, DBRef Holder);

	/// <summary>A character on an account of its own, a character on another account, and a wizard.</summary>
	private async Task<Cast> CastAsync(string prefix)
	{
		var character = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, $"{prefix}Own");
		var stranger = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, $"{prefix}Other");
		var wizard = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, $"{prefix}Wiz");
		var wizardPlayer = (await Mediator.Send(new GetObjectNodeQuery(wizard))).Expect<SharpPlayer>();
		await Mediator.Send(new SetObjectFlagCommand(wizardPlayer, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!));

		var account = await AccountFor(character);
		await AccountFor(stranger);
		return new Cast(character, stranger, wizard, AccountHolders.ForAccount(account.Id)!.Value);
	}

	private async Task<SharpAccount> AccountFor(DBRef actor)
	{
		var identity = (await Mediator.Send(new GetObjectNodeQuery(actor))).Expect<SharpPlayer>().Object.DBRef;
		if (await Accounts.GetAccountForCharacterAsync(identity) is { } existing) return existing;
		var created = (await Accounts.CreateAccountAsync("objacct_" + Guid.NewGuid().ToString("N"), null, "TestPassword123!"))
			.Expect<SharpAccount>();
		await Accounts.LinkCharacterAsync(created.Id!, identity);
		return created;
	}

	private async Task<ObjectsController> ControllerAs(DBRef actor)
	{
		var identity = (await Mediator.Send(new GetObjectNodeQuery(actor))).Expect<SharpPlayer>().Object.DBRef;
		var account = await AccountFor(actor);
		return new ObjectsController(
			Mediator,
			factory.Services.GetRequiredService<IAttributeService>(),
			factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			factory.Services.GetRequiredService<IEngineCommandInvoker>(),
			factory.Services.GetRequiredService<IPermissionService>(),
			factory.Services.GetRequiredService<IRealityPolicy>(),
			factory.Services.GetRequiredService<IVisibleWorldProjection>(),
			factory.Services.GetRequiredService<IBooleanExpressionParser>(),
			factory.Services.GetRequiredService<ICommandOutputCapture>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(
						[new Claim(ClaimTypes.NameIdentifier, account.Id!), new Claim(GameHub.CharacterDbrefClaim, identity.ToString())],
						"TestScheme"))
				}
			}
		};
	}

	private static int? StatusOf(IActionResult result) => result switch
	{
		ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
		StatusCodeResult status => status.StatusCode,
		_ => null
	};

	[Test]
	public async Task TheAccountsCharacterSeesItsHolderAsAnAccount()
	{
		var cast = await CastAsync("ObjAcctSee");

		var result = await (await ControllerAs(cast.Character)).GetObject(cast.Holder.Number, CancellationToken.None);

		var summary = (ObjectSummaryDto)((OkObjectResult)result).Value!;
		await Assert.That(summary.Dbref).IsEqualTo(cast.Holder.ToString());
		await Assert.That(summary.Type).IsEqualTo("ACCOUNT");
		await Assert.That(summary.Name).IsEqualTo($"Account {cast.Holder.AccountNumber()}");
	}

	[Test]
	public async Task ACharacterOnAnotherAccountIsRefused()
	{
		var cast = await CastAsync("ObjAcctDeny");
		await (await ControllerAs(cast.Wizard)).SetAttribute(cast.Holder.Number, "XP", new SetAttributeRequest("12"), CancellationToken.None);
		var stranger = await ControllerAs(cast.Stranger);

		await Assert.That(StatusOf(await stranger.GetObject(cast.Holder.Number, CancellationToken.None)))
			.IsEqualTo(StatusCodes.Status403Forbidden);
		await Assert.That(StatusOf(await stranger.GetAttribute(cast.Holder.Number, "XP", CancellationToken.None)))
			.IsEqualTo(StatusCodes.Status403Forbidden);
		// A listing leaves out what the caller may not see, as it does on an object.
		var listed = await stranger.ListAttributes(cast.Holder.Number, null, CancellationToken.None);
		var names = listed is OkObjectResult { Value: List<AttributeDto> attributes } ? attributes.Select(attribute => attribute.Name) : [];
		await Assert.That(names).DoesNotContain("XP");
	}

	[Test]
	public async Task StaffWriteAndTheAccountsCharacterReads()
	{
		var cast = await CastAsync("ObjAcctWrite");

		var written = await (await ControllerAs(cast.Wizard)).SetAttribute(cast.Holder.Number, "XP",
			new SetAttributeRequest("12"), CancellationToken.None);
		await Assert.That(written).IsTypeOf<NoContentResult>();

		var own = await ControllerAs(cast.Character);
		var read = (AttributeDto)((OkObjectResult)await own.GetAttribute(cast.Holder.Number, "XP", CancellationToken.None)).Value!;
		await Assert.That(read.Value).IsEqualTo("12");
		var listed = (List<AttributeDto>)((OkObjectResult)await own.ListAttributes(cast.Holder.Number, null, CancellationToken.None)).Value!;
		await Assert.That(listed.Select(attribute => attribute.Name)).Contains("XP");
	}

	[Test]
	public async Task TheAccountsCharacterCannotWrite()
	{
		var cast = await CastAsync("ObjAcctNoWrite");
		var own = await ControllerAs(cast.Character);

		var refused = await own.SetAttribute(cast.Holder.Number, "XP", new SetAttributeRequest("9999"), CancellationToken.None);

		await Assert.That(StatusOf(refused)).IsEqualTo(StatusCodes.Status403Forbidden);
		await Assert.That(await own.GetAttribute(cast.Holder.Number, "XP", CancellationToken.None)).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task StaffClearAnAccountsAttribute()
	{
		var cast = await CastAsync("ObjAcctClear");
		var wizard = await ControllerAs(cast.Wizard);
		await wizard.SetAttribute(cast.Holder.Number, "NOTE", new SetAttributeRequest("temporary"), CancellationToken.None);

		await Assert.That(await wizard.ClearAttribute(cast.Holder.Number, "NOTE", CancellationToken.None)).IsTypeOf<NoContentResult>();
		await Assert.That(await wizard.GetAttribute(cast.Holder.Number, "NOTE", CancellationToken.None)).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task StaffSetAWriteKeyThatLetsTheCharacterWrite()
	{
		var cast = await CastAsync("ObjAcctKey");
		var wizard = await ControllerAs(cast.Wizard);
		await wizard.SetAttribute(cast.Holder.Number, "XP", new SetAttributeRequest("12"), CancellationToken.None);

		var set = await wizard.SetAttributeKey(cast.Holder.Number, "XP", "write", new SetAttributeKeyRequest($"={cast.Character}"),
			CancellationToken.None);
		await Assert.That(set).IsTypeOf<NoContentResult>();

		var seen = (AttributeDto)((OkObjectResult)await wizard.GetAttribute(cast.Holder.Number, "XP", CancellationToken.None)).Value!;
		await Assert.That(seen.WriteKey).IsNotNull();
		await Assert.That(seen.ReadKey).IsNull();

		var own = await ControllerAs(cast.Character);
		await Assert.That(await own.SetAttribute(cast.Holder.Number, "XP", new SetAttributeRequest("13"), CancellationToken.None))
			.IsTypeOf<NoContentResult>();
		var read = (AttributeDto)((OkObjectResult)await own.GetAttribute(cast.Holder.Number, "XP", CancellationToken.None)).Value!;
		await Assert.That(read.Value).IsEqualTo("13");
		// The account's character may examine its account, so the key comes with the value.
		await Assert.That(read.WriteKey).IsEqualTo(seen.WriteKey);

		var stranger = await ControllerAs(cast.Stranger);
		await Assert.That(StatusOf(await stranger.SetAttribute(cast.Holder.Number, "XP", new SetAttributeRequest("99"),
			CancellationToken.None))).IsEqualTo(StatusCodes.Status403Forbidden);
	}

	[Test]
	public async Task OnlyStaffChangeAnAccountsKeys()
	{
		var cast = await CastAsync("ObjAcctKeyDeny");
		await (await ControllerAs(cast.Wizard)).SetAttribute(cast.Holder.Number, "XP", new SetAttributeRequest("12"), CancellationToken.None);
		var own = await ControllerAs(cast.Character);

		var refused = await own.SetAttributeKey(cast.Holder.Number, "XP", "write", new SetAttributeKeyRequest($"={cast.Character}"),
			CancellationToken.None);

		await Assert.That(StatusOf(refused)).IsEqualTo(StatusCodes.Status403Forbidden);
		await Assert.That(StatusOf(await own.SetAttributeKey(cast.Holder.Number, "XP", "sideways", new SetAttributeKeyRequest(""),
			CancellationToken.None))).IsEqualTo(StatusCodes.Status400BadRequest);
	}

	[Test]
	public async Task ARefusedKeySaysWhy()
	{
		var cast = await CastAsync("ObjAcctKeyWhy");
		var wizard = await ControllerAs(cast.Wizard);
		await wizard.SetAttribute(cast.Holder.Number, "XP", new SetAttributeRequest("12"), CancellationToken.None);

		var garbled = await wizard.SetAttributeKey(cast.Holder.Number, "XP", "write", new SetAttributeKeyRequest("role^"),
			CancellationToken.None);
		var evaluated = await wizard.SetAttributeKey(cast.Holder.Number, "XP", "write", new SetAttributeKeyRequest("CHECK/1"),
			CancellationToken.None);

		await Assert.That(StatusOf(garbled)).IsEqualTo(StatusCodes.Status400BadRequest);
		await Assert.That(((ApiErrorDto)((ObjectResult)garbled).Value!).Error).IsEqualTo("I don't understand that key.");
		await Assert.That(StatusOf(evaluated)).IsEqualTo(StatusCodes.Status400BadRequest);
		await Assert.That(((ApiErrorDto)((ObjectResult)evaluated).Value!).Error)
			.IsEqualTo("An account runs no code, so its keys cannot hold an evaluation lock.");
	}

	[Test]
	public async Task AHolderWithNoAccountIsNotFound()
	{
		var cast = await CastAsync("ObjAcctNone");

		await Assert.That(await (await ControllerAs(cast.Wizard)).GetObject(AccountHolders.For(AccountHolders.MaxKey).Number, CancellationToken.None))
			.IsTypeOf<NotFoundResult>();
	}
}
