using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
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
	private record HelpIndexDto(string Corpus, string? Topic, string? Html, IReadOnlyList<string> Topics);

	private record HelpEntryDto(
		string Corpus,
		string RequestedTopic,
		string? Topic,
		string? Markdown,
		string? Html,
		IReadOnlyList<string> Candidates);

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
		await Assert.That(index.Html!).Contains("href=\"/help/getting%20started\"");
	}

	[Test]
	[Arguments("@mail", "@MAIL")]
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
		await Assert.That(entry.Candidates).Contains("helpfile");
		await Assert.That(entry.Candidates.Count).IsGreaterThan(1);
	}

	/// <summary>
	/// The whole point of the change: <c>help @mail</c> at a telnet prompt and
	/// <c>GET /api/help/entry?topic=@mail</c> must be the same entry, not two systems that happen to
	/// look similar. Asserted by rendering the API's markdown through the game's own ASCII renderer
	/// and comparing it to what the command actually notified.
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

		var before = NotifyCount();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"help {topic}"));
		var notified = NotifiedSince(before);

		var expected = RecursiveMarkdownHelper.RenderMarkdown(entry.Markdown!).ToString();

		await Assert.That(notified).Contains(expected)
			.Because($"'help {topic}' in game and /help/{topic} in the portal must reach the same entry");
	}

	/// <summary>
	/// Corpus isolation. <c>Security</c> exists only in the wizard-only <c>ahelp</c> files; the
	/// general corpus must not fall through to it. Before the corpus scoping fix a bare "help"
	/// reference searched every category, so a mortal typing <c>help security</c> got the admin entry.
	/// </summary>
	[Test]
	public async Task PublicCorpus_DoesNotFallThroughToAdminOnlyTopics()
	{
		var resolver = factory.Services.GetRequiredService<IHelpTopicResolver>();

		var admin = await resolver.GetExactAsync("ahelp", "Security");
		await Assert.That(admin).IsNotNull()
			.Because("the admin corpus has to actually be indexed for this test to mean anything");

		var http = CreateClient();
		var response = await http.GetAsync("api/help/entry?topic=Security");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task AdminCorpus_RefusedToAnonymous()
	{
		var http = CreateClient();

		await Assert.That((await http.GetAsync("api/help/admin")).StatusCode)
			.IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That((await http.GetAsync("api/help/admin/entry?topic=Security")).StatusCode)
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
		await Assert.That((await http.GetAsync("api/help/admin/entry?topic=Security")).StatusCode)
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
				OneOf<MString, string> oneOf => oneOf.Match(m => m.ToString(), s => s),
				MString mstring => mstring.ToString(),
				string text => text,
				_ => null
			};
	}
}
