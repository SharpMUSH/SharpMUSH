using Bunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// A text-file index built from a dictionary instead of a directory, so the real
/// <see cref="HelpTopicResolver"/> can run its actual matching over a corpus a test can state in
/// four lines. Entry keys are topic names, exactly as the markdown-header indexer produces them.
/// </summary>
file sealed class DictionaryTextFileService(Dictionary<string, Dictionary<string, string>> corpora)
	: ITextFileService
{
	private Dictionary<string, string>? Corpus(string reference) =>
		corpora.TryGetValue(reference.Split('/')[0], out var entries) ? entries : null;

	public Task<IEnumerable<string>> ListCategoriesAsync() =>
		Task.FromResult<IEnumerable<string>>(corpora.Keys);

	public Task<string> ListEntriesAsync(string fileReference, string separator = " ") =>
		Task.FromResult(string.Join(separator, Corpus(fileReference)?.Keys ?? Enumerable.Empty<string>()));

	public Task<string?> GetEntryAsync(string fileReference, string entryName)
	{
		var corpus = Corpus(fileReference);
		return Task.FromResult(corpus is not null && corpus.TryGetValue(entryName, out var body) ? body : null);
	}

	public Task<IEnumerable<string>> ListFilesAsync(string? category = null) =>
		Task.FromResult<IEnumerable<string>>([]);

	public Task<string?> GetFileContentAsync(string fileReference) => Task.FromResult<string?>(null);

	public Task<IEnumerable<string>> SearchEntriesAsync(string fileReference, string pattern)
	{
		var regex = new Regex(
			"^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
			RegexOptions.IgnoreCase);
		return Task.FromResult<IEnumerable<string>>(
			(Corpus(fileReference)?.Keys ?? Enumerable.Empty<string>()).Where(k => regex.IsMatch(k)).ToList());
	}

	public Task<IEnumerable<string>> SearchContentAsync(string fileReference, string searchTerm) =>
		Task.FromResult<IEnumerable<string>>(
			(Corpus(fileReference) ?? new Dictionary<string, string>())
			.Where(kv => kv.Value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
			.Select(kv => kv.Key)
			.ToList());

	public Task ReindexAsync() => Task.CompletedTask;
}

/// <summary>
/// Serves <c>/api/help*</c> by invoking the real <see cref="HelpController"/>, so the pages are
/// tested against the shapes and the HTML the server actually produces rather than against a
/// hand-written fixture that could drift from it.
/// </summary>
/// <remarks>
/// The controller's <c>[Authorize(Roles = "Wizard,God")]</c> gate is framework-enforced and does not
/// run here; that refusal is asserted over real HTTP in
/// <c>SharpMUSH.Tests.Integration.Portal.HelpApiTests</c>. What this handler does honour is the
/// <paramref name="isStaff"/> flag, so the page's own decision about whether to ask for the admin
/// corpus can be observed.
/// </remarks>
file sealed class HelpApiHandler(IHelpTopicResolver resolver, bool isStaff) : HttpMessageHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var controller = new HelpController(resolver);
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		var topic = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["topic"];

		if (path.StartsWith("api/help/admin", StringComparison.Ordinal) && !isStaff)
		{
			return new HttpResponseMessage(HttpStatusCode.Forbidden);
		}

		object? result = path switch
		{
			"api/help" => (await controller.Index()).Result,
			"api/help/entry" => (await controller.Entry(topic)).Result,
			"api/help/admin" => (await controller.AdminIndex()).Result,
			"api/help/admin/entry" => (await controller.AdminEntry(topic)).Result,
			_ => null
		};

		return result switch
		{
			OkObjectResult ok => Json(ok.Value!, HttpStatusCode.OK),
			NotFoundObjectResult missing => Json(missing.Value!, HttpStatusCode.NotFound),
			_ => new HttpResponseMessage(HttpStatusCode.NotFound)
		};
	}

	private static HttpResponseMessage Json(object value, HttpStatusCode status) =>
		new(status) { Content = JsonContent.Create(value, value.GetType()) };
}

/// <summary>
/// The portal help pages, rendered against the real help API.
/// </summary>
/// <remarks>
/// These pages used to render <c>WikiView</c> against slugs no install ever seeds, so <c>/help</c>
/// showed "This page does not exist yet. You can create it…" to every visitor on a fresh game. The
/// assertions below are about what a reader actually sees: real index content, a real entry, a
/// disambiguation list, a plain miss — and never an invitation to author a wiki page.
/// </remarks>
public class HelpPageTests : TrackingBunitContext
{
	private const string IndexBody = """
		# help
		This is the index to the MUSH online help files.
		For an explanation of the help system, type: help [newbie]
		""";

	private const string MailBody = """
		# MAIL
		# @MAIL
		@mail invokes the built-in MUSH mailer.
		""";

