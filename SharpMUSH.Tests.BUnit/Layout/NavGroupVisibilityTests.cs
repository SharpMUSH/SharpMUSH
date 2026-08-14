using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>Answers the one call NavMenu's own init makes — GET api/applications — with no apps.</summary>
file sealed class EmptyRegistryHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		=> Task.FromResult(request.RequestUri!.AbsolutePath.TrimStart('/') == "api/applications"
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) }
			: new HttpResponseMessage(HttpStatusCode.NotFound));
}

/// <summary>
/// The reported defect: NavMenu rendered the "Build" group label unconditionally while every item
/// inside it is permission- or data-gated, so an anonymous visitor and a registered account with no
/// characters both got a BUILD heading with zero children. "Manage" collapsed the same way — down to
/// a single entry, Help, for every non-admin persona.
///
/// <para>
/// The fix is a CSS rule (<c>.nav-group:not(:has(.phosphor-nav-link))</c>) rather than a render-time
/// condition, because emptiness is not statically knowable here: the children are
/// <c>&lt;AuthorizeView&gt;</c> blocks and data-driven <c>&lt;ApplicationNavLinks&gt;</c>, both of
/// which render zero ELEMENTS rather than an empty container the parent could inspect. So these
/// tests assert the two halves of that contract separately: the rendered DOM really does put a group
/// into (or keep it out of) the "no links inside" state the selector matches, and the stylesheet
/// really does carry the rule that hides it.
/// </para>
/// </summary>
public class NavGroupVisibilityTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];

	private BunitAuthorizationContext Auth { get; }

	public NavGroupVisibilityTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(false);
		var terminalHost = new TerminalServiceHost(() => terminal);
		Services.AddSingleton(terminalHost);
		Services.AddSingleton<ITerminalService>(terminalHost);

		var playTerminal = Substitute.For<IPlayTerminalService>();
		playTerminal.IsConnected.Returns(false);
		var playHost = new PlayTerminalServiceHost(() => playTerminal);
		Services.AddSingleton(playHost);
		Services.AddSingleton<IPlayTerminalService>(playHost);

		Services.AddSingleton(Substitute.For<IConnectionStateService>());
		Services.AddSingleton<CharacterSwitchService>();

		// No applications registered: the data-driven half of every group contributes nothing, which
		// is the state the four seeded personas were actually in.
		var apiClient = new HttpClient(new EmptyRegistryHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		_ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services.AddSingleton(factory);
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(new AccountAuthService(
			factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>()));

		Auth = AddAuthorization();
	}

	private IRenderedComponent<NavMenu> RenderNav() => Render<NavMenu>(p => p.Add(c => c.IsCollapsed, false));

	/// <summary>The group whose label is <paramref name="labelKey"/> (EchoLocalizer echoes resx keys).</summary>
	private static IElement Group(IRenderedComponent<NavMenu> cut, string labelKey) =>
		cut.FindAll(".nav-group")
			.Single(g => g.QuerySelector(".nav-group-label")?.TextContent.Trim() == labelKey);

	private static int LinkCount(IElement group) => group.QuerySelectorAll(".phosphor-nav-link").Length;

	[Test]
	public async Task Build_has_no_links_for_an_anonymous_visitor()
	{
		Auth.SetNotAuthorized();

		var cut = RenderNav();

		await Assert.That(LinkCount(Group(cut, "NavSectionBuild"))).IsEqualTo(0)
			.Because("every Build entry is permission-gated, so the CSS rule is what must hide the heading");
	}

	[Test]
	public async Task Manage_has_no_links_for_a_signed_in_non_admin()
	{
		// A registered player: authenticated, but holding none of the admin policies Manage gates on.
		Auth.SetAuthorized("player");
		Auth.SetClaims(new Claim(ClaimTypes.Role, "Player"));

		var cut = RenderNav();

		await Assert.That(LinkCount(Group(cut, "NavSectionManage"))).IsEqualTo(0);
		await Assert.That(LinkCount(Group(cut, "NavSectionBuild"))).IsEqualTo(0);
	}

	[Test]
	public async Task Populated_groups_keep_their_links_so_the_rule_cannot_hide_them()
	{
		Auth.SetNotAuthorized();

		var cut = RenderNav();

		// The other half of the fix: the selector must not swallow groups that do have children.
		await Assert.That(LinkCount(Group(cut, "Play"))).IsGreaterThan(0);
		await Assert.That(LinkCount(Group(cut, "NavSectionWorld"))).IsGreaterThan(0);
	}

	[Test]
	public async Task An_admin_still_sees_Build_and_Manage()
	{
		Auth.SetAuthorized("headwiz");
		Auth.SetClaims(new Claim(ClaimTypes.Role, "Wizard"));
		Auth.SetPolicies("softcode.use", "players.view", "roles.admin");

		var cut = RenderNav();

		await Assert.That(LinkCount(Group(cut, "NavSectionBuild"))).IsGreaterThan(0);
		await Assert.That(LinkCount(Group(cut, "NavSectionManage"))).IsGreaterThan(0);
	}

	/// <summary>
	/// Help was the only ungated entry under Manage, which made Manage a one-item section holding
	/// something nobody manages for every non-admin persona. It now sits with Wiki under World —
	/// reference reading, next to the other reference reading — leaving Manage as pure staff tooling.
	/// </summary>
	[Test]
	public async Task Help_lives_under_World_not_Manage()
	{
		Auth.SetNotAuthorized();

		var cut = RenderNav();

		await Assert.That(Group(cut, "NavSectionWorld").QuerySelector("a[href='/help']")).IsNotNull();
		await Assert.That(Group(cut, "NavSectionManage").QuerySelector("a[href='/help']")).IsNull();
	}

	[Test]
	public async Task The_stylesheet_hides_a_nav_group_that_rendered_no_links()
	{
		// The stylesheet is split by responsibility (tokens / shell / utilities / syntax / globals), so
		// this reads the whole folder as one sheet rather than pinning a filename the split moved the
		// rule out of.
		var css = string.Join("\n", await Task.WhenAll(
			Directory.EnumerateFiles(Path.Join(AppContext.BaseDirectory, "client", "css"), "*.css")
				.OrderBy(f => f, StringComparer.Ordinal)
				.Select(f => File.ReadAllTextAsync(f))));

		var rule = Regex.Match(
			css,
			@"\.nav-group:not\(:has\(\.phosphor-nav-link\)\)\s*\{(?<body>[^}]*)\}",
			RegexOptions.Singleline);

		await Assert.That(rule.Success).IsTrue()
			.Because("the DOM assertions above only matter if the stylesheet acts on that state");
		await Assert.That(rule.Groups["body"].Value.Replace(" ", "")).Contains("display:none");
	}

	/// <summary>
	/// Disposes the HttpClient created for the substitute IHttpClientFactory. TUnit's disposer prefers
	/// IAsyncDisposable over IDisposable when a type implements both (as BunitContext does), so
	/// overriding only Dispose would never run.
	/// </summary>
	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
