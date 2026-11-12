using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// <c>api/built-documents</c>: the builder opens and saves the document a registered application keeps beside its route
/// on the <c>http_handler</c>, as the acting character. Each test registers an application of its own under a unique
/// route, so the shared handler's real routes are never written.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ApplicationDocumentsApiTests(ServerWebAppFactory factory)
{
	private const string Document = """
		{"kind":"view","schema_version":1,"pages":[{"key":"main","order":1,"sections":[{"name":"About","order":1,
		"elements":[{"kind":"field","key":"created","label":"Created","type":"text"}]}]}]}
		""";

	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();

	private DBRef Handler => new((int)factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.HttpHandler!.Value);

	private async Task<DBRef> NewPlayerAsync(string prefix, bool wizard = false)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, prefix);
		if (wizard)
		{
			var wizardFlag = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
			await Mediator.Send(new SetObjectFlagCommand((await Mediator.Send(new GetObjectNodeQuery(player))).Expect<AnySharpObject>(), wizardFlag!));
		}
		return player;
	}

	private async Task<ApplicationDocumentsController> ControllerAs(DBRef actor)
	{
		var identity = (await Mediator.Send(new GetObjectNodeQuery(actor))).Expect<SharpPlayer>().Object.DBRef;
		var accounts = factory.Services.GetRequiredService<IAccountService>();
		var account = await accounts.GetAccountForCharacterAsync(identity);
		if (account is null)
		{
			account = await accounts.CreateAccountAsync("docs_" + Guid.NewGuid().ToString("N"), null, "TestPassword123!") switch
			{
				SharpAccount created => created,
				Error<string> error => throw new InvalidOperationException($"Account creation failed: {error.Value}"),
			};
			await accounts.LinkCharacterAsync(account.Id!, identity);
		}
		var claims = new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, account.Id!), new Claim(GameHub.CharacterDbrefClaim, identity.ToString())], "TestScheme");
		return new ApplicationDocumentsController(
			Mediator,
			factory.Services.GetRequiredService<IAttributeService>(),
			factory.Services.GetRequiredService<IApplicationRegistryService>(),
			factory.Services.GetRequiredService<IPackageRegistryService>(),
			factory.Services.GetRequiredService<IPermissionService>(),
			factory.Services.GetRequiredService<IVisibleWorldProjection>(),
			factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>())
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(claims) } }
		};
	}

	/// <summary>A registered schema application served from <c>http/&lt;route&gt;/schema</c>, with its document kept beside the route.</summary>
	private async Task<(string Slug, string Attribute)> RegisterAsync(string? document = Document)
	{
		var route = TestIsolationHelpers.GenerateUniqueName("doc").ToLowerInvariant().Replace('_', '-');
		var attribute = $"DATA`{route.ToUpperInvariant()}`SCHEMA";
		if (document is not null)
		{
			var handler = (await Mediator.Send(new GetObjectNodeQuery(Handler))).Expect<AnySharpObject>();
			var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
			await factory.Services.GetRequiredService<IAttributeService>()
				.SetAttributeAsync(god, handler, attribute, MarkupString.MarkupText.Plain(document));
		}
		await factory.Services.GetRequiredService<IApplicationRegistryService>().UpsertApplicationAsync(new RegisteredApplication(
			route, "Document " + route, null, ApplicationKind.Page, $"http/{route}/schema?at={{path}}", null, null,
			PortalRole.Player, null, null, 0));
		return (route, attribute);
	}

	private static T Body<T>(IActionResult result) => result is OkObjectResult { Value: T value }
		? value
		: throw new InvalidOperationException($"Expected 200 with a {typeof(T).Name}, got {result}.");

	[Test]
	[Arguments("http/profile/schema", "DATA`PROFILE`SCHEMA")]
	[Arguments("/http/jobs/schema?at={path}", "DATA`JOBS`SCHEMA")]
	[Arguments("http/relationships/card/schema", "DATA`RELATIONSHIPS`CARD`SCHEMA")]
	[Arguments("api/built/mood/schema", null)]
	[Arguments("http/{path}/schema", null)]
	[Arguments("http//schema", null)]
	public async Task A_schema_route_keeps_its_document_beside_it(string url, string? attribute) =>
		await Assert.That(ApplicationDocumentsController.DocumentAttribute(url)).IsEqualTo(attribute);

	[Test]
	public async Task A_wizard_opens_and_saves_a_document_where_it_is_kept()
	{
		var (slug, attribute) = await RegisterAsync();
		var controller = await ControllerAs(await NewPlayerAsync("DocWizard", wizard: true));

		var listed = Body<List<ApplicationDocumentsController.StoredDocument>>(await controller.List(CancellationToken.None))
			.Single(document => document.Slug == slug);
		await Assert.That(listed.Attribute).IsEqualTo(attribute);
		await Assert.That(listed.Controls).IsTrue();
		await Assert.That(listed.Package).IsNull();

		var opened = Body<ApplicationDocumentsController.StoredDocumentSource>(await controller.Get(slug, CancellationToken.None));
		await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(opened.Schema), JsonNode.Parse(Document))).IsTrue();

		var changed = Document.Replace("\"About\"", "\"Who they are\"");
		var saved = Body<ApplicationDocumentsController.StoredDocumentSource>(
			await controller.Save(slug, new ApplicationDocumentsController.SaveRequest(changed), CancellationToken.None));
		await Assert.That(saved.Schema).IsEqualTo(changed);

		var handler = (await Mediator.Send(new GetObjectNodeQuery(Handler))).Expect<AnySharpObject>();
		var stored = await factory.Services.GetRequiredService<IAttributeService>()
			.GetAttributeAsync(handler, handler, attribute, IAttributeService.AttributeMode.Read, parent: false);
		await Assert.That(stored.Expect<SharpAttribute[]>().Last().Value.ToPlainText()).IsEqualTo(changed);
	}

	[Test]
	public async Task A_document_that_fails_the_schema_check_is_not_saved()
	{
		var (slug, _) = await RegisterAsync();
		var controller = await ControllerAs(await NewPlayerAsync("DocBad", wizard: true));

		var result = await controller.Save(slug, new ApplicationDocumentsController.SaveRequest("""{"kind":"nonsense"}"""), CancellationToken.None);

		await Assert.That(result).IsTypeOf<UnprocessableEntityObjectResult>();
		var opened = Body<ApplicationDocumentsController.StoredDocumentSource>(await controller.Get(slug, CancellationToken.None));
		await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(opened.Schema), JsonNode.Parse(Document))).IsTrue();
	}

	[Test]
	public async Task An_application_that_builds_its_document_in_softcode_is_not_offered()
	{
		var (slug, _) = await RegisterAsync(document: null);
		var controller = await ControllerAs(await NewPlayerAsync("DocNone", wizard: true));

		var listed = Body<List<ApplicationDocumentsController.StoredDocument>>(await controller.List(CancellationToken.None));

		await Assert.That(listed.Any(document => document.Slug == slug)).IsFalse();
		await Assert.That(await controller.Get(slug, CancellationToken.None)).IsTypeOf<NotFoundObjectResult>();
	}

	[Test]
	public async Task A_player_who_cannot_read_the_handler_can_neither_open_nor_save()
	{
		var (slug, _) = await RegisterAsync();
		var controller = await ControllerAs(await NewPlayerAsync("DocMortal"));

		await Assert.That(await controller.Get(slug, CancellationToken.None)).IsTypeOf<NotFoundObjectResult>();
		await Assert.That(await controller.Save(slug, new ApplicationDocumentsController.SaveRequest(Document), CancellationToken.None))
			.IsTypeOf<NotFoundObjectResult>();
	}

	[Test]
	public async Task The_character_header_opens_its_profile_document_from_the_profile_handler_package()
	{
		var controller = await ControllerAs(await NewPlayerAsync("DocProfile", wizard: true));

		var listed = Body<List<ApplicationDocumentsController.StoredDocument>>(await controller.List(CancellationToken.None))
			.Single(document => document.Slug == DefaultApplicationsBootstrapService.CharacterHeaderSlug);

		await Assert.That(listed.Attribute).IsEqualTo("DATA`PROFILE`SCHEMA");
		await Assert.That(listed.Package).IsEqualTo("profile-handler");
	}
}
