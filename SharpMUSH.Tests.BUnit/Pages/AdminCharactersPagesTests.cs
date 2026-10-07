using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>The characters list, a character's detail page, the moderation page and the audit log.</summary>
public class AdminCharactersPagesTests : TrackingBunitContext
{
	/// <summary>Answers by path and query first, then by path alone, else 404.</summary>
	private sealed class Handler : HttpMessageHandler
	{
		public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
		public List<string> Requested { get; } = [];
		private readonly List<HttpResponseMessage> _responses = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var full = request.RequestUri!.PathAndQuery.TrimStart('/');
			var path = request.RequestUri.AbsolutePath.TrimStart('/');
			Requested.Add($"{request.Method} {full}");
			var response = Bodies.TryGetValue(full, out var body) || Bodies.TryGetValue(path, out body)
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
				: new HttpResponseMessage(HttpStatusCode.NotFound);
			_responses.Add(response);
			return Task.FromResult(response);
		}

		/// <summary>Disposed with the tracked client, which owns this handler.</summary>
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				foreach (var response in _responses) response.Dispose();
				_responses.Clear();
			}

			base.Dispose(disposing);
		}
	}

	private readonly Handler _api = new();
	private BunitAuthorizationContext Auth { get; }

	public AdminCharactersPagesTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton<AdminAccountsService>()
			.AddSingleton<AdminCharactersService>()
			.AddSingleton<AdminAuditService>()
			.AddSingleton<AdminBansService>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		Auth = AddAuthorization();
		Auth.SetAuthorized("staff");
		Auth.SetPolicies("players.view");
	}

	private IRenderedComponent<MudHarness> RenderPage<TPage>(Action<ComponentParameterCollectionBuilder<TPage>>? parameters = null)
		where TPage : Microsoft.AspNetCore.Components.IComponent =>
		Render<MudHarness>(p => p.AddChildContent<TPage>(parameters ?? (_ => { })));

	private const string Alice = """
		{"dbrefNumber":7,"creationTime":1700000000000,"name":"Alice","accountKey":"acc1","accountName":"alice",
		 "online":true,"flags":"PLAYER CONNECTED","lastConnect":"Mon Oct 05 12:00:00 2026"}
		""";

	private const string Bob = """
		{"dbrefNumber":9,"creationTime":1700000000001,"name":"Bob","accountKey":null,"accountName":null,
		 "online":false,"flags":"PLAYER","lastConnect":null}
		""";

	[Test]
	public async Task Characters_ListsEachCharacter_LinkingToItsDetail()
	{
		_api.Bodies["api/admin/characters"] = $$"""{"characters":[{{Alice}},{{Bob}}],"total":2}""";
		var cut = RenderPage<AdminCharacters>();

		cut.WaitForAssertion(() => cut.Find("a.adm-chars-link"), TimeSpan.FromSeconds(5));
		var links = cut.FindAll("a.adm-chars-link");
		await Assert.That(links.Select(a => a.GetAttribute("href"))).IsEquivalentTo(["/admin/characters/7", "/admin/characters/9"]);
		await Assert.That(cut.Find(".adm-chars-total").TextContent).IsEqualTo("AdmCharactersTotal(2)");
		await Assert.That(cut.Markup).Contains("AdmCharactersNoAccount").Because("Bob has no account");
		await Assert.That(cut.Markup).Contains("AdmCharactersNever").Because("Bob never connected");
		await Assert.That(cut.FindAll(".mud-pagination").Count).IsEqualTo(0).Because("two rows fit one page");
	}

	[Test]
	public async Task Characters_SendsOnlyTheFiltersThatAreSet()
	{
		_api.Bodies["api/admin/characters"] = $$"""{"characters":[{{Alice}}],"total":1}""";
		var cut = RenderPage<AdminCharacters>();
		cut.WaitForAssertion(() => cut.Find("a.adm-chars-link"), TimeSpan.FromSeconds(5));

		await Assert.That(_api.Requested).Contains("GET api/admin/characters?page=1&pageSize=50");
	}

	private const string AliceDetail = $$"""
		{"character":{{Alice}},"created":"2023-11-14T22:13:20+00:00","attributeCount":12,"mailTotal":3,"mailUnread":1,
		 "lastLogout":null,"lastSite":null,"lastIp":null,"roles":["builder"],"connections":[4]}
		""";

	[Test]
	public async Task PlayerDetail_ShowsTheCharacter()
	{
		_api.Bodies["api/admin/characters/7"] = AliceDetail;
		var cut = RenderPage<PlayerDetail>(p => p.Add(x => x.Id, 7));

		cut.WaitForAssertion(() => cut.Find(".adm-char-facts"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-page-head h1").TextContent.Trim()).IsEqualTo("Alice");
		await Assert.That(cut.Markup).Contains("AdmCharacterMailCount(3, 1)");
		await Assert.That(cut.Markup).Contains("builder");
		await Assert.That(cut.Markup).DoesNotContain("AdmCharacterLastSite")
			.Because("the server sends the site only to server.admin, and the page shows nothing in its place");
	}

	[Test]
	public async Task PlayerDetail_OffersNoActionsToAViewer()
	{
		_api.Bodies["api/admin/characters/7"] = AliceDetail;
		var cut = RenderPage<PlayerDetail>(p => p.Add(x => x.Id, 7));

		cut.WaitForAssertion(() => cut.Find(".adm-char-facts"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".adm-char-boot, .adm-char-unlink").Count).IsEqualTo(0);
	}

	[Test]
	public async Task PlayerDetail_OffersBootUnlinkAndTheAuditTrailToAModerator()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		_api.Bodies["api/admin/characters/7"] = AliceDetail;
		var cut = RenderPage<PlayerDetail>(p => p.Add(x => x.Id, 7));

		cut.WaitForAssertion(() => cut.Find(".adm-char-boot"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".adm-char-unlink").Count).IsEqualTo(1);
		await Assert.That(cut.Find("a.kit-capsule[href^='/admin/audit']").GetAttribute("href"))
			.IsEqualTo("/admin/audit?text=%237%3A1700000000000");
	}

	[Test]
	public async Task PlayerDetail_OffersNoBootForAnOfflineCharacter()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		_api.Bodies["api/admin/characters/9"] = $$"""
			{"character":{{Bob}},"created":"2023-11-14T22:13:20+00:00","attributeCount":0,"mailTotal":0,"mailUnread":0,
			 "lastLogout":null,"lastSite":null,"lastIp":null,"roles":[],"connections":[]}
			""";
		var cut = RenderPage<PlayerDetail>(p => p.Add(x => x.Id, 9));

		cut.WaitForAssertion(() => cut.Find(".adm-char-facts"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".adm-char-boot").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".adm-char-unlink").Count).IsEqualTo(0).Because("Bob has no account");
	}

	private static string Entry(string id, string action, string? next = null) => $$"""
		{"id":"{{id}}","at":"2026-10-05T12:00:00+00:00","action":"{{action}}","source":"Portal",
		 "actor":{"accountId":"a","accountName":"wiz","objid":"#1:1","name":"One"},
		 "target":{"kind":"character","id":"#7:1700000000000","name":"Alice"},"details":"Disabled"}
		""";

	[Test]
	public async Task AuditLog_ListsEntries_AndLoadsMoreFromTheCursor()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		_api.Bodies["api/admin/audit?limit=50"] = $$"""{"entries":[{{Entry("02", "player.boot")}}],"next":"02"}""";
		_api.Bodies["api/admin/audit?before=02&limit=50"] = $$"""{"entries":[{{Entry("01", "account.status")}}],"next":null}""";
		var cut = RenderPage<AuditLog>();

		cut.WaitForAssertion(() => cut.Find(".adm-audit-more"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("a.adm-audit-link").GetAttribute("href")).IsEqualTo("/admin/characters/7");
		await Assert.That(cut.Markup).Contains("AdmAuditSourcePortal");

		await cut.Find(".adm-audit-more").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".adm-audit-table tbody tr").Count != 2) throw new InvalidOperationException("not appended");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".adm-audit-more").Count).IsEqualTo(0).Because("the last page has no cursor");
	}

	[Test]
	public async Task AuditLog_TakesItsTextFilterFromTheAddress()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
			.NavigateTo("/admin/audit?text=%237%3A1700000000000");
		_api.Bodies["api/admin/audit"] = """{"entries":[],"next":null}""";
		var cut = RenderPage<AuditLog>();

		cut.WaitForAssertion(() => cut.Find(".kit-empty-box"), TimeSpan.FromSeconds(5));
		await Assert.That(_api.Requested).Contains("GET api/admin/audit?text=%237%3A1700000000000&limit=50");
	}

	private const string NoBans = """{"bans":[],"hosts":[]}""";

	[Test]
	public async Task PlayerDetail_LinksAModeratorToTheModerationPage()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		_api.Bodies["api/admin/characters/7"] = AliceDetail;
		var cut = RenderPage<PlayerDetail>(p => p.Add(x => x.Id, 7));

		cut.WaitForAssertion(() => cut.Find("a.adm-char-moderate"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("a.adm-char-moderate").GetAttribute("href")).IsEqualTo("/admin/moderation?character=7");
	}

	[Test]
	public async Task Moderation_ListsBansAndTheSitelockRules()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		_api.Bodies["api/admin/bans"] = """
			{"bans":[{"accountKey":"acc1","accountName":"alice","characters":["Alice"],"reason":"spam",
			  "bannedBy":"wiz","at":"2026-10-05T12:00:00+00:00","expiresAt":null}],
			 "hosts":[{"pattern":"*.example.com","rules":["!connect"]}]}
			""";
		var cut = RenderPage<Moderation>();

		cut.WaitForAssertion(() => cut.Find(".adm-mod-table"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".adm-mod-table").TextContent).Contains("spam");
		await Assert.That(cut.Find(".adm-mod-table").TextContent).Contains("AdmModerationPermanent");
		await Assert.That(cut.FindAll(".adm-mod-table .adm-mod-lift").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".adm-mod-hosts").TextContent).Contains("*.example.com");
		await Assert.That(cut.FindAll("a[href='/admin/config/sitelock']").Count).IsEqualTo(0)
			.Because("managing the sitelock needs config.admin");
	}

	[Test]
	public async Task Moderation_WarnsThePickedCharacterOnceAReasonIsGiven()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
			.NavigateTo("/admin/moderation?character=7");
		_api.Bodies["api/admin/characters/7"] = AliceDetail;
		_api.Bodies["api/admin/bans"] = NoBans;
		_api.Bodies["api/admin/characters/7/warn"] = "{}";
		var cut = RenderPage<Moderation>();

		cut.WaitForAssertion(() => cut.Find(".adm-mod-warn"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".adm-mod-warn").HasAttribute("disabled")).IsTrue().Because("a warning needs a reason");
		await Assert.That(cut.Find(".adm-mod-ban").HasAttribute("disabled")).IsTrue().Because("a ban needs a reason");
		await Assert.That(cut.Find(".adm-mod-boot").HasAttribute("disabled")).IsFalse().Because("Alice is online");

		await cut.Find("#moderation-reason").InputAsync("off-topic");
		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".adm-mod-warn").HasAttribute("disabled")) throw new InvalidOperationException("still disabled");
		}, TimeSpan.FromSeconds(5));
		await cut.Find(".adm-mod-warn").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!_api.Requested.Contains("POST api/admin/characters/7/warn?created=1700000000000"))
				throw new InvalidOperationException("not sent");
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task Moderation_OffersNoBanOrUnlinkForACharacterWithoutAnAccount()
	{
		Auth.SetPolicies("players.view", "players.moderate");
		Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
			.NavigateTo("/admin/moderation?character=9");
		_api.Bodies["api/admin/characters/9"] = $$"""
			{"character":{{Bob}},"created":"2023-11-14T22:13:20+00:00","attributeCount":0,"mailTotal":0,"mailUnread":0,
			 "lastLogout":null,"lastSite":null,"lastIp":null,"roles":[],"connections":[]}
			""";
		_api.Bodies["api/admin/bans"] = NoBans;
		var cut = RenderPage<Moderation>();

		cut.WaitForAssertion(() => cut.Find(".adm-mod-unlink"), TimeSpan.FromSeconds(5));
		await cut.Find("#moderation-reason").InputAsync("reason");
		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".adm-mod-warn").HasAttribute("disabled")) throw new InvalidOperationException("still disabled");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".adm-mod-ban").HasAttribute("disabled")).IsTrue();
		await Assert.That(cut.Find(".adm-mod-unlink").HasAttribute("disabled")).IsTrue();
		await Assert.That(cut.Find(".adm-mod-boot").HasAttribute("disabled")).IsTrue().Because("Bob is offline");
	}
}
