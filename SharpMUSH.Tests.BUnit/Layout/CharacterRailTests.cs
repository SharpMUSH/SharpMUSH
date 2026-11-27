using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>Signs in with <paramref name="characters"/>; refuses a switch, once <paramref name="hold"/> lets it answer.</summary>
file sealed class RailApiHandler(IReadOnlyList<CharacterSummary> characters, Task? hold = null) : HttpMessageHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (hold is not null && request.RequestUri!.AbsolutePath.TrimStart('/') == "api/auth/switch-character") await hold;
		return Answer(request);
	}

	private HttpResponseMessage Answer(HttpRequestMessage request) =>
		request.RequestUri!.AbsolutePath.TrimStart('/') == "api/auth/account-login"
			? new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "acct-1",
					username = "headwiz",
					characters,
					accountSessionToken = "session-token-1",
					mustChangePassword = false,
					role = "Player",
					permissions = Array.Empty<string>(),
				})
			}
			: new HttpResponseMessage(HttpStatusCode.NotFound);
}

/// <summary>
/// The client's rail as a phone shows it (a row in Play's menu): one portrait per character of the account, what
/// the ones behind have unread, and a click on one not connected playing it.
/// </summary>
public class CharacterRailTests : TrackingBunitContext
{
	private static readonly CharacterSummary Ash = new(5, 1L, "Ash", "");
	private static readonly CharacterSummary Brin = new(6, 1L, "Brin", "");
	private static readonly CharacterSummary Cole = new(7, 1L, "Cole", "");

	public CharacterRailTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true));
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private async Task<CharacterSessionRegistry> SignedInAsync(bool signedIn = true, Task? hold = null)
	{
		var api = Track(new HttpClient(new RailApiHandler([Ash, Brin, Cole], hold)) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(api);
		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		// Read the empty session store first: read after the login, it would sign the tab out.
		await auth.InitAsync();
		if (signedIn && await auth.LoginAsync("headwiz", "password") is ApiFailure failure)
			throw new InvalidOperationException(failure.Message);

		var registry = new CharacterSessionRegistry(Session("#5:1", unread: 0), () => Session("#6:1", unread: 3));
		Services.AddSingleton(factory);
		Services.AddSingleton(auth);
		Services.AddSingleton(registry);
		Services.AddSingleton(Substitute.For<IConnectionStateService>());
		Services.AddSingleton<TerminalResumeStore>();
		Services.AddSingleton(new TerminalServiceHost(() => Substitute.For<ITerminalService>()));
		Services.AddSingleton<CharacterSwitchService>();
		Services.AddSingleton<ClientCharacters>();
		return registry;
	}

	private static CharacterSession Session(string objid, int unread)
	{
		var terminal = Substitute.For<IPlayTerminalService>();
		terminal.IsConnected.Returns(true);
		terminal.Identity.Returns(new TerminalIdentity("headwiz", objid));
		terminal.OobChannels.Returns(new OobChannelStore());
		var comms = Substitute.For<ICommFeed>();
		comms.Channels.Returns([new CommChannel("Public", unread)]);
		comms.Conversations.Returns([]);
		return new CharacterSession(new PlayTerminalServiceHost(() => terminal), comms, Substitute.For<IChannelWho>());
	}

	[Test]
	public async Task Every_account_character_has_a_portrait_and_the_ones_behind_show_what_they_have_unread()
	{
		var registry = await SignedInAsync();
		registry.Open();

		var cut = Render<CharacterRail>(p => p.Add(r => r.Strip, true));

		var portraits = cut.FindAll(".client-char");
		await Assert.That(portraits.Select(portrait => portrait.GetAttribute("title"))).IsEquivalentTo(new[] { "Ash", "Brin", "Cole" });
		await Assert.That(portraits[0].GetAttribute("aria-current")).IsEqualTo("true");
		await Assert.That(portraits[1].HasAttribute("aria-current")).IsFalse();
		await Assert.That(portraits[1].QuerySelector(".client-char-count")?.TextContent).IsEqualTo("3");
		await Assert.That(portraits[0].QuerySelector(".client-char-count")).IsNull();
		await Assert.That(portraits[2].ClassList.Contains("client-char--off")).IsTrue().Because("Cole is not connected in this tab");
	}

	[Test]
	public async Task There_is_no_add_button()
	{
		var registry = await SignedInAsync();
		registry.Open();

		var cut = Render<CharacterRail>(p => p.Add(r => r.Strip, true));

		await Assert.That(cut.FindAll(".client-add")).IsEmpty();
	}

	[Test]
	public async Task A_visitor_sees_only_the_session_in_front()
	{
		await SignedInAsync(signedIn: false);

		var cut = Render<CharacterRail>(p => p.Add(r => r.Strip, true));

		await Assert.That(cut.FindAll(".client-char").Count).IsEqualTo(1);
	}

	[Test]
	public async Task A_character_that_could_not_be_played_is_named_in_a_notice()
	{
		var registry = await SignedInAsync();
		registry.Open();
		var snackbar = Substitute.For<ISnackbar>();
		Services.AddSingleton(snackbar);
		var cut = Render<CharacterRail>(p => p.Add(r => r.Strip, true));

		// The server refuses the switch (this fake answers it 404).
		await cut.FindAll(".client-char")[2].ClickAsync();

		var notice = snackbar.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(ISnackbar.Add)).GetArguments();
		await Assert.That(notice[0] as string).Contains("NavClientSwitchFailed");
		await Assert.That(notice[0] as string).Contains("Cole");
		await Assert.That(notice[1]).IsEqualTo(Severity.Error);
	}

	/// <summary>
	/// A click while a switch is under way is not lost: it waits for that switch, and of several such clicks only the
	/// last is played.
	/// </summary>
	[Test]
	public async Task A_click_during_a_switch_waits_for_it_and_only_the_last_one_is_played()
	{
		var answer = new TaskCompletionSource();
		var registry = await SignedInAsync(hold: answer.Task);
		registry.Open();
		var snackbar = Substitute.For<ISnackbar>();
		Services.AddSingleton(snackbar);
		var cut = Render<CharacterRail>(p => p.Add(r => r.Strip, true));

		var first = cut.FindAll(".client-char")[2].ClickAsync();
		await cut.FindAll(".client-char")[1].ClickAsync();
		await cut.FindAll(".client-char")[2].ClickAsync();
		await Assert.That(cut.FindAll(".client-char").Any(portrait => portrait.HasAttribute("disabled"))).IsFalse();
		answer.SetResult();
		await first.WaitAsync(TimeSpan.FromSeconds(5));

		// This fake refuses every switch, so each one played is named in a notice: Cole twice, Brin never.
		var named = snackbar.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ISnackbar.Add))
			.Select(call => call.GetArguments()[0] as string)
			.ToList();
		await Assert.That(named.Count).IsEqualTo(2);
		await Assert.That(named.All(text => text!.Contains("Cole"))).IsTrue();
	}
}
