using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// HttpMessageHandler that routes wiki API calls directly to an InMemoryWikiService.
/// This gives tests a fully working WikiService without a real server or stub 404s.
/// </summary>
file sealed class InMemoryWikiHandler(IWikiService wikiService) : HttpMessageHandler
{
	private record CreateReq(string Title, string Markdown, string? Namespace, string? Category);
	private record UpdateReq(string Markdown, string? EditSummary);
	private record ExistsReq(string[] Refs);

	private static readonly Regex _slugRoute = new(@"^api/wiki/([^/]+)$", RegexOptions.Compiled);
	private static readonly Regex _nsRoute = new(@"^api/wiki/ns/([^/]+)/([^/]+)/([^/]+)$", RegexOptions.Compiled);

	protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Get &&
				_nsRoute.Match(path) is { Success: true } getMatch)
		{
			var ns = ParseNs(Uri.UnescapeDataString(getMatch.Groups[1].Value));
			var category = Uri.UnescapeDataString(getMatch.Groups[2].Value);
			var slug = Uri.UnescapeDataString(getMatch.Groups[3].Value);
			var result = await wikiService.GetBySlugAsync(slug, category, ns);
			return result.Match(
					page => Json(ToDto(page)),
					_ => new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		// Refs use URL-path form: "ns/category/slug".
		if (request.Method == HttpMethod.Post && path == "api/wiki/exists")
		{
			var req = await request.Content!.ReadFromJsonAsync<ExistsReq>(cancellationToken: cancellationToken);
			if (req is null) return new HttpResponseMessage(HttpStatusCode.BadRequest);

			var map = new Dictionary<string, bool>();
			foreach (var reference in req.Refs)
			{
				var (ns, category, slug) = ParseRef(reference);
				var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
				map[reference] = lookup.IsT0;
			}

			return Json(map);
		}

		if (request.Method == HttpMethod.Post && path == "api/wiki")
		{
			var req = await request.Content!.ReadFromJsonAsync<CreateReq>(cancellationToken: cancellationToken);
			if (req is null) return new HttpResponseMessage(HttpStatusCode.BadRequest);
			var result = await wikiService.CreateAsync(req.Title, req.Markdown, "#1", ParseNs(req.Namespace), req.Category);
			return result.Match(
					page => Json(ToDto(page), HttpStatusCode.Created),
					_ => new HttpResponseMessage(HttpStatusCode.Conflict));
		}

		if (request.Method == HttpMethod.Put &&
				_slugRoute.Match(path) is { Success: true } putMatch)
		{
			var id = Uri.UnescapeDataString(putMatch.Groups[1].Value);
			var req = await request.Content!.ReadFromJsonAsync<UpdateReq>(cancellationToken: cancellationToken);
			if (req is null) return new HttpResponseMessage(HttpStatusCode.BadRequest);
			var result = await wikiService.UpdateAsync(id, req.Markdown, "#1", req.EditSummary);
			return result.Match(
					page => Json(ToDto(page)),
					_ => new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		return new HttpResponseMessage(HttpStatusCode.NotFound);
	}

	private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
			new(status) { Content = JsonContent.Create(value) };

	private static WikiController.WikiPageDto ToDto(WikiPage p) => new(
			p.Id, p.Slug, p.Title, p.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
			p.CreatedAt, p.UpdatedAt, p.IsProtected, p.RevisionNumber,
			p.Category, p.Tags, p.Published);

	private static WikiNamespace ParseNs(string? ns) =>
			Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var r) ? r : WikiNamespace.Main;

	private static (WikiNamespace Ns, string Category, string Slug) ParseRef(string reference)
	{
		var parts = reference.Split('/');
		return parts.Length switch
		{
			>= 3 => (ParseNs(parts[0]), parts[1], string.Join('/', parts[2..])),
			2 => (ParseNs(parts[0]), "general", parts[1]),
			_ => (WikiNamespace.Main, "general", reference)
		};
	}
}

/// <summary>Helper to register the full wiki service stack needed for client component tests.</summary>
file static class WikiServiceSetup
{
	/// <summary>Returns the authorization context so a test can grant itself policies.</summary>
	public static BunitAuthorizationContext AddWikiTestServices(this TrackingBunitContext ctx)
	{
		var auth = ctx.AddAuthorization();

		// One InMemoryWikiService instance shared between the handler and the test
		// so tests can seed pages directly via IWikiService and have WikiService
		// (the HTTP client wrapper) read them back through the same data store.
		var wikiSvc = new InMemoryWikiService(new WikiMarkdigPipeline());

		var apiClient = ctx.Track(new HttpClient(new InMemoryWikiHandler(wikiSvc))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		});

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		ctx.Services
				// Register the client so the DI container owns and disposes it at teardown.
				.AddSingleton(apiClient)
				.AddMudServices()
				.AddSingleton<WikiMarkdigPipeline>()
				.AddSingleton<IWikiService>(wikiSvc)
				.AddSingleton(factory)
				.AddSingleton(sp => new WikiService(
						sp.GetRequiredService<IHttpClientFactory>(),
						NullLogger<WikiService>.Instance))
				// CharacterProfile composes the profile/gallery widgets, which inject these.
				// Their reads hit the in-memory handler's 404 fallback and degrade gracefully.
				.AddSingleton(sp => new SchemaAppService(
						sp.GetRequiredService<IHttpClientFactory>(),
						NullLogger<SchemaAppService>.Instance))
				.AddSingleton(sp => new GalleryService(
						sp.GetRequiredService<IHttpClientFactory>(),
						NullLogger<GalleryService>.Instance))
				.AddSingleton(sp => new CharacterDirectoryService(
						sp.GetRequiredService<IHttpClientFactory>(),
						NullLogger<CharacterDirectoryService>.Instance))
				// The profile header is an application-backed SchemaWidget; it injects these.
				.AddSingleton(new SharpMUSH.Client.Services.ApplicationCatalog([]))
				.AddSingleton(sp => new ApplicationRegistryClient(
						sp.GetRequiredService<IHttpClientFactory>(),
						NullLogger<ApplicationRegistryClient>.Instance))
				.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		// The example pages compose from scoped layouts. Register the widget registry and a real
		// (HTTP-backed) layout service; the in-memory handler 404s /api/layouts/* so each scope falls
		// back to its code default.
		var registry = new WidgetRegistry();
		registry.Register(new QuickLinksWidgetDescriptor());
		registry.Register(new WelcomeTextWidgetDescriptor());
		registry.Register(new CharacterGalleryWidgetDescriptor());
		registry.Register(new WikiIndexWidgetDescriptor());
		registry.Register(new WikiBodyWidgetDescriptor());
		ctx.Services
				.AddSingleton<IWidgetRegistry>(registry)
				.AddSingleton<ILayoutService>(sp => new LayoutService(
						sp.GetRequiredService<IHttpClientFactory>(),
						NullLogger<LayoutService>.Instance));

		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		return auth;
	}
}