	private static readonly Dictionary<string, Dictionary<string, string>> Corpora = new()
	{
		["help"] = new(StringComparer.OrdinalIgnoreCase)
		{
			["help"] = IndexBody,
			["newbie"] = "# newbie\nIf you are new to MUSHing…",
			["MAIL"] = MailBody,
			["@MAIL"] = MailBody,
			["mail-sending"] = "# mail-sending\nHow to send mail.",
			["mail-reading"] = "# mail-reading\nHow to read mail.",
			["getting started"] = "# Getting Started\nA walkthrough.",
		},
		["ahelp"] = new(StringComparer.OrdinalIgnoreCase)
		{
			["ahelp"] = "# ahelp\nAdministrative help.",
			["Security"] = "# Security\nWizard-only security notes.",
		},
	};

	/// <summary>
	/// Waits for rendered markup to contain <paramref name="expected"/>. Written as a throwing
	/// assertion rather than a bool-returning lambda: bUnit's WaitForAssertion takes an Action, so a
	/// lambda that merely *returns* false satisfies it immediately and waits for nothing.
	/// </summary>
	private static void WaitForMarkup(Bunit.IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut, string expected) =>
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains(expected, StringComparison.Ordinal))
			{
				throw new InvalidOperationException($"markup does not (yet) contain '{expected}'");
			}
		}, TimeSpan.FromSeconds(5));

	private void AddHelpServices(bool isStaff)
	{
		var resolver = new HelpTopicResolver(new DictionaryTextFileService(Corpora));
		var client = Track(new HttpClient(new HelpApiHandler(resolver, isStaff))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		});

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		Services
			.AddSingleton(client)
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new GameHelpService(
				sp.GetRequiredService<IHttpClientFactory>(),
				NullLogger<GameHelpService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task Index_RendersTheShippedIndexAndEveryTopic()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		WaitForMarkup(cut, "This is the index to the MUSH online help files.");

		await Assert.That(cut.Markup).Contains("This is the index to the MUSH online help files.");
		await Assert.That(cut.Markup).Contains("href=\"/help/mail-sending\"");
		await Assert.That(cut.Markup).Contains("href=\"/help/getting%20started\"");
		await Assert.That(cut.Markup).DoesNotContain("does not exist yet")
			.Because("help is not wiki content — there is nothing here for a reader to create");
	}

	[TUnit.Core.Test]
	public async Task Index_OmitsTheAdminCorpusForAMortal()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization().SetAuthorized("mortal");

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		WaitForMarkup(cut, "This is the index to the MUSH online help files.");

		await Assert.That(cut.Markup).DoesNotContain("href=\"/help/admin/Security\"");
	}

	[TUnit.Core.Test]
	public async Task Index_ListsTheAdminCorpusForStaff()
	{
		AddHelpServices(isStaff: true);
		this.AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		WaitForMarkup(cut, "href=\"/help/admin/Security\"");

		await Assert.That(cut.Markup).Contains("href=\"/help/admin/Security\"");
	}

	[TUnit.Core.Test]
	[Arguments("@mail", "@mail invokes the built-in MUSH mailer.")]
	[Arguments("getting started", "A walkthrough.")]
	[Arguments("newbie", "If you are new to MUSHing")]
	public async Task Topic_RendersTheResolvedEntry(string topic, string expected)
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, topic));
		WaitForMarkup(cut, expected);

		await Assert.That(cut.Markup).Contains(expected);
	}

	[TUnit.Core.Test]
	public async Task Topic_OffersTheCandidatesWhenSeveralMatch()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, "mail-*"));
		WaitForMarkup(cut, "href=\"/help/mail-reading\"");

		await Assert.That(cut.Markup).Contains("href=\"/help/mail-reading\"");
		await Assert.That(cut.Markup).Contains("href=\"/help/mail-sending\"");
	}

	[TUnit.Core.Test]
	public async Task Topic_SaysNoSuchTopicRatherThanOfferingToCreateOne()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, "nosuchtopicxyz"));
		WaitForMarkup(cut, "HelpNoSuchTopic");

		await Assert.That(cut.Markup).Contains("HelpNoSuchTopic");
		await Assert.That(cut.Markup).DoesNotContain("does not exist yet");
	}

	/// <summary>
	/// The admin page renders below <c>AuthorizeRouteView</c> in bUnit, so its <c>[Authorize]</c>
	/// has no runtime effect here and a redirect test would prove nothing. What is worth asserting
	/// is that the page reads the admin corpus rather than the general one — a mortal reaching it
	/// gets the server's 403, which this handler reproduces, and sees an error rather than content.
	/// </summary>
	[TUnit.Core.Test]
	public async Task AdminTopic_ReadsTheAdminCorpus()
	{
		AddHelpServices(isStaff: true);
		this.AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");

		var cut = Render<SharpMUSH.Client.Pages.HelpAdminTopic>(p => p.Add(c => c.Topic, "Security"));
		WaitForMarkup(cut, "Wizard-only security notes.");

		await Assert.That(cut.Markup).Contains("Wizard-only security notes.");
	}

	[TUnit.Core.Test]
	public async Task AdminTopic_ShowsNoContentWhenTheServerRefuses()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization().SetAuthorized("mortal");

		var cut = Render<SharpMUSH.Client.Pages.HelpAdminTopic>(p => p.Add(c => c.Topic, "Security"));
		WaitForMarkup(cut, "HelpLoadFailed");

		await Assert.That(cut.Markup).DoesNotContain("Wizard-only security notes.");
	}
}
