using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// A minimal test-only host that owns <see cref="AccountPanel.IsOpen"/> and re-renders
/// <see cref="AccountPanel"/> whenever <see cref="AccountPanel.IsOpenChanged"/> fires — exactly what
/// a real two-way <c>@bind-IsOpen</c> parent (NavMenu) does. Built with a manual
/// <see cref="RenderTreeBuilder"/> rather than a second .razor file, since the panel's own
/// open/close behavior (the thing several of these tests assert against real rendered markup, not
/// just "the callback fired") only shows up when something actually re-supplies the parameter after
/// the callback — a bare <c>Render&lt;AccountPanel&gt;(p => p.Add(x => x.IsOpen, true))</c> would
/// freeze IsOpen at its initial value for the rest of the test.
/// </summary>
file sealed class AccountPanelTestHost : ComponentBase
{
	[Parameter] public bool InitialOpen { get; set; }
	[Parameter] public IReadOnlyList<CharacterSummary> Characters { get; set; } = [];
	[Parameter] public CharacterSummary? ActiveCharacter { get; set; }
	[Parameter] public EventCallback<CharacterSummary> OnSwitchCharacter { get; set; }
	[Parameter] public EventCallback<CharacterSummary> OnOpenInNewTab { get; set; }
	[Parameter] public EventCallback OnLogout { get; set; }

	private bool _isOpen;
	private bool _seeded;

	protected override void OnParametersSet()
	{
		if (!_seeded)
		{
			_isOpen = InitialOpen;
			_seeded = true;
		}
	}

	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.OpenComponent<AccountPanel>(0);
		builder.AddAttribute(1, nameof(AccountPanel.IsOpen), _isOpen);
		builder.AddAttribute(2, nameof(AccountPanel.IsOpenChanged),
			EventCallback.Factory.Create<bool>(this, open =>
			{
				_isOpen = open;
				StateHasChanged();
			}));
		builder.AddAttribute(3, nameof(AccountPanel.Characters), Characters);
		builder.AddAttribute(4, nameof(AccountPanel.ActiveCharacter), ActiveCharacter);
		builder.AddAttribute(5, nameof(AccountPanel.OnSwitchCharacter), OnSwitchCharacter);
		builder.AddAttribute(6, nameof(AccountPanel.OnOpenInNewTab), OnOpenInNewTab);
		builder.AddAttribute(7, nameof(AccountPanel.OnLogout), OnLogout);
		builder.CloseComponent();
	}
}

/// <summary>
/// bUnit coverage for the two things Task 8 introduces: <see cref="AccountPanel"/> itself (open/
/// close mechanics, the root-vs-submenu levels, the character list, the three account actions), and
/// its wiring into the NavMenu bottom-left profile card (the card now opens the panel instead of
/// linking straight to <c>/account</c>).
///
/// Two harnesses:
/// - <see cref="RenderPanel"/> renders <see cref="AccountPanel"/> directly (via
///   <see cref="AccountPanelTestHost"/>) for panel-only behavior — no HTTP, no real
///   <see cref="AccountAuthService"/> login needed, since the panel is a pure presentation
///   component driven entirely by parameters.
/// - <see cref="RenderNavMenu"/> renders the real <see cref="NavMenu"/> for the two tests that are
///   actually about the CARD (closed until clicked; still openable when the sidebar is collapsed).
///   Mirrors <c>NavMenuActiveCharacterTests</c>'s harness but skips the login HTTP call entirely —
///   the card only needs the cascaded ClaimsPrincipal (<c>Auth.SetAuthorized</c>) to render, not an
///   actually-logged-in <see cref="AccountAuthService"/>.
/// </summary>
public class AccountPanelTests : BunitContext
{
	private BunitAuthorizationContext Auth { get; }