/// <summary>
/// bUnit tests verifying that the wiki route pages render WikiView
/// with the correct Slug and Mode parameters.
/// </summary>
public class WikiPageRouteTests : TrackingBunitContext
{
	public WikiPageRouteTests()
	{
		this.AddWikiTestServices();
	}

	[TUnit.Core.Test]
	public async Task WikiIndex_RendersHeroAndCategoryGrid()
	{
		// The index composes from the "wiki-index" layout scope; its default layout is the
		// WikiIndex widget — a hero + auto-generated category grid sourced from WikiService.
		var cut = Render<SharpMUSH.Client.Pages.WikiIndex>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Everything you need to play"))
				throw new InvalidOperationException("wiki-index layout not resolved yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("Everything you need to play");
		await Assert.That(cut.Markup).Contains("wiki-hero");
	}

	[TUnit.Core.Test]
	public async Task WikiPage_RendersWikiViewWithParameterSlug()
	{
		var cut = Render<SharpMUSH.Client.Pages.WikiPage>(p => p
				.Add(c => c.Slug, "Magic_System")
				.Add(c => c.Ns, "main")
				.Add(c => c.Category, "general"));

		var wikiView = cut.FindComponent<WikiView>();
		await Assert.That(wikiView).IsNotNull();
		await Assert.That(wikiView.Instance.Slug).IsEqualTo("Magic_System");
		await Assert.That(wikiView.Instance.Mode).IsEqualTo(WikiView.WikiMode.View);
	}

	[TUnit.Core.Test]
	public async Task WikiPageEdit_RendersWikiViewInEditModeWithSeededContent()
	{
		// Seed via IWikiService so the HTTP handler can return real data.
		// InMemoryWikiService.Slugify: "Magic System" -> "magic_system"
		var wikiSvc = Services.GetRequiredService<IWikiService>();
		await wikiSvc.CreateAsync("Magic System", "Content here.", authorDbref: "#1", WikiNamespace.Main);

		var host = Render<Components.MudHarness>(p => p
				.AddChildContent<SharpMUSH.Client.Pages.WikiPageEdit>(cp => cp
						.Add(c => c.Slug, "magic_system")
						.Add(c => c.Ns, "main")
						.Add(c => c.Category, "general")));
		var cut = host.FindComponent<SharpMUSH.Client.Pages.WikiPageEdit>();

		var wikiView = cut.FindComponent<WikiView>();
		await Assert.That(wikiView).IsNotNull();
		await Assert.That(wikiView.Instance.Slug).IsEqualTo("magic_system");
		await Assert.That(wikiView.Instance.Mode).IsEqualTo(WikiView.WikiMode.Edit);
	}

	[TUnit.Core.Test]
	public async Task WikiPageEdit_ColdLoad_RendersTheEditorInsteadOfABlankColumn()
	{
		var wikiSvc = Services.GetRequiredService<IWikiService>();
		await wikiSvc.CreateAsync("Magic System", "Content here.", authorDbref: "#1", WikiNamespace.Main);

		var host = Render<Components.MudHarness>(p => p
				.AddChildContent<SharpMUSH.Client.Pages.WikiPageEdit>(cp => cp
						.Add(c => c.Slug, "magic_system")
						.Add(c => c.Ns, "main")
						.Add(c => c.Category, "general")));
		var cut = host.FindComponent<SharpMUSH.Client.Pages.WikiPageEdit>();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindComponents<WikiEdit>().Count == 0)
				throw new InvalidOperationException("editor not rendered yet");
		}, TimeSpan.FromSeconds(5));

		var editor = cut.FindComponent<WikiEdit>();
		await Assert.That(editor.Instance.Article).IsNotNull();
		await Assert.That(editor.Instance.Article!.Content).IsEqualTo("Content here.");
		await Assert.That(cut.Markup).Contains("wiki-edit-title");
	}
}

