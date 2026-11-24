using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using System.Security.Claims;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// <c>api/admin/attribute-keys</c>: the portal's Attribute keys page lists the attribute table's keys and sets them through
/// <c>@attribute/lock</c>, answering a refusal with the command's own reason. Driven through the controller, as the
/// session's character, so the command's permission check is the one under test.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AttributeKeysControllerTests(ServerWebAppFactory factory)
{
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();
	private IAccountService Accounts => factory.Services.GetRequiredService<IAccountService>();

	private async Task<DBRef> WizardAsync(string prefix)
	{
		var wizard = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, prefix);
		var player = (await Mediator.Send(new GetObjectNodeQuery(wizard))).Expect<SharpPlayer>();
		await Mediator.Send(new SetObjectFlagCommand(player, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!));
		return wizard;
	}

	/// <summary>A new attribute table entry, made by <paramref name="wizard"/>.</summary>
	private async Task<string> EntryAsync(DBRef wizard)
	{
		var name = "APIKEY" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		await factory.Services.GetRequiredService<IEngineCommandInvoker>().InvokeAsync("@ATTRIBUTE", wizard,
			new Dictionary<string, CallState> { ["0"] = new(name), ["1"] = new("none") }, ["ACCESS"]);
		return name;
	}

	private async Task<AttributeKeysController> ControllerAs(DBRef actor)
	{
		var identity = (await Mediator.Send(new GetObjectNodeQuery(actor))).Expect<SharpPlayer>().Object.DBRef;
		var account = (await Accounts.CreateAccountAsync("keyapi_" + Guid.NewGuid().ToString("N"), null, "TestPassword123!"))
			.Expect<SharpAccount>();
		await Accounts.LinkCharacterAsync(account.Id!, identity);
		return new AttributeKeysController(
			Mediator,
			factory.Services.GetRequiredService<IEngineCommandInvoker>(),
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

	private static string? ReasonOf(IActionResult result) => (result as ObjectResult)?.Value is ApiErrorDto error ? error.Error : null;

	[Test]
	public async Task StaffSetAKeyAndTheListShowsIt()
	{
		var wizard = await WizardAsync("KeyApiWiz");
		var name = await EntryAsync(wizard);
		var controller = await ControllerAs(wizard);

		var set = await controller.SetKey(name.ToLowerInvariant(), "write", new SetAttributeKeyRequest("role^xpclerk"), CancellationToken.None);

		await Assert.That(StatusOf(set)).IsEqualTo(StatusCodes.Status204NoContent);
		var listed = (List<AttributeKeyEntryDto>)((OkObjectResult)await controller.List(CancellationToken.None)).Value!;
		await Assert.That(listed).Contains(new AttributeKeyEntryDto(name, null, "ROLE^xpclerk"));

		await controller.SetKey(name, "write", new SetAttributeKeyRequest(""), CancellationToken.None);
		listed = (List<AttributeKeyEntryDto>)((OkObjectResult)await controller.List(CancellationToken.None)).Value!;
		await Assert.That(listed.Select(entry => entry.Name)).DoesNotContain(name);
	}

	[Test]
	public async Task ARefusedKeySaysWhy()
	{
		var wizard = await WizardAsync("KeyApiBad");
		var name = await EntryAsync(wizard);
		var controller = await ControllerAs(wizard);

		var bad = await controller.SetKey(name, "read", new SetAttributeKeyRequest(")("), CancellationToken.None);
		await Assert.That(StatusOf(bad)).IsEqualTo(StatusCodes.Status400BadRequest);
		await Assert.That(ReasonOf(bad)).IsEqualTo("I don't understand that key.");

		var unknown = await controller.SetKey("NOSUCH" + Guid.NewGuid().ToString("N")[..8], "read",
			new SetAttributeKeyRequest("=#1"), CancellationToken.None);
		await Assert.That(StatusOf(unknown)).IsEqualTo(StatusCodes.Status404NotFound);
		await Assert.That(ReasonOf(unknown)).IsEqualTo("I don't know that attribute. Please use @attribute/access to create it, first.");

		var kind = await controller.SetKey(name, "examine", new SetAttributeKeyRequest("=#1"), CancellationToken.None);
		await Assert.That(StatusOf(kind)).IsEqualTo(StatusCodes.Status400BadRequest);
	}

	[Test]
	public async Task SomeoneWhoIsNotStaffIsRefusedByTheCommand()
	{
		var wizard = await WizardAsync("KeyApiOwner");
		var name = await EntryAsync(wizard);
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, "KeyApiPlayer");

		var refused = await (await ControllerAs(player)).SetKey(name, "write", new SetAttributeKeyRequest("=#1"), CancellationToken.None);

		await Assert.That(StatusOf(refused)).IsEqualTo(StatusCodes.Status403Forbidden);
		await Assert.That(ReasonOf(refused)).StartsWith("Permission denied.");
		await Assert.That((await Mediator.Send(new GetAttributeEntryQuery(name)))!.WriteLock).IsNull();
	}
}