	public AccountPanelTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, StubLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		Auth = this.AddAuthorization();
	}

	private sealed class StubLocalizer<T> : IStringLocalizer<T>
	{
		public LocalizedString this[string name] => new(name, name);
		public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
		public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
	}

	/// <summary>Fakes <c>api/applications</c> only — NavMenu's own OnInitializedAsync fetches it
	/// unconditionally via <see cref="ApplicationRegistryClient"/>, whether or not anyone logs in.</summary>
	private sealed class ApplicationsOnlyHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.TrimStart('/') == "api/applications")
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(Array.Empty<object>())
				});
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}

	private IRenderedComponent<MudHarness> RenderPanel(
		IReadOnlyList<CharacterSummary>? characters = null,
		CharacterSummary? activeCharacter = null,
		bool initialOpen = false,
		EventCallback<CharacterSummary> onSwitchCharacter = default,
		EventCallback<CharacterSummary> onOpenInNewTab = default,
		EventCallback onLogout = default)
		=> Render<MudHarness>(p => p.AddChildContent<AccountPanelTestHost>(h => h
			.Add(x => x.InitialOpen, initialOpen)
			.Add(x => x.Characters, characters ?? [])
			.Add(x => x.ActiveCharacter, activeCharacter)
			.Add(x => x.OnSwitchCharacter, onSwitchCharacter)
			.Add(x => x.OnOpenInNewTab, onOpenInNewTab)
			.Add(x => x.OnLogout, onLogout)));

	/// <summary>Real NavMenu, unauthenticated AccountAuthService (no login call) — the card only
	/// depends on the cascaded ClaimsPrincipal, not on AccountAuth actually holding a session.
	/// Wrapped in <see cref="MudHarness"/> for the same reason <c>AccountChromeTests</c> is: the
	/// panel now renders a MudPopover, which needs a MudPopoverProvider in the tree.</summary>
	private IRenderedComponent<MudHarness> RenderNavMenu(bool isCollapsed)
	{
		Auth.SetAuthorized("headwiz");

		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(false);
		Services.AddSingleton(terminal);

		var playTerminal = Substitute.For<IPlayTerminalService>();
		playTerminal.IsConnected.Returns(false);
		Services.AddSingleton(playTerminal);

		var apiClient = new HttpClient(new ApplicationsOnlyHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services.AddSingleton(factory);
		Services.AddSingleton(sp => new ApplicationRegistryClient(
			sp.GetRequiredService<IHttpClientFactory>(),
			NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(sp => new AccountAuthService(
			sp.GetRequiredService<IHttpClientFactory>(), JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance));

		return Render<MudHarness>(p => p.AddChildContent<NavMenu>(nm => nm.Add(c => c.IsCollapsed, isCollapsed)));
	}

	// ── Card wiring (real NavMenu) ──────────────────────────────────────────────────────────

	[Test]
	public async Task Panel_is_closed_until_the_card_is_clicked()
	{
		var cut = RenderNavMenu(isCollapsed: false);

		await Assert.That(cut.FindAll(".account-panel").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".account-panel-scrim").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Clicking_the_card_opens_the_panel()
	{
		var cut = RenderNavMenu(isCollapsed: false);

		cut.Find("button.phosphor-profile-card").Click();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel").Count == 0)
				throw new InvalidOperationException("panel not open yet");
		});
		await Assert.That(cut.Markup).Contains("Account Management");
	}

	[Test]
	public async Task Panel_opens_when_the_sidebar_is_collapsed()
	{
		var cut = RenderNavMenu(isCollapsed: true);

		// Collapsed: the card renders icon-only (no name/sub/chevron), but the button itself and its
		// click handler are unaffected — the whole point of the collapsed rail is that the card
		// remains a working affordance, just narrower.
		await Assert.That(cut.FindAll(".phosphor-profile-name").Count).IsEqualTo(0);

		cut.Find("button.phosphor-profile-card").Click();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel").Count == 0)
				throw new InvalidOperationException("panel not open yet");
		});
		await Assert.That(cut.Markup).Contains("Account Management");
	}

	// ── Panel behavior (direct AccountPanel render) ─────────────────────────────────────────

	[Test]
	public async Task Escape_closes_the_panel()
	{
		var cut = RenderPanel(initialOpen: true);
		await Assert.That(cut.FindAll(".account-panel").Count).IsEqualTo(1);

		cut.Find(".account-panel").KeyDown(new KeyboardEventArgs { Key = "Escape" });

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel").Count != 0)
				throw new InvalidOperationException("panel still open");
		});
		await Assert.That(cut.FindAll(".account-panel").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Clicking_outside_closes_the_panel()
	{
		var cut = RenderPanel(initialOpen: true);
		await Assert.That(cut.FindAll(".account-panel-scrim").Count).IsEqualTo(1);

		cut.Find(".account-panel-scrim").Click();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel").Count != 0)
				throw new InvalidOperationException("panel still open");
		});
		await Assert.That(cut.FindAll(".account-panel-scrim").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Switch_Character_opens_the_submenu()
	{
		var characters = new[] { new CharacterSummary(1, 1L, "Alpha", ""), new CharacterSummary(2, 2L, "Beta", "") };
		var cut = RenderPanel(characters: characters, initialOpen: true);

		await Assert.That(cut.Find(".account-panel-track").ClassList).DoesNotContain("account-panel-track--submenu");

		cut.Find(".account-panel-switch-btn").Click();

		await Assert.That(cut.Find(".account-panel-track").ClassList).Contains("account-panel-track--submenu");
		var rows = cut.FindAll(".account-panel-character");
		await Assert.That(rows.Count).IsEqualTo(2);
		await Assert.That(cut.Markup).Contains("Alpha");
		await Assert.That(cut.Markup).Contains("Beta");
	}

	[Test]
	public async Task Active_character_carries_a_checkmark()
	{
		var alpha = new CharacterSummary(1, 1L, "Alpha", "");
		var beta = new CharacterSummary(2, 2L, "Beta", "");
		var cut = RenderPanel(characters: [alpha, beta], activeCharacter: beta, initialOpen: true);

		cut.Find(".account-panel-switch-btn").Click();

		var rows = cut.FindAll(".account-panel-character");
		var alphaRow = rows.Single(r => r.TextContent.Contains("Alpha"));
		var betaRow = rows.Single(r => r.TextContent.Contains("Beta"));

		await Assert.That(betaRow.ClassList).Contains("account-panel-character--active");
		await Assert.That(betaRow.QuerySelector(".account-panel-character-check")!.TextContent).IsEqualTo("✓");
		await Assert.That(alphaRow.ClassList).DoesNotContain("account-panel-character--active");
		await Assert.That(alphaRow.QuerySelector(".account-panel-character-check")!.TextContent).IsEqualTo("");
	}

	[Test]
	public async Task Choosing_a_character_invokes_OnSwitchCharacter()
	{
		CharacterSummary? chosen = null;
		var alpha = new CharacterSummary(1, 1L, "Alpha", "");
		var beta = new CharacterSummary(2, 2L, "Beta", "");
		var cut = RenderPanel(
			characters: [alpha, beta],
			activeCharacter: alpha,
			initialOpen: true,
			onSwitchCharacter: EventCallback.Factory.Create<CharacterSummary>(this, c => chosen = c));

		cut.Find(".account-panel-switch-btn").Click();
		cut.FindAll(".account-panel-character").Single(r => r.TextContent.Contains("Beta")).Click();

		await Assert.That(chosen).IsEqualTo(beta);
		// Selecting closes the panel — the same UX every character-switching surface in the app uses.
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel").Count != 0)
				throw new InvalidOperationException("panel still open after selecting");
		});
	}

	[Test]
	public async Task Account_Management_routes_to_slash_account()
	{
		var cut = RenderPanel(initialOpen: true);

		var link = cut.FindAll(".account-panel-item").Single(el => el.TextContent.Contains("Account Management"));
		await Assert.That(link.TagName).IsEqualTo("A");
		await Assert.That(link.GetAttribute("href")).IsEqualTo("/account");

		link.Click();

		// Clicking closes the panel — proof the same element's @onclick handler actually ran
		// alongside the href, not just that the href attribute happens to be correct.
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel").Count != 0)
				throw new InvalidOperationException("panel still open after clicking Account Management");
		});
	}

	[Test]
	public async Task Logout_invokes_OnLogout()
	{
		var invoked = false;
		var cut = RenderPanel(initialOpen: true, onLogout: EventCallback.Factory.Create(this, () => invoked = true));

		cut.FindAll(".account-panel-item").Single(el => el.TextContent.Contains("Logout")).Click();

		await Assert.That(invoked).IsTrue();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel").Count != 0)
				throw new InvalidOperationException("panel still open after logout");
		});
	}

	[Test]
	public async Task No_characters_renders_the_NoCharacter_state_without_a_submenu()
	{
		var cut = RenderPanel(characters: [], initialOpen: true);

		await Assert.That(cut.FindAll(".account-panel-item--inert").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".account-panel-item--inert").TextContent).Contains("No characters");

		// The submenu section must not exist at all — not just be unreachable.
		await Assert.That(cut.FindAll(".account-panel-level--submenu").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".account-panel-character").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".account-panel-switch-btn").Count).IsEqualTo(0);

		// Account Management and Logout are unaffected by an empty roster.
		await Assert.That(cut.Markup).Contains("Account Management");
		await Assert.That(cut.Markup).Contains("Logout");
	}
}