/// <summary>
/// Verifies view-time redlink resolution in WikiDisplay: wiki links to missing
/// pages gain class="wiki-redlink"; links to existing pages stay unmarked.
/// </summary>
public class WikiRedlinkRenderingTests : TrackingBunitContext
{
	public WikiRedlinkRenderingTests()
	{
		this.AddWikiTestServices();
	}

	[TUnit.Core.Test]
	public async Task WikiPage_MarksMissingLinksRed_LeavesExistingLinksAlone()
	{
		var wikiSvc = Services.GetRequiredService<IWikiService>();
		await wikiSvc.CreateAsync("Real Target", "I exist.", "#1", WikiNamespace.Main);
		await wikiSvc.CreateAsync("Linking Page",
				"See [[Real Target]] and [[Ghost Page]].", "#1", WikiNamespace.Main);

		var cut = Render<SharpMUSH.Client.Pages.WikiPage>(p => p
				.Add(c => c.Slug, "linking_page")
				.Add(c => c.Ns, "main")
				.Add(c => c.Category, "general"));

		// The exists round-trip completes asynchronously after first render.
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("href=\"/wiki/main/general/ghost_page\" class=\"wiki-redlink\""))
				throw new InvalidOperationException("redlink not applied yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("href=\"/wiki/main/general/ghost_page\" class=\"wiki-redlink\"");
		await Assert.That(cut.Markup).Contains("href=\"/wiki/main/general/real_target\"");
		await Assert.That(cut.Markup).DoesNotContain("href=\"/wiki/main/general/real_target\" class=\"wiki-redlink\"");
	}
}

public class CharacterRouteTests : TrackingBunitContext
{
	private readonly BunitAuthorizationContext _auth;

