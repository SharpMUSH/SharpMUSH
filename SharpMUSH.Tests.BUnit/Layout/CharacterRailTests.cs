using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
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

file sealed class RailApiHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(request.RequestUri!.AbsolutePath.TrimStart('/') == "api/auth/account-login"
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
			: new HttpResponseMessage(HttpStatusCode.NotFound));
}

/// <summary>
/// The client's rail as a phone shows it (a row in Play's menu): one portrait per character the tab keeps, what
/// the ones behind have unread, and Add listing the account's characters under the row.
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

	private async Task<CharacterSessionRegistry> SignedInAsync(bool signedIn = true)
	{
		var api = Track(new HttpClient(new RailApiHandler([Ash, Brin, Cole])) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(api);
		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
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
	public async Task Each_kept_character_has_a_portrait_and_the_ones_behind_show_what_they_have_unread()
	{
		var registry = await SignedInAsync();
		registry.Open();

		var cut = Render<CharacterRail>(p => p.Add(r => r.Strip, true));

		var portraits = cut.FindAll(".client-char");
		await Assert.That(portraits.Count).IsEqualTo(2);
		await Assert.That(portraits[0].GetAttribute("aria-current")).IsEqualTo("true");
		await Assert.That(portraits[1].HasAttribute("aria-current")).IsFalse();
		await Assert.That(portraits[1].QuerySelector(".client-char-count")?.TextContent).IsEqualTo("3");
		await Assert.That(portraits[0].QuerySelector(".client-char-count")).IsNull();
	}

	[Test]
	public async Task Add_lists_the_accounts_characters_under_the_row_with_log_out_for_the_ones_behind()
	{
		var registry = await SignedInAsync();
		registry.Open();
		var cut = Render<CharacterRail>(p => p.Add(r => r.Strip, true));

		await cut.Find(".client-add").ClickAsync();

		var rows = cut.FindAll(".client-strip > .client-add-panel .client-add-row");
		await Assert.That(rows.Select(row => row.QuerySelector(".client-add-name")!.TextContent)).IsEquivalentTo(new[] { "Ash", "Brin", "Cole" });
		await Assert.That(rows[0].QuerySelector(".client-add-out")).IsNull().Because("the character in front is not logged out from here");
		await Assert.That(rows[1].QuerySelector(".client-add-out")).IsNotNull();
		await Assert.That(rows[2].QuerySelector(".client-add-out")).IsNull().Because("Cole is not playing in this tab");
	}

	[Test]
	public async Task A_visitor_has_no_Add()
	{
		await SignedInAsync(signedIn: false);

		var cut = Render<CharacterRail>(p => p.Add(r => r.Strip, true));

		await Assert.That(cut.FindAll(".client-char").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".client-add")).IsEmpty();
	}
}
