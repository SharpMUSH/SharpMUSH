using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// The portal's read-only help API over real HTTP.
///
/// The point of this suite is the parity test: the portal used to answer help out of the wiki, so a
/// fresh game showed "This page does not exist yet" at <c>/help</c> while <c>help</c> at a telnet
/// prompt printed a full index. The fix is that both surfaces now run the same
/// <see cref="IHelpTopicResolver"/> over the same files, and <see cref="GameAndPortalResolveTheSameEntry"/>
/// asserts that rather than trusting it.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class HelpApiTests(ServerWebAppFactory factory)
{
	[Test]
	public async Task NamedSectionShowsCompleteWebArticleAndFocusedTerminalEntry()
	{
		var resolver = factory.Services.GetRequiredService<IHelpTopicResolver>();
		var terminal = await resolver.GetExactAsync("help", "align examples");
		await Assert.That(terminal?.Article?.Id).IsEqualTo("align");
		await Assert.That(terminal?.Markdown).DoesNotContain("&haiku");
		await Assert.That(terminal?.Markdown).Contains("Ashen-Shug");
		var http = CreateClient();
		var entry = await http.GetFromJsonAsync<HelpEntryDto>("api/help/entry?topic=align%20examples");
		await Assert.That(entry?.Markdown).Contains("&haiku");
		await Assert.That(entry?.Html).Contains("id=\"examples\"");
		await Assert.That(entry?.Html).Contains("href=\"/help/align%28%29#examples\"");
		var index = await http.GetFromJsonAsync<HelpIndexDto>("api/help");
		await Assert.That(index!.Topics).DoesNotContain("ALIGN2");
		await Assert.That(index.Topics).DoesNotContain("LALIGN()");
		var manifest = await http.GetFromJsonAsync<List<HelpArticle>>("api/help/articles");
		await Assert.That(manifest!.Count(article => article.Id == "align")).IsEqualTo(1);
		await Assert.That((await http.GetAsync("api/help/admin/articles")).StatusCode)
			.IsEqualTo(HttpStatusCode.Unauthorized);
	}

	private record HelpIndexDto(string Corpus, string? Topic, string? Html, IReadOnlyList<string> Topics);

	private record HelpEntryDto(
		string Corpus,
		string RequestedTopic,
		string? Topic,
		string? Markdown,
		string? Html,
		IReadOnlyList<string> Candidates)
	{
		public string? ArticleId { get; init; }
		public string? SectionId { get; init; }
		public string? CanonicalHref { get; init; }
	}

	private record AccountRegisterRequest(string Username, string? Email, string Password);

	private record AccountLoginResponse(string AccountId, string Username, string AccountSessionToken, string Role);

	private IMUSHCodeParser Parser => factory.CommandParser;
	private INotifyService NotifyService => factory.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => factory.Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// Pinned to https: the server redirects http→https, and HttpClient drops the Authorization
	/// header when it follows that 307.
	/// </summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	[Test]
	public async Task SectionDeepLinksKeepIdentityAndShareTheArticleSeoCanonical()
	{
		var http = CreateClient();
		var entry = await http.GetFromJsonAsync<HelpEntryDto>("api/help/entry?topic=align%20examples");
		await Assert.That(entry?.SectionId).IsEqualTo("examples");
		await Assert.That(entry?.CanonicalHref).IsEqualTo("/help/align%28%29#examples");
		using var request = new HttpRequestMessage(HttpMethod.Get, "help/align%20examples");
		request.Headers.UserAgent.ParseAdd("Googlebot/2.1 (+http://www.google.com/bot.html)");
		var response = await http.SendAsync(request);
		var html = await response.Content.ReadAsStringAsync();
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(html).Contains("<link rel=\"canonical\" href=\"https://localhost/help/align%28%29\"");
		await Assert.That(html).Contains("id=\"examples\"");
		await Assert.That(html).Contains("href=\"/help/align%28%29#examples\"");
	}

	[Test]
	public async Task Index_ServesTheShippedCorpusNotAnEmptyWikiPage()
	{
		var http = CreateClient();
		var response = await http.GetAsync("api/help");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var index = await response.Content.ReadFromJsonAsync<HelpIndexDto>();
		await Assert.That(index).IsNotNull();
		await Assert.That(index!.Topic).IsEqualTo("help");
		await Assert.That(index.Topics.Count).IsGreaterThan(500)
			.Because("the shipped corpus indexes over a thousand topics; a handful would mean it is reading the wrong directory");
		await Assert.That(index.Html).IsNotNull();
		await Assert.That(index.Html!).Contains("This is the index to the MUSH online help files.");
	}

	/// <summary>
	/// The index's <c>[newbie]</c>-style references must come back as portal links, escaped, so a
	/// reader can actually click through to the awkward ones.
	/// </summary>
	[Test]
	public async Task Index_LinksTopicReferencesIntoPortalUrls()
	{
		var http = CreateClient();
		var index = await http.GetFromJsonAsync<HelpIndexDto>("api/help");

		await Assert.That(index!.Html!).Contains("href=\"/help/newbie\"");
		await Assert.That(index.Html!).Contains("href=\"/help/Getting%20Started\"");
	}

	[Test]
	[Arguments("@mail", "MAIL")]
	[Arguments("mail-sending", "MAIL-SENDING")]
	[Arguments("getting started", "Getting Started")]
	[Arguments("NEWBIE", "newbie")]
	public async Task Entry_ResolvesTopicsWhoseNamesFightWithUrls(string requested, string expectedTopic)
	{
		var http = CreateClient();
		var response = await http.GetAsync($"api/help/entry?topic={Uri.EscapeDataString(requested)}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var entry = await response.Content.ReadFromJsonAsync<HelpEntryDto>();
		await Assert.That(entry!.Topic).IsEqualTo(expectedTopic);
		await Assert.That(entry.Markdown).IsNotNull();
		await Assert.That(entry.Html).IsNotNull();
	}

	[Test]
	public async Task Entry_UnknownTopicIs404NotAnInviteToWriteOne()
	{
		var http = CreateClient();
		var response = await http.GetAsync("api/help/entry?topic=nosuchtopicxyz123");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		var entry = await response.Content.ReadFromJsonAsync<HelpEntryDto>();
		await Assert.That(entry!.Topic).IsNull();
		await Assert.That(entry.Candidates.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Entry_AmbiguousTopicReturnsCandidates()
	{
		var http = CreateClient();
		var response = await http.GetAsync("api/help/entry?topic=help*");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var entry = await response.Content.ReadFromJsonAsync<HelpEntryDto>();
		await Assert.That(entry!.Topic).IsNull();
		await Assert.That(entry.Candidates).Contains("help search");
		await Assert.That(entry.Candidates).DoesNotContain("helpfile");
		await Assert.That(entry.Candidates.Count).IsGreaterThan(1);
	}

	/// <summary>
	/// The whole point of the change: <c>help @mail</c> at a telnet prompt and
	/// <c>GET /api/help/entry?topic=@mail</c> must be the same entry, not two systems that happen to
	/// look similar. They share canonical identity while the web composes the full article and the
	/// terminal renders the focused lookup returned by the common resolver.
	/// </summary>
	[Test]
	[Arguments("@mail")]
	[Arguments("getting started")]
	[Arguments("newbie")]
	public async Task GameAndPortalResolveTheSameEntry(string topic)
	{
		var http = CreateClient();
		var entry = await http.GetFromJsonAsync<HelpEntryDto>(
			$"api/help/entry?topic={Uri.EscapeDataString(topic)}");
		await Assert.That(entry!.Markdown).IsNotNull();
		var resolver = factory.Services.GetRequiredService<IHelpTopicResolver>();
		var focused = (await resolver.ResolveAsync("help", topic)).Expect<HelpEntry>();
		await Assert.That(entry.Topic).IsEqualTo(focused.Topic);
		await Assert.That(entry.ArticleId).IsEqualTo(focused.Article?.Id);
		await Assert.That(entry.SectionId).IsEqualTo(focused.SectionId);

		var before = NotifyCount();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"help {topic}"));
		var notified = NotifiedSince(before);

		var expected = RecursiveMarkdownHelper.RenderMarkdown(focused.Markdown).ToString();

		await Assert.That(notified).Contains(expected)
			.Because($"'help {topic}' in game and /help/{topic} in the portal must reach the same entry");
	}

	/// <summary>
	/// Corpus isolation. <c>ahelp</c> is a topic in both corpora: the command's entry in the general
	/// files and the index of the wizard-only <c>ahelp</c> files. The general corpus must answer with
	/// its own entry, never the admin one. Before the corpus scoping fix a bare "help" reference
	/// searched every category, so a mortal could read an admin entry through <c>help</c>.
	/// </summary>
	[Test]
	public async Task PublicCorpus_DoesNotFallThroughToAdminOnlyTopics()
	{
		var resolver = factory.Services.GetRequiredService<IHelpTopicResolver>();

		var admin = await resolver.GetExactAsync("ahelp", "ahelp");
		await Assert.That(admin?.Markdown).Contains(AdminIndexText)
			.Because("the admin corpus has to actually be indexed for this test to mean anything");

		var http = CreateClient();
		var response = await http.GetAsync("api/help/entry?topic=ahelp");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(AdminIndexText);
	}

	/// <summary>A line only the <c>ahelp</c> index carries.</summary>
	private const string AdminIndexText = "The administrator help files, readable by wizards.";

	[Test]
	public async Task AdminCorpus_RefusedToAnonymous()
	{
		var http = CreateClient();

		await Assert.That((await http.GetAsync("api/help/admin")).StatusCode)
			.IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That((await http.GetAsync("api/help/admin/entry?topic=ahelp")).StatusCode)
			.IsEqualTo(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// A freshly registered account controls no characters, so it derives no Wizard role — the
	/// portal equivalent of the mortal that <c>ahelp</c> answers "This command is for administrators
	/// only." to.
	/// </summary>
	[Test]
	public async Task AdminCorpus_RefusedToMortal()
	{
		var http = CreateClient();
		var register = await http.PostAsJsonAsync(
			"api/auth/account-register",
			new AccountRegisterRequest($"help{Guid.NewGuid():N}"[..18], null, "Help-Test-Pw-1!"));
		await Assert.That(register.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var account = await register.Content.ReadFromJsonAsync<AccountLoginResponse>();
		http.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", account!.AccountSessionToken);

		// Sanity: the same token reads general help perfectly well, so a 403 below is the corpus
		// gate and not a broken session.
		await Assert.That((await http.GetAsync("api/help")).StatusCode).IsEqualTo(HttpStatusCode.OK);

		await Assert.That((await http.GetAsync("api/help/admin")).StatusCode)
			.IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await http.GetAsync("api/help/admin/entry?topic=ahelp")).StatusCode)
			.IsEqualTo(HttpStatusCode.Forbidden);
	}

	private int NotifyCount() =>
		NotifyService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

	private IReadOnlyList<string> NotifiedSince(int count) =>
		NotifyService.ReceivedCalls()
			.Select(MessageTextOf)
			.OfType<string>()
			.Skip(count)
			.ToList();

	private static string? MessageTextOf(ICall call)
	{
		if (call.GetMethodInfo().Name != nameof(INotifyService.Notify))
		{
			return null;
		}

		var args = call.GetArguments();
		return args.Length < 2
			? null
			: args[1] switch
			{
				SharpMessage message => message switch
				{
					MString markup => markup.ToString(),
					string plain => plain,
				},
				MString mstring => mstring.ToString(),
				string text => text,
				_ => null
			};
	}
}