	public CharacterRouteTests()
	{
		_auth = this.AddWikiTestServices();
	}

	/// <summary>
	/// Renders the real page and waits until the profile layout has resolved and the wiki body has
	/// settled into its empty state (the widget renders before its fetch completes).
	/// </summary>
	private IRenderedComponent<SharpMUSH.Client.Pages.CharacterProfile> RenderProfile(string name)
	{
		var cut = Render<SharpMUSH.Client.Pages.CharacterProfile>(p => p.Add(c => c.Name, name));
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("WikiCharacterNoBio", StringComparison.Ordinal))
				throw new InvalidOperationException("character wiki body not settled yet");
		}, TimeSpan.FromSeconds(5));
		return cut;
	}

	/// <summary>
	/// A character with no wiki page is the normal case. The profile used to borrow the wiki's
	/// "This page does not exist yet" alert, which read as breakage on a character's own profile.
	/// [Authorize] does nothing in bUnit (pages render below AuthorizeRouteView), so this asserts
	/// the rendered wording directly rather than any redirect.
	/// </summary>
	[TUnit.Core.Test]
	public async Task CharacterProfile_WithoutBiography_StatesTheFactAndOffersToWriteOne()
	{
		_auth.SetAuthorized("Scribe");
		_auth.SetPolicies("wiki.create");

		var markup = RenderProfile("Aurelia").Markup;

		// EchoLocalizer renders the indexed form as "Key(arg)", so this also pins the name argument.
		await Assert.That(markup).Contains("WikiCharacterNoBio(Aurelia)");
		await Assert.That(markup).Contains("WikiCharacterWriteBio");
		await Assert.That(markup).DoesNotContain("WikiPageNotExist");
		await Assert.That(markup).DoesNotContain("CreateThisPage");
	}

	/// <summary>
	/// A reader who cannot create pages gets the same plain statement — not "Nothing to see here",
	/// which reads as a dead end on a character that plainly exists.
	/// </summary>
	[TUnit.Core.Test]
	public async Task CharacterProfile_WithoutBiography_UnprivilegedReaderSeesNoCreateOffer()
	{
		var markup = RenderProfile("Aurelia").Markup;

		await Assert.That(markup).Contains("WikiCharacterNoBio(Aurelia)");
		await Assert.That(markup).DoesNotContain("WikiCharacterWriteBio");
		await Assert.That(markup).DoesNotContain("NothingToSeeHere");
	}

	/// <summary>The ordinary wiki empty state is untouched — it is honest wording for a wiki page.</summary>
	[TUnit.Core.Test]
	public async Task WikiPage_WithoutArticle_KeepsTheWikiEmptyState()
	{
		_auth.SetAuthorized("Scribe");
		_auth.SetPolicies("wiki.create");

		var cut = Render<WikiView>(p => p
				.Add(v => v.Slug, "ghost_page")
				.Add(v => v.Mode, WikiView.WikiMode.View));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("WikiPageNotExist", StringComparison.Ordinal))
				throw new InvalidOperationException("wiki body not settled yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).DoesNotContain("WikiCharacterNoBio");
	}

	[TUnit.Core.Test]
	public async Task CharacterProfile_RendersWikiViewWithCharacterName()
	{
		// The profile composes from the "profile" scope; its default layout places the WikiBody
		// widget, which renders WikiView for the character supplied via the cascading page context.
		var cut = Render<SharpMUSH.Client.Pages.CharacterProfile>(p => p
				.Add(c => c.Name, "Gandalf"));

		cut.WaitForAssertion(() =>
		{
			if (cut.FindComponents<WikiView>().Count == 0)
				throw new InvalidOperationException("profile layout not resolved yet");
		}, TimeSpan.FromSeconds(5));

		var wikiView = cut.FindComponent<WikiView>();
		await Assert.That(wikiView).IsNotNull();
		await Assert.That(wikiView.Instance.Slug).IsEqualTo("Gandalf");
		await Assert.That(wikiView.Instance.Mode).IsEqualTo(WikiView.WikiMode.View);
	}
}

// The /help routes no longer render WikiView: help is the shipped helpfile corpus, not a wiki
// namespace. Their coverage lives in HelpPageTests.
