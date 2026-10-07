using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// The biography on a profile (board 25): a card titled "Biography", "Wiki page · edited …" under it,
/// History at the right, and the article body without its own kicker and title. A page placed
/// elsewhere by config is titled with the page's own title instead.
/// </summary>
public class WikiBodyBiographyTests : TrackingBunitContext
{
	private sealed class Handler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var path = request.RequestUri!.AbsolutePath;
			string? body = path switch
			{
				"/api/wiki/ns/character/Tomas%20Reyes" => Page("tomas_reyes", "Tomas Reyes", "character", Editable),
				"/api/wiki/ns/main/rules" => Page("rules", "House Rules", "main", Editable),
				"/api/wiki/ns/character/Home" => Page("home", "Home", "character", Editable),
				"/api/wiki/ns/character/Ada%20Locke" => Page("ada_locke", "Ada Locke", "character", edit: false, restricted: true),
				"/api/wiki/exists" => "{}",
				_ => null,
			};
			return Task.FromResult(body is null
				? new HttpResponseMessage(HttpStatusCode.NotFound)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
		}

		/// <summary>Whether the server tells this reader they may edit the unrestricted pages.</summary>
		public bool Editable { get; set; }

		private static string Json(bool value) => value ? "true" : "false";

		private static string Page(string slug, string title, string ns, bool edit, bool restricted = false) => $$"""
			{"id":"1","slug":"{{slug}}","title":"{{title}}","namespace":"{{ns}}","categories":[],
			 "markdownSource":"Lean and quiet.","renderedHtml":"<p>Lean and quiet.</p>","plainText":"Lean and quiet.",
			 "createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"{{DateTimeOffset.UtcNow.AddDays(-3):O}}",
			 "isRestricted":{{Json(restricted)}},"access":{"read":true,"edit":{{Json(edit)}},"delete":{{Json(edit)}},"manage":false},
			 "revisionNumber":2,"published":true,"lastEditedBy":"Tomas Reyes",
			 "locale":"en","requestedLocale":"en","availableLocales":["en"]}
			""";
	}

	private BunitAuthorizationContext Auth { get; }

	private Handler Server { get; } = new();

	public WikiBodyBiographyTests()
	{
		var client = Track(new HttpClient(Server) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new WikiService(factory, NullLogger<WikiService>.Instance))
			.AddSingleton<WikiMarkdigPipeline>()
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddLocalization();
		Auth = AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task OnAProfile_ItIsTheBiographyCard()
	{
		var cut = Render<CascadingWrapper>(p => p.AddChildContent<WikiBodyWidget>()
			.Add(x => x.Context, new ProfilePageContext("Tomas Reyes", false)));
		cut.WaitForAssertion(() => cut.Find(".wiki-body-card .kit-card-sub"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".wiki-body-card .kit-card-title").TextContent).IsEqualTo("Biography");
		await Assert.That(cut.Find(".wiki-body-card .kit-card-sub").TextContent).StartsWith("Wiki page · edited");
		await Assert.That(cut.Find(".wiki-body-card a.wiki-body-history").GetAttribute("href"))
			.IsEqualTo("/wiki/character/tomas_reyes/history");
		await Assert.That(cut.Find(".wiki-body-card").TextContent).Contains("Lean and quiet.");
		await Assert.That(cut.FindAll(".wiki-body-card .wiki-article-title").Count).IsEqualTo(0)
			.Because("the card header names it; a second title inside would repeat it");
	}

	[Test]
	public async Task ACharacterNamedHome_IsStillABiography()
	{
		// The home hero is the main wiki's home page, not any page whose slug happens to be "home".
		var cut = Render<CascadingWrapper>(p => p.AddChildContent<WikiBodyWidget>()
			.Add(x => x.Context, new ProfilePageContext("Home", false)));
		cut.WaitForAssertion(() => cut.Find(".wiki-body-card .kit-card-sub"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".wiki-body-card .kit-card-title").TextContent).IsEqualTo("Biography");
	}

	[Test]
	public async Task AConfiguredPage_IsTitledWithItsOwnTitle()
	{
		var config = JsonSerializer.SerializeToElement(new { slug = "rules" });
		var cut = Render<WikiBodyWidget>(p => p.Add(x => x.Config, config));
		cut.WaitForAssertion(() => cut.Find(".wiki-body-card .kit-card-sub"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".wiki-body-card .kit-card-title").TextContent).IsEqualTo("House Rules");
	}

	[Test]
	public async Task WithoutWikiEdit_ThereIsNoEditButton()
	{
		Auth.SetAuthorized("Tomas Reyes");
		var cut = RenderProfile();
		await Assert.That(cut.FindAll(".wiki-body-card .wiki-body-edit").Count).IsEqualTo(0)
			.Because("the server says this reader may not edit the page");
	}

	[Test]
	public async Task WithWikiEdit_EditOpensTheEditorInTheCard_AndDiscardReturns()
	{
		Auth.SetAuthorized("Tomas Reyes");
		Server.Editable = true;
		var cut = RenderProfile();

		await cut.Find(".wiki-body-card .wiki-body-edit").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".wiki-body-card .wiki-edit-textarea"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".wiki-body-card .wiki-edit-textarea").GetAttribute("value")).IsEqualTo("Lean and quiet.");
		await Assert.That(cut.FindAll(".wiki-body-card .wiki-body-edit").Count).IsEqualTo(0)
			.Because("the editor is open; a second click would throw the draft away");

		await cut.Find(".wiki-body-card .wiki-edit-cancel").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".wiki-body-card .wiki-body-edit"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".wiki-body-card .wiki-edit-textarea").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ARestrictedBiography_OffersEditOnlyWhenTheServerAllowsIt()
	{
		// The client holds wiki.edit, but the page's own requirements are the server's call: it answers edit:false.
		Auth.SetAuthorized("Tomas Reyes");
		Auth.SetPolicies("wiki.edit");
		Server.Editable = true;
		var cut = RenderProfile("Ada Locke");
		await Assert.That(cut.FindAll(".wiki-body-card .wiki-body-edit").Count).IsEqualTo(0)
			.Because("the server refuses this reader's save of the page");
	}

	private IRenderedComponent<CascadingWrapper> RenderProfile(string character = "Tomas Reyes")
	{
		var cut = Render<CascadingWrapper>(p => p.AddChildContent<WikiBodyWidget>()
			.Add(x => x.Context, new ProfilePageContext(character, false)));
		cut.WaitForAssertion(() => cut.Find(".wiki-body-card .kit-card-sub"), TimeSpan.FromSeconds(5));
		return cut;
	}

	/// <summary>Supplies the profile page context the way CharacterProfile does.</summary>
	public sealed class CascadingWrapper : Microsoft.AspNetCore.Components.ComponentBase
	{
		[Microsoft.AspNetCore.Components.Parameter] public ProfilePageContext? Context { get; set; }
		[Microsoft.AspNetCore.Components.Parameter] public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }

		protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
		{
			builder.OpenComponent<Microsoft.AspNetCore.Components.CascadingValue<ProfilePageContext?>>(0);
			builder.AddAttribute(1, "Value", Context);
			builder.AddAttribute(2, "ChildContent", ChildContent);
			builder.CloseComponent();
		}
	}
}
