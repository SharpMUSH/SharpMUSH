using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
using System.Text.Json.Nodes;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// The application builder's reads on <c>api/built</c>: the apps an object holds, and what a set of attributes holds
/// now, both as the acting character. The controller is built with that character's claims, since the test host's
/// HTTP requests all act as God.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class BuiltApplicationsApiTests(ServerWebAppFactory factory)
{
	private const string Schema = """
		{"kind":"view","schema_version":1,"title":"Mood board","pages":[{"key":"page1","title":"Page 1","order":1,
		"sections":[{"name":"","order":1,"elements":[{"kind":"keyvalue","fields":["mood"]}]}]}]}
		""";

	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();

	private Task<DBRef> NewPlayerAsync(string prefix) => TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, prefix);

	private async Task<BuiltApplicationsController> ControllerAs(DBRef actor)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(actor))).Expect<SharpPlayer>();
		var identity = player.Object.DBRef;
		var accounts = factory.Services.GetRequiredService<IAccountService>();
		var account = await accounts.GetAccountForCharacterAsync(identity);
		if (account is null)
		{
			account = await accounts.CreateAccountAsync("built_" + Guid.NewGuid().ToString("N"), null, "TestPassword123!") switch
			{
				SharpAccount created => created,
				Error<string> error => throw new InvalidOperationException($"Account creation failed: {error.Value}"),
			};
			await accounts.LinkCharacterAsync(account.Id!, identity);
		}
		var claims = new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, account.Id!), new Claim(GameHub.CharacterDbrefClaim, identity.ToString())], "TestScheme");
		return new BuiltApplicationsController(
			Mediator,
			factory.Services.GetRequiredService<IAttributeService>(),
			factory.Services.GetRequiredService<IApplicationRegistryService>(),
			factory.Services.GetRequiredService<IRealityPolicy>(),
			factory.Services.GetRequiredService<IVisibleWorldProjection>(),
			factory.Services.GetRequiredService<IPermissionService>())
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(claims) } }
		};
	}

	private static T Body<T>(IActionResult result) => result is OkObjectResult { Value: T value }
		? value
		: throw new InvalidOperationException($"Expected 200 with a {typeof(T).Name}, got {result}.");

	[Test]
	public async Task The_shelf_lists_what_the_character_holds_and_says_they_control_it()
	{
		var actor = await NewPlayerAsync("BuiltShelf");
		var controller = await ControllerAs(actor);
		var saved = await controller.SaveSource("mood-board",
			new BuiltApplicationsController.SaveRequest("me", Schema, """{"mood":"PROFILE`MOOD"}""", null, null), CancellationToken.None);
		await Assert.That(saved).IsTypeOf<OkObjectResult>();

		var shelf = Body<BuiltApplicationsController.BuiltShelf>(await controller.Shelf(null, CancellationToken.None));

		await Assert.That(shelf.Controls).IsTrue();
		await Assert.That(shelf.Holder).StartsWith($"#{actor.Number}:");
		var app = shelf.Apps.Single();
		await Assert.That(app.Slug).IsEqualTo("mood-board");
		await Assert.That(app.Title).IsEqualTo("Mood board");
		await Assert.That(app.Kind).IsEqualTo("view");
		await Assert.That(app.Parts).IsEquivalentTo(["SOURCES"]);
	}

	[Test]
	public async Task Another_players_character_is_not_shown_as_controlled()
	{
		var owner = await NewPlayerAsync("BuiltOwner");
		var other = await NewPlayerAsync("BuiltOther");

		var result = await (await ControllerAs(other)).Shelf($"#{owner.Number}", CancellationToken.None);

		// Both start in the default home, so each can see the other.
		await Assert.That(Body<BuiltApplicationsController.BuiltShelf>(result).Controls).IsFalse();
	}

	[Test]
	public async Task Values_reads_the_callers_attributes_and_hides_what_is_not_an_attribute()
	{
		var actor = await NewPlayerAsync("BuiltValues");
		var controller = await ControllerAs(actor);
		var player = (await Mediator.Send(new GetObjectNodeQuery(actor))).Expect<SharpPlayer>();
		var mood = $"calm {Guid.NewGuid():N}";
		await factory.Services.GetRequiredService<IAttributeService>()
			.SetAttributeAsync(player, player, "PROFILE`MOOD", MarkupString.MarkupText.Plain(mood));

		var fields = (JsonObject)Body<JsonObject>(await controller.Values(new BuiltApplicationsController.ValuesRequest(null,
			new Dictionary<string, string>
			{
				["mood"] = "PROFILE`MOOD",
				["absent"] = "PROFILE`ABSENT",
				["bad"] = "not an attribute!",
			}), CancellationToken.None))["fields"]!;

		await Assert.That((string?)fields["mood"]!["value"]).IsEqualTo(mood);
		await Assert.That((bool?)fields["mood"]!["visible"]).IsTrue();
		await Assert.That((string?)fields["absent"]!["value"]).IsEqualTo(string.Empty);
		await Assert.That((bool?)fields["absent"]!["visible"]).IsTrue();
		await Assert.That((bool?)fields["bad"]!["visible"]).IsFalse();
	}
}
